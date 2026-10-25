// Flight Lab · src/LunaEvo.FlightLab.Core/Wingman/WingmanTools.cs
// [Core] KSP Wingman as a tool Flight Lab installs: its release (GitHub, SHA-256 checked) into Flight Lab's tools
//        folder, its bridge mod into a KSP, kRPC checked, and a local model server (Ollama) found and given a model.
// Used by: SetupPage, AgentsPage
// Guide:   Meat and Potatoes/05-Install-And-Settings.md

using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using LunaEvo.FlightLab.Core.Install;

namespace LunaEvo.FlightLab.Core.Wingman;

/// <summary>A KSP Wingman version unpacked in Flight Lab's tools folder.</summary>
public sealed record WingmanInstall(string Version, string Folder)
{
    /// <summary>Its programs' names on this system (".exe" on Windows; none on Linux).</summary>
    public static string ProgramName(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    public string Console => Path.Combine(Folder, ProgramName("KspWingman"));
    public string McpServer => Path.Combine(Folder, ProgramName("KspWingman.Mcp"));
    public string Bridge => Path.Combine(Folder, "GameData", "KspWingman");
}

public enum BridgeState { NoKsp, NotInstalled, Different, Installed }

public static class WingmanTools
{
    public const string ReleasesUrl = "https://api.github.com/repos/NEON-NULL-NARWHAL/ksp-wingman/releases?per_page=10";
    public const long MaxUnpackedBytes = 400L * 1024 * 1024;
    public const int MaxEntries = 200;

