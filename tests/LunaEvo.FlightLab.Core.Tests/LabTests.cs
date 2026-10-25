// Flight Lab · tests/LunaEvo.FlightLab.Core.Tests/LabTests.cs
// [Test] Flight Lab's own parts: its settings (saved, loaded, a bad file kept aside), where the knowledge and the radio
//        are kept, the sessions read from the agents' logs, KSP folders, and the tower's radio file for the plugin.
// Used by: dotnet test
// Guide:   Meat and Potatoes/05-Install-And-Settings.md

using System.Text.Json;
using LunaEvo.FlightLab.Core.Install;
using LunaEvo.FlightLab.Core.Tower;

namespace LunaEvo.FlightLab.Core.Tests;

public class LabTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Settings_are_saved_and_loaded_and_a_bad_file_is_kept_aside()
    {
        var path = Path.Combine(TestFolders.Temp("settings"), "settings.json");
        Assert.Equal(new LabSettings(), LabSettingsStore.Load(path)); // none yet: defaults
        Assert.Equal("gemma4:e4b", new LabSettings().PilotModel);
        Assert.Equal(TowerServer.DefaultPort, new LabSettings().TowerPort);

        var settings = new LabSettings(ServerFolder: @"D:\server", PilotModel: "other", TowerPort: 9000, Theme: "Light");
        LabSettingsStore.Save(path, settings);
        Assert.Equal(settings, LabSettingsStore.Load(path));
        Assert.False(File.Exists(path + ".tmp"));

        File.WriteAllText(path, "{ not json");
        Assert.Equal(new LabSettings(), LabSettingsStore.Load(path));
        Assert.Equal("{ not json", File.ReadAllText(path + ".bad"));
    }

    [Fact]
    public void Knowledge_and_radio_live_in_the_server_or_next_to_the_settings()
    {
        var settingsPath = Path.Combine(TestFolders.Temp("lab"), "settings.json");
        var server = TestFolders.Temp("server");
        Assert.Equal(Path.Combine(server, "LunaEvo", "agents", "knowledge.json"),
            LabSettingsStore.KnowledgePath(new LabSettings(ServerFolder: server), settingsPath)); // the same file Mission Control used
        Assert.Equal(Path.Combine(server, "LunaEvo", "radio.jsonl"), LabSettingsStore.RadioPath(new LabSettings(ServerFolder: server)));

        var gone = Path.Combine(server, "gone");
        Assert.Equal(Path.Combine(Path.GetDirectoryName(settingsPath)!, "agents", "knowledge.json"),
            LabSettingsStore.KnowledgePath(new LabSettings(ServerFolder: gone), settingsPath));
        Assert.Null(LabSettingsStore.RadioPath(new LabSettings()));
    }

    [Fact]
    public void Sessions_are_read_from_the_agents_logs_newest_first()
    {
        var logs = TestFolders.Temp("logs");
        File.WriteAllLines(Path.Combine(logs, "wingman-Alpha-2026-10-23.jsonl"),
        [
            """{"time":"2026-10-23T10:00:00Z","kind":"status","text":"Climbing"}""",
            """{"time":"2026-10-23T10:05:00Z","kind":"flight","text":"Flight landed: 1 m off the centerline."}""",
            "not json",
            """{"time":"2026-10-23T10:20:00Z","kind":"flight","text":"Flight crashed on final."}""",
        ]);
        File.WriteAllLines(Path.Combine(logs, "wingman-Bravo-2026-10-24.jsonl"),
            ["""{"time":"2026-10-24T09:00:00Z","kind":"test","text":"Flight test: pitch 30 °/s"}"""]);
        File.WriteAllText(Path.Combine(logs, "tower-2026-10-24.jsonl"), "");
        File.WriteAllText(Path.Combine(logs, "wingman-x-notadate.jsonl"), "");

        var sessions = SessionLog.List(logs);
        Assert.Equal(["Bravo", "Alpha"], sessions.Select(s => s.CallSign));
        var alpha = sessions[1];
        Assert.Equal(new DateOnly(2026, 10, 23), alpha.Day);
        Assert.Equal((2, 1, 1), (alpha.Flights, alpha.Landed, alpha.Crashed));
        Assert.DoesNotContain(alpha.Story, l => l.Kind == "status"); // the story only
        Assert.Empty(SessionLog.List(Path.Combine(logs, "none")));
        Assert.Null(SessionLog.Parse("""{"kind":"flight"}""", "Alpha")); // no time
    }

    [Fact]
    public void KSP_folders_need_the_game_and_GameData()
    {
        Assert.True(KspFolder.IsKspFolder(TestFolders.FakeKsp()));
        Assert.False(KspFolder.IsKspFolder(TestFolders.Temp("empty")));
        Assert.False(KspFolder.IsKspFolder(null));
        var noGameData = TestFolders.Temp("nogamedata");
        File.WriteAllText(Path.Combine(noGameData, "KSP.x86_64"), "");
        Assert.False(KspFolder.IsKspFolder(noGameData));
        Directory.CreateDirectory(Path.Combine(noGameData, "GameData"));
        Assert.True(KspFolder.IsKspFolder(noGameData)); // Linux's KSP
        Assert.False(KspFolder.IsRunning(noGameData));
    }

    [Fact]
    public void The_tower_writes_its_channel_for_the_plugin_only_when_it_talks_on_chat()
    {
        var radio = Path.Combine(TestFolders.Temp("radio"), "radio.jsonl");
        using var server = new TowerServer(0, () => T0);
        server.Answer("POST", "/v1/say", """{"callSign":"Alpha","kind":"intent","text":"local flight"}""");
        Assert.False(File.Exists(radio)); // off

        server.RadioPath = radio;
        server.Answer("POST", "/v1/say", """{"callSign":"Alpha","kind":"request-approach","text":"request visual"}""");
        var lines = File.ReadAllLines(radio).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(["Alpha", "KSC tower"], lines.Select(l => l.GetProperty("from").GetString()));
        Assert.StartsWith("Alpha, KSC tower, cleared", lines[1].GetProperty("text").GetString());

        // Full, it starts a new file instead of going silent for good (Mission Control's audit 21, D3).
        File.WriteAllText(radio, new string('x', (int)TowerServer.MaxRadioBytes));
        server.Answer("POST", "/v1/say", """{"callSign":"Alpha","kind":"intent","text":"local flight"}""");
        Assert.True(new FileInfo(radio).Length < 1000);
        Assert.Contains("local flight", File.ReadAllText(radio));
        Assert.True(File.Exists(radio + ".old"));
    }
}
