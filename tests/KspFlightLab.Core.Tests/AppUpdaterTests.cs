// KSP Flight Lab · tests/KspFlightLab.Core.Tests/AppUpdaterTests.cs
// [Test] Flight Lab updating itself: picking the release, verified download, the exe swap, clean-up, dev builds.
//        From Mission Control's AppUpdaterTests.
// Used by: dotnet test
// Guide:   Meat and Potatoes/05-Install-And-Settings.md

using System.Net;
using KspFlightLab.Core.Install;

namespace KspFlightLab.Core.Tests;

public class AppUpdaterTests
{
    public const string ExeUrl = "https://github.com/NEON-NULL-NARWHAL/ksp-flight-lab/releases/download/v9.9.9/KspFlightLab.exe";

    /// <summary>"An exe": just enough to pass the MZ check (no version info, so the version check is skipped).</summary>
    public static byte[] FakeExe(string text) => System.Text.Encoding.ASCII.GetBytes("MZ" + text);

    /// <summary>Flight Lab's releases: a newer pre-release, a newer one with the exe and its digest, an older one.</summary>
    public static string ReleasesJson(byte[] exe, bool withDigest = true)
    {
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(exe));
        object Release(string tag, bool prerelease, object[] assets) => new
        {
            tag_name = tag, prerelease, draft = false, html_url = "https://github.com/x", published_at = "2026-10-26T12:00:00Z",
            body = $"## {tag}" + "\n" + "New things.", assets,
        };
        object Asset(string url, int size, string? digest) => new { name = AppUpdater.ExeName, browser_download_url = url, size, digest };
        return System.Text.Json.JsonSerializer.Serialize(new[]
        {
            Release("v10.0.0", true, [Asset(ExeUrl.Replace("v9.9.9", "v10.0.0"), exe.Length, "sha256:" + sha)]), // pre-release: skipped
            Release("v9.9.9", false, [Asset(ExeUrl, exe.Length, withDigest ? "sha256:" + sha : null)]),
            Release("v0.1.0", false, [Asset(ExeUrl.Replace("v9.9.9", "v0.1.0"), exe.Length, "sha256:" + sha)]),
        });
    }

    private static string TempExe(string text)
    {
        var exe = Path.Combine(TestFolders.Temp("app"), AppUpdater.ExeName);
        File.WriteAllBytes(exe, FakeExe(text));
        return exe;
    }

    [Fact]
    public async Task Finds_the_newest_verifiable_release_newer_than_this_one()
    {
        var exe = FakeExe("new");
        var updater = new AppUpdater(new FakeHttp().Text(AppUpdater.ReleasesUrl, ReleasesJson(exe)).Client);

        var update = await updater.CheckAsync(new Version(0, 1, 0));
        Assert.Equal(("v9.9.9", new Version(9, 9, 9)), (update!.Release.Tag, update.Version)); // not the pre-release
        Assert.Contains("New things", update.Release.Notes);
        Assert.Null(await updater.CheckAsync(new Version(9, 9, 9))); // already newest
        Assert.Null(await new AppUpdater(new FakeHttp().Text(AppUpdater.ReleasesUrl, ReleasesJson(exe, withDigest: false)).Client)
            .CheckAsync(new Version(0, 1, 0))); // no checksum: never offered
    }

    [Fact]
    public async Task Explains_when_github_wont_show_the_releases()
    {
        var hidden = new AppUpdater(new FakeHttp().Status(AppUpdater.ReleasesUrl, HttpStatusCode.NotFound).Client);
        var e = await Assert.ThrowsAsync<AppUpdateUnavailableException>(() => hidden.CheckAsync(new Version(0, 1, 0)));
        Assert.Contains("private", e.Message);
    }

    [Fact]
    public async Task Downloads_verifies_and_swaps_the_running_exe()
    {
        var newExe = FakeExe("new version");
        var updater = new AppUpdater(new FakeHttp().Text(AppUpdater.ReleasesUrl, ReleasesJson(newExe)).Bytes(ExeUrl, newExe).Client);
        var current = TempExe("old version");

        var update = (await updater.CheckAsync(new Version(0, 1, 0)))!;
        var downloaded = await updater.DownloadAsync(update);
        var old = AppUpdater.Install(downloaded, current, update);

        Assert.Equal(newExe, File.ReadAllBytes(current));
        Assert.Equal(FakeExe("old version"), File.ReadAllBytes(old));
        Assert.False(File.Exists(current + ".new"));
        AppUpdater.CleanUp(current); // what the new version does on startup
        Assert.False(File.Exists(old));
    }

    [Fact]
    public async Task Refuses_a_download_that_doesnt_match_and_leaves_the_app_alone()
    {
        var updater = new AppUpdater(new FakeHttp().Text(AppUpdater.ReleasesUrl, ReleasesJson(FakeExe("what GitHub says")))
            .Bytes(ExeUrl, FakeExe("tampered on the way")).Client);
        var current = TempExe("old version");
        var update = (await updater.CheckAsync(new Version(0, 1, 0)))!;

        await Assert.ThrowsAsync<DownloadVerificationException>(() => updater.DownloadAsync(update));

        // And if the file changes after downloading, the swap checks again.
        var swapped = Path.Combine(Path.GetDirectoryName(current)!, "download.exe");
        File.WriteAllBytes(swapped, FakeExe("swapped later"));
        Assert.Throws<DownloadVerificationException>(() => AppUpdater.Install(swapped, current, update));
        Assert.Equal(FakeExe("old version"), File.ReadAllBytes(current));
        Assert.False(File.Exists(current + ".old"));
        Assert.False(File.Exists(current + ".new"));
    }

    [Fact]
    public void Refuses_something_that_isnt_a_program()
    {
        var notExe = System.Text.Encoding.ASCII.GetBytes("<html>surprise</html>");
        var current = TempExe("old version");
        var download = Path.Combine(Path.GetDirectoryName(current)!, "download.exe");
        File.WriteAllBytes(download, notExe);
        var update = new AppUpdate(new Release("v9.9.9", new Version(9, 9, 9), false, DateTimeOffset.Now, "", []),
            new ReleaseAsset(AppUpdater.ExeName, ExeUrl, notExe.Length, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(notExe))));

        if (OperatingSystem.IsWindows())
            Assert.Throws<DownloadVerificationException>(() => AppUpdater.Install(download, current, update));
        Assert.Equal(FakeExe("old version"), File.ReadAllBytes(current));
    }

    [Fact]
    public void Only_the_published_single_file_exe_updates_itself()
    {
        var exe = TempExe("x");
        Assert.True(AppUpdater.CanUpdateItself(exe));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(exe)!, "KspFlightLab.dll"), "");
        Assert.False(AppUpdater.CanUpdateItself(exe)); // a development build
        Assert.False(AppUpdater.CanUpdateItself(Path.Combine(Path.GetDirectoryName(exe)!, "dotnet.exe")));
        Assert.False(AppUpdater.CanUpdateItself(null));
    }
}