    /// <summary>Where versions are unpacked (one folder each): Flight Lab's Tools/KspWingman (MainWindow may move it next to its settings).</summary>
    public static string Root { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaEvo", "FlightLab", "Tools", "KspWingman");

    /// <summary>The newest unpacked version that has both programs, or null.</summary>
    public static WingmanInstall? Installed()
    {
        if (!Directory.Exists(Root))
            return null;
        return Directory.GetDirectories(Root)
            .Select(d => new WingmanInstall(Path.GetFileName(d), d))
            .Where(i => File.Exists(i.Console) && File.Exists(i.McpServer) && Version.TryParse(i.Version, out _))
            .OrderByDescending(i => Version.Parse(i.Version))
            .FirstOrDefault();
    }

    /// <summary>The newest release and its zip (KspWingman-&lt;version&gt;.zip), only when GitHub gives its SHA-256.</summary>
    public static async Task<(Release Release, ReleaseAsset Zip)?> LatestAsync(HttpClient http, CancellationToken cancel = default)
    {
        foreach (var release in await GitHubReleases.GetAllAsync(http, ReleasesUrl, includePrereleases: false, cancel))
            if (release.Version != null && release.Assets.FirstOrDefault(a => a.Name == $"KspWingman-{release.Version}.zip" && a.Sha256 != null) is { } zip)
                return (release, zip);
        return null;
    }

    /// <summary>Downloads (GitHub only, checked against its SHA-256) and unpacks a release. Returns the install.</summary>
    public static async Task<WingmanInstall> DownloadAndInstallAsync(HttpClient http, Release release, ReleaseAsset zip, IProgress<double>? progress = null)
    {
        var path = await VerifiedDownload.DownloadAsync(http, zip, progress);
        try
        {
            return await Task.Run(() => Unpack(path, release.Version!.ToString(), zip.Sha256!));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Unpacks a release zip into Root/&lt;version&gt;: only the two programs, the bridge mod and the docs, nothing outside
    /// the folder, nothing too big. The checksum is checked again here (the file could have changed since the download).
    /// </summary>
    public static WingmanInstall Unpack(string zipPath, string version, string expectedSha256)
    {
        if (VerifiedDownload.Sha256Of(zipPath) != expectedSha256)
            throw new DownloadVerificationException("KSP Wingman's download doesn't match GitHub's checksum. Nothing was installed.");
        if (!Version.TryParse(version, out _))
            throw new InvalidDataException($"Not a version: {version}");
        var folder = Path.Combine(Root, version);
        var staging = folder + ".staging-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                if (zip.Entries.Count > MaxEntries || zip.Entries.Sum(e => e.Length) > MaxUnpackedBytes)
                    throw new InvalidDataException("That doesn't look like KSP Wingman (far too big).");
                var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    var wanted = name == WingmanInstall.ProgramName("KspWingman") || name == WingmanInstall.ProgramName("KspWingman.Mcp") || name is "README.md" or "LICENSE" or "CHANGELOG.md" ||
                                 name.StartsWith("GameData/KspWingman/", StringComparison.Ordinal);
                    if (!wanted || name.EndsWith('/'))
                        continue;
                    var destination = Path.GetFullPath(Path.Combine(root, name));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"That zip has a file outside its folder ({entry.FullName}). Nothing was installed.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination);
                }
            }
            var install = new WingmanInstall(version, staging);
            if (!File.Exists(install.Console) || !File.Exists(install.McpServer) || !Directory.Exists(install.Bridge))
                throw new InvalidDataException("That zip isn't a KSP Wingman release (its programs or its mod are missing).");
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            Directory.Move(staging, folder);
            return new WingmanInstall(version, folder);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>Whether kRPC (which Wingman talks through) is in that KSP.</summary>
    public static bool HasKrpc(string kspFolder) =>
        Directory.Exists(Path.Combine(kspFolder, "GameData", "kRPC")) &&
        Directory.EnumerateFiles(Path.Combine(kspFolder, "GameData", "kRPC"), "KRPC*.dll", SearchOption.AllDirectories).Any();

    /// <summary>The bridge mod in that KSP: missing, another version, or this install's.</summary>
    public static BridgeState Bridge(string kspFolder, WingmanInstall install)
    {
        if (!KspFolder.IsKspFolder(kspFolder))
            return BridgeState.NoKsp;
        var dll = Path.Combine(kspFolder, "GameData", "KspWingman", "KspWingman.Bridge.dll");
        if (!File.Exists(dll))
            return BridgeState.NotInstalled;
        var ours = Path.Combine(install.Bridge, "KspWingman.Bridge.dll");
        return File.Exists(ours) && VerifiedDownload.Sha256Of(ours) == VerifiedDownload.Sha256Of(dll) ? BridgeState.Installed : BridgeState.Different;
    }

    /// <summary>Copies the bridge mod into that KSP's GameData (KSP must be closed); replaces an older one.</summary>
    public static void InstallBridge(string kspFolder, WingmanInstall install)
    {
        if (!KspFolder.IsKspFolder(kspFolder))
            throw new InvalidOperationException("That isn't a KSP folder (no KSP program and GameData in it).");
        if (KspFolder.IsRunning(kspFolder))
            throw new InvalidOperationException("Close KSP first.");
        var target = Path.Combine(kspFolder, "GameData", "KspWingman");
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        foreach (var file in Directory.EnumerateFiles(install.Bridge, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            var to = Path.Combine(target, Path.GetRelativePath(install.Bridge, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
    }

    public const string OllamaUrl = "http://127.0.0.1:11434/";
    /// <summary>The model KSP Wingman is tested with as pilot and planner (0.8+: 24 of 24 orders, the best planner).</summary>
    public const string DefaultModel = "gemma4:e4b";
    private const int MaxOllamaBytes = 1024 * 1024;

    /// <summary>Ollama on this PC: its version and its models, or null when it isn't running.</summary>
    public static async Task<(string Version, IReadOnlyList<string> Models)?> OllamaAsync(HttpClient http, string baseUrl = OllamaUrl, CancellationToken cancel = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var version = Text(await Json(http, new Uri(new Uri(baseUrl), "api/version"), timeout.Token), "version") ?? "?";
            var tags = await Json(http, new Uri(new Uri(baseUrl), "api/tags"), timeout.Token);
            var models = new List<string>();
            if (tags.TryGetProperty("models", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var m in list.EnumerateArray())
                    if (m.ValueKind == JsonValueKind.Object && Text(m, "name") is { } name)
                        models.Add(name);
            return (version, models);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Asks Ollama to download a model (gigabytes: minutes). Ollama streams one JSON line per step ({"status","total",
    /// "completed"}, or {"error"}); progress goes 0..1. Returns null when done, or what went wrong.
    /// </summary>
    public static async Task<string?> PullAsync(HttpClient http, string model, IProgress<double>? progress = null, string baseUrl = OllamaUrl, CancellationToken cancel = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "api/pull")) { Content = JsonContent.Create(new { model, stream = true }) };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode)
                return $"Ollama said {(int)response.StatusCode}.";
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancel));
            string? status = null;
            while (await reader.ReadLineAsync(cancel) is { } line)
            {
                if (line.Length > MaxOllamaBytes)
                    return "Ollama's answer is unexpectedly large.";
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (Text(doc.RootElement, "error") is { } error)
                        return "Ollama: " + error;
                    status = Text(doc.RootElement, "status") ?? status;
                    if (doc.RootElement.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number && total.TryGetInt64(out var t) && t > 0 &&
                        doc.RootElement.TryGetProperty("completed", out var done) && done.ValueKind == JsonValueKind.Number && done.TryGetInt64(out var d))
                        progress?.Report(Math.Clamp((double)d / t, 0, 1));
                }
                catch (JsonException)
                {
                    // Not a line we know: keep reading.
                }
            }
            return status == "success" ? null : $"Ollama stopped before it finished ({status ?? "no answer"}).";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return "Ollama didn't answer: " + e.Message;
        }
    }

    private static async Task<JsonElement> Json(HttpClient http, Uri url, CancellationToken cancel)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancel);
        if (bytes.Length > MaxOllamaBytes)
            throw new JsonException("Ollama's answer is unexpectedly large.");
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    private static string? Text(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
