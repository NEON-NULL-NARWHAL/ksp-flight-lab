// KSP Flight Lab · tests/KspFlightLab.Tests/PageTests.cs
// [Test] Every page, driven like a user: Install (Wingman, its mod, the model), Agents (fake consoles: events shown,
//        orders, training, stop), the KSC tower (two agents, one cleared and one holding), the Knowledge pages (live from
//        the tower and from its file with the tower off), Sessions from the agents' logs, and Settings.
// Used by: dotnet test
// Guide:   Meat and Potatoes/06-App-UI.md

using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using KspFlightLab.Core;
using KspFlightLab.Core.Tests;
using KspFlightLab.Core.Tower;
using KspFlightLab.Core.Wingman;

namespace KspFlightLab.Tests;

public class PageTests
{
    private const string ZipUrl = "https://github.com/NEON-NULL-NARWHAL/ksp-wingman/releases/download/v0.6.0/KspWingman-0.6.0.zip";

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static byte[] ReleaseZip()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in new[] { "KspWingman.exe", "KspWingman.Mcp.exe", @"GameData\KspWingman\KspWingman.Bridge.dll" })
                using (var writer = new StreamWriter(zip.CreateEntry(name).Open()))
                    writer.Write("MZ " + name);
        return memory.ToArray();
    }

    private static string ReleasesJson(byte[] zip) => JsonSerializer.Serialize(new[]
    {
        new
        {
            tag_name = "v0.6.0", prerelease = false, draft = false, html_url = "https://github.com/x", published_at = "2026-10-23T12:00:00Z", body = "",
            assets = new[] { new { name = "KspWingman-0.6.0.zip", browser_download_url = ZipUrl, size = zip.Length, digest = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(zip)) } },
        },
    });

    private static void FakeInstall()
    {
        var folder = Directory.CreateDirectory(Path.Combine(WingmanTools.Root, "0.6.0", "GameData", "KspWingman")).Parent!.Parent!.FullName;
        File.WriteAllText(Path.Combine(folder, WingmanInstall.ProgramName("KspWingman")), "MZ");
        File.WriteAllText(Path.Combine(folder, WingmanInstall.ProgramName("KspWingman.Mcp")), "MZ");
    }

    private static string Report(string callSign, double along, double cross) =>
        JsonSerializer.Serialize(new
        {
            callSign,
            latitude = Ksc.At("", along, cross, 900, 0).Latitude,
            longitude = Ksc.At("", along, cross, 900, 0).Longitude,
            altitude = 900,
            heading = 90,
            speed = 120,
            situation = "Flying",
        });

    [AvaloniaFact]
    public void Every_page_opens_and_the_footer_says_what_runs()
    {
        using var ui = new UiHarness();
        Assert.Contains("selected", ui.Find<Button>("nav/" + MainWindow.SetupKey).Classes); // Wingman not installed: Install first
        foreach (var key in new[] { MainWindow.AgentsKey, MainWindow.TowerKey, MainWindow.SkillsKey, MainWindow.PlanesKey, MainWindow.MapKey,
                     MainWindow.LessonsKey, MainWindow.SessionsKey, MainWindow.SetupKey, MainWindow.SettingsKey })
        {
            ui.Open(key);
            Assert.Equal(key, ui.Window.Current);
            Assert.Contains("selected", ui.Find<Button>("nav/" + key).Classes);
        }
        Assert.Equal("Tower off", ui.Find<TextBlock>("footer/tower").Text);
        Assert.Contains("Wingman not installed · 0 agent(s) · server: no server chosen", ui.Find<TextBlock>("footer/versions").Text);
        Assert.False(File.Exists(ErrorLog.Path));
    }

    [AvaloniaFact]
    public void Install_page_installs_wingman_its_mod_and_downloads_the_model()
    {
        using var ui = new UiHarness(ksp: true, settings: s => s with { PlannerModel = "qwen3:4b" });
        var zip = ReleaseZip();
        ui.Web.Text(WingmanTools.ReleasesUrl, ReleasesJson(zip)).Bytes(ZipUrl, zip)
            .Text("http://127.0.0.1:11434/api/version", """{"version":"0.40.9"}""")
            .Text("http://127.0.0.1:11434/api/tags", """{"models":[{"name":"gemma4:e4b"}]}""")
            .Text("http://127.0.0.1:11434/api/pull", """{"status":"success"}""");

        ui.Open(MainWindow.SetupKey);
        Assert.Contains("Ollama isn't running", ui.Description("setup/ollamaStatus")); // checked at the start, before the fake was ready
        ui.Click(ui.Find<Button>("setup/ollamaCheck"));
        ui.WaitFor(() => ui.Description("setup/ollamaStatus").Contains("0.40.9"), "Ollama checked");
        Assert.Contains("qwen3:4b not downloaded", ui.Description("setup/ollamaStatus")); // the pilot is there, the planner isn't
        Assert.StartsWith("Not installed.", ui.Description("setup/wingmanStatus"));
        Assert.Contains("install KSP Wingman first", ui.Description("setup/bridgeStatus"));
        Assert.Contains("No server chosen", ui.Description("setup/server"));

        ui.Click(ui.Find<Button>("setup/wingman"));
        ui.WaitFor(() => WingmanTools.Installed() != null && ui.Find<TextBlock>("setup/result").Text!.Contains("installed"), "Wingman installed");
        Assert.StartsWith("0.6.0 installed.", ui.Description("setup/wingmanStatus"));
        Assert.Contains("Wingman's mod isn't installed · kRPC isn't", ui.Description("setup/bridgeStatus"));
        // Inside Flight Lab's own settings folder here (a --settings run is portable), never the real tools folder.
        Assert.StartsWith(ui.Folder, WingmanTools.Root);

        ui.Click(ui.Find<Button>("setup/bridge"));
        Assert.True(File.Exists(Path.Combine(ui.KspDirectory!, "GameData", "KspWingman", "KspWingman.Bridge.dll")));
        Assert.Contains("Wingman's mod is installed", ui.Description("setup/bridgeStatus"));
        Assert.Null(ui.TryFind<Button>("setup/bridge"));

        ui.Click(ui.Find<Button>("setup/pull"));
        ui.WaitFor(() => ui.Find<TextBlock>("setup/result").Text == "qwen3:4b ready.", "model pulled");
        Assert.Contains(ui.Web.Requests, r => r.Url.EndsWith("/api/pull", StringComparison.Ordinal) && r.Body.Contains("qwen3:4b"));

        ui.Click(ui.Find<Button>("setup/wingman")); // now "Check for updates"
        ui.WaitFor(() => ui.Find<TextBlock>("setup/result").Text!.Contains("is the newest"), "update check");
    }

    [AvaloniaFact]
    public void The_tower_page_shows_two_agents_one_cleared_and_one_holding()
    {
        var port = FreePort(); // not 8920: that may be in use on this PC
        using var ui = new UiHarness(settings: s => s with { TowerPort = port });
        ui.Open(MainWindow.TowerKey);
        Assert.StartsWith("Off.", ui.Description("tower/status"));

        ui.Click(ui.Find<Button>("tower/start"));
        var server = ui.Window.Tower.Server!;
        Assert.Equal(port, server.Port); // the settings' port
        Assert.Equal(200, server.Answer("POST", "/v1/report", Report("Alpha", 15_000, -1000)).Status);
        server.Answer("POST", "/v1/report", Report("Bravo", 3000, 2000));
        server.Answer("POST", "/v1/say", """{"callSign":"Alpha","kind":"request-approach","text":""}""");
        server.Answer("POST", "/v1/say", """{"callSign":"Bravo","kind":"intent","text":"land"}""");
        ui.Pump();

        Assert.Contains("number 1, on the RNAV 09 NORTH", ui.Description("tower/plane/Alpha"));
        Assert.Contains("number 2, holding", ui.Description("tower/plane/Bravo"));
        var channel = ui.Texts("tower/channel");
        Assert.Contains(channel, l => l.Contains("KSC tower → Alpha:  Alpha, KSC tower, say intentions."));
        Assert.Contains(channel, l => l.Contains("number 2. Hold at KSC-OH"));
        Assert.StartsWith($"Tower on · localhost:{server.Port}", ui.Find<TextBlock>("footer/tower").Text);
        Assert.False(ui.Find<CheckBox>("tower/onChat").IsEnabled); // no server, no radio

        var log = Path.Combine(ui.Window.LogFolder, $"tower-{DateTime.Now:yyyy-MM-dd}.jsonl");
        Assert.Contains(File.ReadAllLines(log), l => l.Contains("\"kind\":\"hold\""));

        ui.Click(ui.Find<Button>("tower/start")); // stop
        Assert.Null(ui.Window.Tower.Server);
        Assert.StartsWith("Off.", ui.Description("tower/status"));
    }

    [AvaloniaFact]
    public void Agents_show_their_flight_take_orders_and_train_and_the_knowledge_pages_follow()
    {
        using var ui = new UiHarness();
        FakeInstall();
        var fakes = new List<(System.Diagnostics.ProcessStartInfo Info, FakeAgent Agent)>();
        ui.Window.Agents.Host = new AgentHost(info => { var f = new FakeAgent(); fakes.Add((info, f)); return f; });
        ui.Window.Tower.Start(FreePort());
        var server = ui.Window.Tower.Server!;

        ui.Open(MainWindow.AgentsKey);
        Assert.Contains("0.6.0", ui.Description("agents/status"));
        Assert.Equal("gemma4:e4b", ui.Find<TextBox>("agents/model").Text); // from the settings
        Assert.True(ui.Find<TextBlock>("agents/none").IsVisible);
        ui.Find<TextBox>("agents/port").Text = "50010";
        ui.Find<TextBox>("agents/planner").Text = "qwen3:4b";
        ui.Click(ui.Find<Button>("agents/start"));
        var (info, agent) = Assert.Single(fakes);
        Assert.Equal("qwen3:4b", info.ArgumentList[info.ArgumentList.IndexOf("--planner-model") + 1]);
        Assert.Contains("--ipc", info.ArgumentList);
        Assert.Equal($"http://localhost:{server.Port}/", info.Environment["WINGMAN_TOWER_URL"]);
        Assert.Equal("Wingman2", ui.Find<TextBox>("agents/callSign").Text);

        ui.Find<TextBox>("agents/port").Text = "50010";
        ui.Click(ui.Find<Button>("agents/start"));
        Assert.Contains("already flying the KSP on kRPC port 50010", ui.Find<TextBlock>("agents/result").Text);
        Assert.Single(fakes);

        agent.Say("""{"type":"event","kind":"phase","text":"Parked → TakingOff","data":{"from":"Parked","to":"TakingOff"}}""");
        agent.Say("""{"type":"event","kind":"tower","text":"Wingman1, KSC tower, cleared RNAV 09 NORTH.","data":{"kind":"clearance"}}""");
        agent.Say("""{"type":"event","kind":"flight","text":"Flight landed: stopped 1105 m down KSC runway 09.","data":{"result":"landed"}}""");
        ui.WaitFor(() => ui.Find<TextBlock>("agents/agent/Wingman1/summary").Text!.Contains("last flight: Flight landed"), "events shown");
        Assert.Contains(ui.Texts("agents/agent/Wingman1/events"), l => l.Contains("tower  Wingman1, KSC tower, cleared RNAV 09 NORTH."));
        Assert.Contains("1 agent(s)", ui.Find<TextBlock>("footer/versions").Text);

        ui.Find<TextBox>("agents/agent/Wingman1/order").Text = "land at KSC";
        ui.Click(ui.Find<Button>("agents/agent/Wingman1/send"));
        Assert.Equal("land at KSC", JsonNode.Parse(agent.Sent.Last())!["text"]!.GetValue<string>());

        ui.Find<ComboBox>("agents/agent/Wingman1/training").SelectedIndex = 1; // Fly fast
        ui.Click(ui.Find<Button>("agents/agent/Wingman1/train"));
        Assert.Equal("fast", JsonNode.Parse(agent.Sent.Last())!["focus"]!.GetValue<string>());
        agent.Say("""{"type":"event","kind":"free","text":"Free flight for 30 minutes: training fly-fast until it's learned (3 good in a row). Any order stops it."}""");
        ui.WaitFor(() => ui.Find<TextBlock>("agents/agent/Wingman1/summary").Text!.Contains("· training: fly fast"), "training shown");
        var sent = agent.Sent.Count;
        ui.Find<ComboBox>("agents/agent/Wingman1/training").SelectedIndex = 3; // switching while training
        Assert.Equal(sent + 1, agent.Sent.Count);
        Assert.Equal("planes", JsonNode.Parse(agent.Sent.Last())!["focus"]!.GetValue<string>());

        // What it learns, through the tower's channel: the Knowledge pages show it live.
        Assert.Equal(200, server.Answer("POST", "/v1/flight", """{"callSign":"Wingman1","craft":"Aeris 3A","maxSpeed":160,"result":"landed","approach":"VISUAL 09","goArounds":0,"minutes":6,"landing":{"downRunway":900,"offCenterline":5,"headingError":1},"phases":["TakingOff","Climbing","OnRoute","OnFinal","Stopped"],"finals":[{"weaves":1,"high":12,"slope":4.5}]}""").Status);
        Assert.Equal(200, server.Answer("POST", "/v1/lesson", """{"callSign":"Wingman1","skill":"land-visual","text":"Join the short visual's downwind at 700 m.","evidence":[1]}""").Status);
        Assert.Equal(200, server.Answer("POST", "/v1/test", """{"callSign":"Wingman1","craft":"Aeris 3A","pitchRate":30,"rollRate":90,"turnRate":2.1,"minSpeed":62,"deceleration":1.2,"acceleration":3,"cruiseThrottle":0.3,"steerRate":8,"drift":-0.4,"braking":3.5,"finalMin":70,"finalMax":95,"touchdown":60,"maxSlope":4.5}""").Status);
        Assert.Equal(200, server.Answer("POST", "/v1/place", """{"callSign":"Wingman1","latitude":0.12,"longitude":-74.5,"ground":420,"biome":"Highlands"}""").Status);

        ui.Open(MainWindow.SkillsKey);
        Assert.Contains("1 flight(s) by 1 agent(s), 1 lesson(s)", ui.Description("skills/where"));
        Assert.Contains("Join the short visual's downwind at 700 m.", ui.Description("skills/land-visual"));
        Assert.StartsWith("🔒", ui.Texts("skills/land-rnav")[0]);

        ui.Open(MainWindow.PlanesKey);
        Assert.Contains("1 by Wingman1 · 1 landed, 0 crashed", ui.Description("planes/Aeris 3A/flights"));
        Assert.Contains("pitch 30 °/s and roll 90 °/s", ui.Description("planes/Aeris 3A/test"));
        Assert.Contains("brakes 3.5 m/s²", ui.Description("planes/Aeris 3A/ground"));
        Assert.Contains("final speeds 70-95 m/s · touchdown 60 m/s · glide slope 3° up to 4.5°", ui.Description("planes/Aeris 3A/limits"));
        Assert.Contains("1 crossing(s), +12 m 1 km out at 4.5°", ui.Description("planes/Aeris 3A/finals"));

        ui.Open(MainWindow.MapKey);
        Assert.Contains("Highlands (1)", ui.Description("map/summary"));
        Assert.Contains("found by Wingman1", ui.Description("map/place/1"));
        Assert.NotEmpty(ExploredMap.FromKsc(server.Knowledge.Places[0]));

        ui.Open(MainWindow.LessonsKey);
        Assert.Contains("land-visual · from Wingman1 · 1 flight(s)", ui.Description("lessons/1"));
        ui.Click(ui.Find<Button>("lessons/1/delete"));
        Assert.Null(ui.TryFind<Control>("lessons/1"));

        // Stopped on approach: it leaves the tower's sequence at once.
        server.Answer("POST", "/v1/report", """{"callSign":"Wingman1","latitude":-0.0486,"longitude":-74.70,"altitude":900,"heading":90,"speed":120,"situation":"Flying"}""");
        server.Answer("POST", "/v1/say", """{"callSign":"Wingman1","kind":"request-approach","text":""}""");
        Assert.Equal(PlaneState.Approach, server.Tower.Planes.Single(p => p.CallSign == "Wingman1").State);
        ui.Open(MainWindow.AgentsKey);
        ui.Click(ui.Find<Button>("agents/agent/Wingman1/stop"));
        ui.WaitFor(() => ui.Find<TextBlock>("agents/agent/Wingman1/summary").Text!.StartsWith("stopped"), "agent stopped");
        Assert.Equal(PlaneState.Gone, server.Tower.Planes.Single(p => p.CallSign == "Wingman1").State);
        ui.Click(ui.Find<Button>("agents/agent/Wingman1/stop")); // now "Remove"
        Assert.Null(ui.TryFind<Control>("agents/agent/Wingman1"));

        // With the tower off, the Knowledge pages read its file (Mission Control's needed the tower on).
        ui.Window.Tower.Stop();
        ui.Open(MainWindow.PlanesKey);
        Assert.Contains("1 by Wingman1", ui.Description("planes/Aeris 3A/flights"));
        ui.Open(MainWindow.LessonsKey);
        Assert.Null(ui.TryFind<Control>("lessons/1")); // still deleted
    }

    [AvaloniaFact]
    public void Lessons_can_be_deleted_with_the_tower_off()
    {
        using var ui = new UiHarness(server: true);
        Assert.Equal(Path.Combine(ui.ServerDirectory!, "LunaEvo", "agents", "knowledge.json"), ui.Window.KnowledgePath); // Mission Control's file
        using (var tower = new TowerServer(0) { KnowledgePath = ui.Window.KnowledgePath })
        {
            Assert.Equal(200, tower.Answer("POST", "/v1/flight", """{"callSign":"Alpha","craft":"Gull","result":"landed","approach":"VISUAL 09","minutes":5,"landing":{"downRunway":900,"offCenterline":5,"headingError":1},"phases":["TakingOff","OnFinal","Stopped"]}""").Status);
            Assert.Equal(200, tower.Answer("POST", "/v1/lesson", """{"callSign":"Alpha","skill":"land-visual","text":"Flare at 10 m.","evidence":[1]}""").Status);
        }
        ui.Open(MainWindow.LessonsKey);
        Assert.Contains("land-visual · from Alpha", ui.Description("lessons/1"));
        ui.Click(ui.Find<Button>("lessons/1/delete"));
        Assert.Null(ui.TryFind<Control>("lessons/1"));
        Assert.Empty(Knowledge.Load(ui.Window.KnowledgePath).Lessons);
    }

    [AvaloniaFact]
    public void Sessions_tell_each_agents_day()
    {
        using var ui = new UiHarness();
        Directory.CreateDirectory(ui.Window.LogFolder);
        File.WriteAllLines(Path.Combine(ui.Window.LogFolder, "wingman-Alpha-2026-10-23.jsonl"),
        [
            """{"time":"2026-10-23T10:00:00Z","kind":"status","text":"Climbing"}""",
            """{"time":"2026-10-23T10:05:00Z","kind":"flight","text":"Flight landed: 1 m off the centerline."}""",
            """{"time":"2026-10-23T10:06:00Z","kind":"free","text":"Explored: Highlands, 420 m"}""",
        ]);
        File.WriteAllLines(Path.Combine(ui.Window.LogFolder, "wingman-Bravo-2026-10-24.jsonl"),
            ["""{"time":"2026-10-24T09:00:00Z","kind":"flight","text":"Flight crashed on final."}"""]);

        ui.Open(MainWindow.SessionsKey);
        Assert.Contains("1 flight(s): 0 landed, 1 crashed", ui.Description("sessions/summary")); // newest first: Bravo
        ui.Find<ComboBox>("sessions/pick").SelectedIndex = 1;
        ui.Pump();
        Assert.Contains("1 flight(s): 1 landed, 0 crashed · 0 test(s) · 0 debrief(s) · 1 place(s) explored", ui.Description("sessions/summary"));
        var story = ui.Window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        Assert.Contains(story, l => l.Contains("flight  Flight landed: 1 m off the centerline."));
        Assert.DoesNotContain(story, l => l.Contains("Climbing"));
    }

    [AvaloniaFact]
    public void Settings_change_the_folders_models_tower_and_look()
    {
        using var ui = new UiHarness();
        var server = TestFolders.Temp("server2");
        var ksp = TestFolders.FakeKsp();
        try
        {
            ui.Window.Tower.Start(FreePort());
            ui.Open(MainWindow.SettingsKey);
            Assert.Contains(Path.Combine(Path.GetDirectoryName(ui.SettingsPath)!, "agents", "knowledge.json"), ui.Description("settings/knowledgePath"));

            ui.Type("settings/server", server);
            Assert.Equal(server, ui.Window.Settings.ServerFolder);
            Assert.Contains("no LMP Server in that folder", ui.Find<TextBlock>("settings/result").Text);
            Assert.Equal(Path.Combine(server, "LunaEvo", "agents", "knowledge.json"), ui.Window.Tower.Server!.KnowledgePath); // the tower moved with it
            Assert.Equal(server, LabSettingsStore.Load(ui.SettingsPath).ServerFolder); // saved

            ui.Type("settings/ksp", ui.Folder);
            Assert.Contains("isn't a KSP folder", ui.Find<TextBlock>("settings/result").Text);
            Assert.Null(ui.Window.Settings.KspFolder);
            ui.Type("settings/ksp", ksp);
            Assert.Equal(ksp, ui.Window.Settings.KspFolder);

            ui.Type("settings/pilot", "qwen3:4b");
            Assert.Equal("qwen3:4b", ui.Window.Settings.PilotModel);
            ui.Type("settings/llm", "not a url");
            Assert.Equal("http://127.0.0.1:11434/v1/", ui.Window.Settings.LlmUrl);
            Assert.Equal("http://127.0.0.1:11434/v1/", ui.Find<TextBox>("settings/llm").Text);

            ui.Find<NumericUpDown>("settings/towerPort").Value = 9001;
            Assert.Equal(9001, ui.Window.Settings.TowerPort);
            ui.Find<ToggleSwitch>("settings/towerOnStart").IsChecked = true;
            Assert.True(LabSettingsStore.Load(ui.SettingsPath).TowerOnStart);
            ui.Find<ToggleSwitch>("settings/towerChat").IsChecked = true;
            Assert.Equal(Path.Combine(server, "LunaEvo", "radio.jsonl"), ui.Window.Tower.Server!.RadioPath);

            ui.Click(ui.Find<Button>("settings/preset/Kerbin blue"));
            Assert.Equal("#2F7FD8", ui.Window.Settings.AccentColor);
            ui.Type("settings/accentHex", "c8452e");
            Assert.Equal("#C8452E", ui.Window.Settings.AccentColor);
            ui.Find<ComboBox>("settings/theme").SelectedItem = "Light";
            Assert.Equal("Light", ui.Window.Settings.Theme);
            ui.Click(ui.Find<Button>("settings/resetAppearance"));
            Assert.Equal((LabSettings.DefaultAccent, "Dark"), (ui.Window.Settings.AccentColor, ui.Window.Settings.Theme));

            ui.Type("settings/server", "");
            Assert.Null(ui.Window.Settings.ServerFolder);
        }
        finally
        {
            ui.Window.Tower.Stop();
            TestFolders.Delete(server);
            TestFolders.Delete(ksp);
        }
    }
}
