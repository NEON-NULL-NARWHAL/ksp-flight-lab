// Flight Lab · src/LunaEvo.FlightLab.Core/LabHttp.cs
// [Core] The one HttpClient for GitHub and Ollama (tests swap in a fake so nothing goes on the network), and the
//        error log for what nobody expected.
// Used by: SetupPage, Program
// Guide:   Meat and Potatoes/05-Install-And-Settings.md

namespace LunaEvo.FlightLab.Core;

public static class LabHttp
{
    private static HttpClient? _client;

    /// <summary>GitHub's API wants a User-Agent; 30 s is enough for its answers (downloads stream past it).</summary>
    public static HttpClient Client
    {
        get => _client ??= Create();
        set => _client = value;
    }

    private static HttpClient Create()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LunaEvo-FlightLab");
        return http;
    }
}

public static class ErrorLog
{
    /// <summary>%APPDATA%\LunaEvo\FlightLab\logs\errors.log (Windows), ~/.config/LunaEvo/FlightLab/logs (Linux).</summary>
    public static string Path { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LunaEvo", "FlightLab", "logs", "errors.log");

    public const long MaxBytes = 5 * 1024 * 1024;

    public static void Write(string where, Exception e)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            if (!File.Exists(Path) || new FileInfo(Path).Length < MaxBytes)
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] {e}\n\n");
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            // Nowhere to write it: nothing more to do.
        }
    }
}
