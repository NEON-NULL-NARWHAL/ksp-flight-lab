// KSP Flight Lab · src/KspFlightLab.Core/Install/GitHubReleases.cs
// [Core] Reads a GitHub repo's releases (KSP Wingman's, Flight Lab's own) defensively: names, versions, assets and the
//        SHA-256 GitHub publishes for each. From LUNA EVO Mission Control's LmpReleases.
// Used by: WingmanTools
// Guide:   Meat and Potatoes/05-Install-And-Settings.md

using System.Text.Json;
using System.Text.RegularExpressions;

namespace KspFlightLab.Core.Install;

/// <param name="Sha256">The SHA-256 GitHub publishes for the file (lowercase hex), or null if it has none.</param>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size, string? Sha256 = null);

public sealed record Release(string Tag, Version? Version, bool Prerelease, DateTimeOffset PublishedAt, string PageUrl, IReadOnlyList<ReleaseAsset> Assets, string Notes = "")
{
    public override string ToString() => Prerelease ? $"{Tag} (pre-release)" : Tag;
}

public static partial class GitHubReleases
{
    public const int MaxFeedBytes = 10 * 1024 * 1024;

    /// <summary>Every non-draft release at a GitHub releases API URL, newest first.</summary>
    public static async Task<IReadOnlyList<Release>> GetAllAsync(HttpClient http, string url, bool includePrereleases, CancellationToken cancel = default)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxFeedBytes)
            throw new JsonException("GitHub's release list is unexpectedly large.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        // A page of releases is well under 1 MB; never buffer more than MaxFeedBytes whatever arrives.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancel)) > 0)
        {
            if (buffer.Length + read > MaxFeedBytes)
                throw new JsonException("GitHub's release list is unexpectedly large.");
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        using var json = await JsonDocument.ParseAsync(buffer, cancellationToken: cancel);

        // GitHub's JSON is outside our control: read it defensively, skipping anything that doesn't look right.
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("GitHub didn't return a list of releases.");
        var releases = new List<Release>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Bool(item, "draft"))
                continue;
            var prerelease = Bool(item, "prerelease");
            if (prerelease && !includePrereleases)
                continue;
            var tag = Str(item, "tag_name");
            var assets = item.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray()
                    .Where(a => a.ValueKind == JsonValueKind.Object)
                    .Select(a => new ReleaseAsset(Str(a, "name"), Str(a, "browser_download_url"),
                        a.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var n) ? n : 0,
                        Digest(Str(a, "digest"))))
                    .ToList()
                : [];
            releases.Add(new Release(tag, ParseVersion(tag), prerelease,
                item.TryGetProperty("published_at", out var published) && published.ValueKind == JsonValueKind.String && published.TryGetDateTimeOffset(out var when) ? when : DateTimeOffset.MinValue,
                Str(item, "html_url"), assets,
                Str(item, "body") is { Length: > 4000 } body ? body[..4000] + "…" : Str(item, "body")));
        }
        return releases.OrderByDescending(r => r.PublishedAt).ToList();
    }

    /// <summary>GitHub publishes "sha256:&lt;64 hex&gt;" per asset. Anything else counts as "no checksum".</summary>
    public static string? Digest(string text) =>
        text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && text.Length == 71 && text[7..].All(Uri.IsHexDigit)
            ? text[7..].ToLowerInvariant()
            : null;

    /// <summary>"v0.12.0" or "0.12.0-preview" → 0.12.0; null without a version in it.</summary>
    public static Version? ParseVersion(string? text)
    {
        var match = text == null ? null : VersionPattern().Match(text);
        return match is { Success: true } && Version.TryParse(match.Value, out var version) ? version : null;
    }

    private static string Str(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool Bool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"\d+\.\d+(\.\d+)?")]
    private static partial Regex VersionPattern();
}
