// Flight Lab · src/LunaEvo.FlightLab.Core/Install/VerifiedDownload.cs
// [Core] Downloads a release asset and checks it: from GitHub over HTTPS (after redirects too), the size GitHub gave,
//        and the SHA-256 GitHub published. From LUNA EVO Mission Control's ServerInstaller (its audit rules).
// Used by: WingmanTools
// Guide:   Meat and Potatoes/05-Install-And-Settings.md

using System.Security.Cryptography;

namespace LunaEvo.FlightLab.Core.Install;

/// <summary>A download that failed its safety checks (wrong checksum, wrong size, untrusted address). Nothing was installed.</summary>
public sealed class DownloadVerificationException(string message) : Exception(message);

public static class VerifiedDownload
{
    /// <summary>The largest download it accepts (KSP Wingman's zip is about 65 MB).</summary>
    public const long MaxDownloadBytes = 500L * 1024 * 1024;

    /// <summary>Only GitHub over HTTPS. Release downloads start at github.com and redirect to *.githubusercontent.com.</summary>
    public static bool IsTrusted(Uri? uri) =>
        uri is { Scheme: "https" } &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Downloads into a fresh temp folder and checks it. Any failure deletes the file and throws
    /// <see cref="DownloadVerificationException"/>. Returns the file's path.
    /// </summary>
    public static async Task<string> DownloadAsync(HttpClient http, ReleaseAsset asset, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var url) || !IsTrusted(url))
            throw new DownloadVerificationException($"Refusing to download from {asset.DownloadUrl}: Flight Lab only downloads from GitHub over HTTPS.");
        if (asset.Sha256 == null)
            throw new DownloadVerificationException($"GitHub has no checksum for {asset.Name}, so it can't be verified.");
        if (asset.Size > MaxDownloadBytes)
            throw new DownloadVerificationException($"{asset.Name} is {asset.Size / 1024 / 1024} MB, which is too big.");

        // A new folder per download: nothing else can have prepared a file at this path.
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "LunaEvo", "FlightLab", "downloads", Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(folder, SafeFileName(asset.Name));
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            if (!IsTrusted(response.RequestMessage?.RequestUri ?? url))
                throw new DownloadVerificationException($"The download was redirected to {response.RequestMessage?.RequestUri?.Host}, which isn't GitHub.");
            var total = response.Content.Headers.ContentLength ?? asset.Size;
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long done = 0;
            await using (var target = File.Create(path))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    done += read;
                    if (done > MaxDownloadBytes)
                        throw new DownloadVerificationException($"{asset.Name} is bigger than it can be.");
                    sha.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                    if (total > 0)
                        progress?.Report(Math.Min(1, (double)done / total));
                }
            }
            if (asset.Size > 0 && done != asset.Size)
                throw new DownloadVerificationException($"{asset.Name} should be {asset.Size} bytes but {done} arrived. The download was incomplete or altered.");
            if (Convert.ToHexStringLower(sha.GetHashAndReset()) != asset.Sha256)
                throw new DownloadVerificationException($"{asset.Name} doesn't match the checksum GitHub published for it, so it was rejected.");
            progress?.Report(1);
            return path;
        }
        catch
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>A file name without any folder parts or characters Windows or Linux refuse.</summary>
    public static string SafeFileName(string name)
    {
        var file = Path.GetFileName(name.Replace('\\', '/'));
        var safe = new string(file.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
        return safe.Length == 0 || safe.Trim('.').Length == 0 ? "download.bin" : safe;
    }
}
