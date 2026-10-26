// KSP Flight Lab · src/KspFlightLab.Core/LabSettings.cs
// [Core] Flight Lab's own settings (the server and KSP folders, the models, the tower, the look), kept as JSON in the
//        user's settings folder (%APPDATA%\KspFlightLab on Windows, ~/.config/KspFlightLab on Linux).
// Used by: MainWindow, SettingsPage, SetupPage, AgentsPage, TowerPage
// Guide:   Meat and Potatoes/05-Install-And-Settings.md

using System.Text.Json;

namespace KspFlightLab.Core;

/// <param name="ServerFolder">The LMP server the agents fly on (its LunaEvo folder keeps the tower's knowledge and radio).</param>
/// <param name="KspFolder">The agents' KSP (for Wingman's mod and kRPC).</param>
/// <param name="TowerOnStart">Start the KSC tower when Flight Lab starts.</param>
/// <param name="TowerChat">The tower talks on LMP chat (through the LUNA EVO server plugin).</param>
/// <param name="CheckForUpdates">Ask GitHub for a newer Flight Lab when it starts.</param>
public sealed record LabSettings(
    string? ServerFolder = null,
    string? KspFolder = null,
    string PilotModel = Wingman.WingmanTools.DefaultModel,
    string PlannerModel = Wingman.WingmanTools.DefaultModel,
    string LlmUrl = "http://127.0.0.1:11434/v1/",
    int TowerPort = Tower.TowerServer.DefaultPort,
    bool TowerOnStart = true,
    bool TowerChat = false,
    string AccentColor = LabSettings.DefaultAccent,
    string Theme = "Dark",
    bool CheckForUpdates = true)
{
    /// <summary>The family's green (Mission Control's and Wingman's too).</summary>
    public const string DefaultAccent = "#3FAE2A";
}

public static class LabSettingsStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KspFlightLab", "settings.json");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The saved settings, or the defaults (no file, or one that can't be read: kept as .bad).</summary>
    public static LabSettings Load(string path)
    {
        if (!File.Exists(path))
            return new LabSettings();
        try
        {
            if (new FileInfo(path).Length > 1024 * 1024)
                throw new JsonException("too big");
            return JsonSerializer.Deserialize<LabSettings>(File.ReadAllText(path), Json) ?? new LabSettings();
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or IOException)
        {
            try { File.Copy(path, path + ".bad", overwrite: true); } catch (IOException) { }
            return new LabSettings();
        }
    }

    public static void Save(string path, LabSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Where the agents' knowledge is kept: in the server's LunaEvo folder (so every agent on that server shares it, and
    /// the knowledge LUNA EVO Mission Control kept before is the same file), else next to the settings.
    /// </summary>
    public static string KnowledgePath(LabSettings settings, string settingsPath) =>
        settings.ServerFolder is { Length: > 0 } server && Directory.Exists(server)
            ? Path.Combine(server, "LunaEvo", "agents", "knowledge.json")
            : Path.Combine(Path.GetDirectoryName(settingsPath)!, "agents", "knowledge.json");

    /// <summary>The server's radio file for the LUNA EVO server plugin, or null without a server.</summary>
    public static string? RadioPath(LabSettings settings) =>
        settings.ServerFolder is { Length: > 0 } server && Directory.Exists(server) ? Path.Combine(server, "LunaEvo", "radio.jsonl") : null;
}
