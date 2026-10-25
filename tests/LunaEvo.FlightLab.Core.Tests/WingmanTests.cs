// Flight Lab · tests/LunaEvo.FlightLab.Core.Tests/WingmanTests.cs
// [Test] KSP Wingman as a tool: the verified download and the careful unzip, its bridge into a KSP, kRPC found, Ollama's
//        answers, and agents (the console's --ipc lines read, orders sent, one agent per KSP).
// Used by: dotnet test
// Guide:   Meat and Potatoes/04-Agents.md

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using LunaEvo.FlightLab.Core.Install;
using LunaEvo.FlightLab.Core.Wingman;

namespace LunaEvo.FlightLab.Core.Tests;

public class WingmanTests
{
    private const string ZipUrl = "https://github.com/NEON-NULL-NARWHAL/ksp-wingman/releases/download/v0.6.0/KspWingman-0.6.0.zip";

    private static string Temp(string name) =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "FlightLabTests", name + "-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>A release zip like publish.ps1's (PowerShell 5 writes backslashes), plus whatever else a test adds.</summary>
    private static byte[] ReleaseZip(params (string Name, string Text)[] extra)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in new[] { ("KspWingman.exe", "MZ console"), ("KspWingman.Mcp.exe", "MZ mcp"), (@"GameData\KspWingman\KspWingman.Bridge.dll", "MZ bridge"), ("README.md", "#") }.Concat(extra))
                using (var writer = new StreamWriter(zip.CreateEntry(name).Open()))
                    writer.Write(text);
        }
        return memory.ToArray();
    }

    private static string ReleasesJson(byte[] zip, bool withDigest = true) => System.Text.Json.JsonSerializer.Serialize(new[]
    {
        new
        {
            tag_name = "v0.6.0", prerelease = false, draft = false, html_url = "https://github.com/x", published_at = "2026-10-23T12:00:00Z", body = "",
            assets = new[] { new { name = "KspWingman-0.6.0.zip", browser_download_url = ZipUrl, size = zip.Length, digest = withDigest ? "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(zip)) : null } },
        },
    });

    [Fact]
    public async Task Installs_a_verified_release_and_its_bridge_into_a_ksp()
    {
        WingmanTools.Root = Temp("wingman");
        var zip = ReleaseZip(("Extra.dll", "not ours"));
        var http = new FakeHttp().Text(WingmanTools.ReleasesUrl, ReleasesJson(zip)).Bytes(ZipUrl, zip);
        Assert.Null(WingmanTools.Installed());

        var (release, asset) = (await WingmanTools.LatestAsync(http.Client))!.Value;
        var install = await WingmanTools.DownloadAndInstallAsync(http.Client, release, asset);
        Assert.Equal(install, WingmanTools.Installed());
        Assert.Equal(Path.Combine(WingmanTools.Root, "0.6.0"), install.Folder);
        Assert.False(File.Exists(Path.Combine(install.Folder, "Extra.dll"))); // only what a release has
        Assert.True(File.Exists(Path.Combine(install.Bridge, "KspWingman.Bridge.dll")));

        var ksp = TestFolders.FakeKsp();
        Assert.False(WingmanTools.HasKrpc(ksp));
        Directory.CreateDirectory(Path.Combine(ksp, "GameData", "kRPC"));
        File.WriteAllText(Path.Combine(ksp, "GameData", "kRPC", "KRPC.dll"), "");
        Assert.True(WingmanTools.HasKrpc(ksp));
        Assert.Equal(BridgeState.NotInstalled, WingmanTools.Bridge(ksp, install));
        WingmanTools.InstallBridge(ksp, install);
        Assert.Equal(BridgeState.Installed, WingmanTools.Bridge(ksp, install));
        File.WriteAllText(Path.Combine(ksp, "GameData", "KspWingman", "KspWingman.Bridge.dll"), "MZ older");
        Assert.Equal(BridgeState.Different, WingmanTools.Bridge(ksp, install));
        Assert.Equal(BridgeState.NoKsp, WingmanTools.Bridge(Temp("notksp"), install));
    }

    [Fact]
    public async Task Refuses_unverified_tampered_and_escaping_zips()
    {
        WingmanTools.Root = Temp("wingman");
        var zip = ReleaseZip();
        // No checksum from GitHub: not offered at all.
        Assert.Null(await WingmanTools.LatestAsync(new FakeHttp().Text(WingmanTools.ReleasesUrl, ReleasesJson(zip, withDigest: false)).Client));

        var path = Path.Combine(Temp("zips"), "w.zip");
        File.WriteAllBytes(path, zip);
        Assert.Throws<DownloadVerificationException>(() => WingmanTools.Unpack(path, "0.6.0", new string('0', 64)));

        File.WriteAllBytes(path, ReleaseZip((@"GameData/KspWingman/../../../escape.txt", "x")));
        Assert.Throws<InvalidDataException>(() => WingmanTools.Unpack(path, "0.6.0", VerifiedDownload.Sha256Of(path)));
        Assert.False(File.Exists(Path.Combine(WingmanTools.Root, "..", "escape.txt")));

        using (var only = new MemoryStream())
        {
            using (var z = new ZipArchive(only, ZipArchiveMode.Create, leaveOpen: true))
                z.CreateEntry("KspWingman.exe");
            File.WriteAllBytes(path, only.ToArray());
        }
        Assert.Throws<InvalidDataException>(() => WingmanTools.Unpack(path, "0.6.0", VerifiedDownload.Sha256Of(path))); // not a release
        Assert.Throws<InvalidDataException>(() => WingmanTools.Unpack(path, "../x", VerifiedDownload.Sha256Of(path)));
        Assert.Null(WingmanTools.Installed());
        Assert.Empty(Directory.GetDirectories(WingmanTools.Root)); // no staging folders left behind
    }

    [Fact]
    public async Task Finds_ollama_and_its_models()
    {
        var http = new FakeHttp()
            .Text("http://127.0.0.1:11434/api/version", """{"version":"0.40.9"}""")
            .Text("http://127.0.0.1:11434/api/tags", """{"models":[{"name":"qwen3:4b-instruct"},{"name":7},"junk",{"name":"gemma4:e4b"}]}""")
            .Text("http://127.0.0.1:11434/api/pull", "{\"status\":\"pulling\",\"total\":200,\"completed\":100}\n{\"status\":\"success\"}\n");
        var (version, models) = (await WingmanTools.OllamaAsync(http.Client))!.Value;
        Assert.Equal("0.40.9", version);
        Assert.Equal(["qwen3:4b-instruct", "gemma4:e4b"], models);
        var progress = new List<double>();
        Assert.Null(await WingmanTools.PullAsync(http.Client, "qwen3:4b-instruct", new Progress<double>(progress.Add)));
        Assert.Contains("missing", await WingmanTools.PullAsync(new FakeHttp().Text("http://127.0.0.1:11434/api/pull", """{"error":"model missing"}""").Client, "nope"));
        Assert.Contains("\"model\":\"qwen3:4b-instruct\"", http.Requests.Last().Body);

        Assert.Null(await WingmanTools.OllamaAsync(new FakeHttp().Client)); // not running
        Assert.Null(await WingmanTools.OllamaAsync(new FakeHttp().Text("http://127.0.0.1:11434/api/version", "{not json").Client));
    }

    private sealed class FakeAgent : IAgentProcess
    {
        public List<string> Sent { get; } = [];
        public bool HasExited { get; private set; }
        public event Action<string?>? Output;
        public event Action? Exited;
        public void Say(string line) => Output?.Invoke(line);

        public void Send(string line)
        {
            Sent.Add(line);
            if (line.Contains("\"quit\""))
                Kill();
        }

        public void Kill()
        {
            HasExited = true;
            Exited?.Invoke();
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task Agents_report_phases_outcomes_and_take_orders()
    {
        var install = new WingmanInstall("0.6.0", @"D:\Tools\KspWingman\0.6.0");
        var info = AgentHost.StartInfo(install, new AgentStart("Wingman1", 50010, "qwen3:4b-instruct", "http://127.0.0.1:11434/v1/", "http://localhost:8920/"));
        Assert.Equal(["--ipc", "--model", "qwen3:4b-instruct", "--llm", "http://127.0.0.1:11434/v1/", "--krpc-port", "50010", "--call-sign", "Wingman1", "--mcp", install.McpServer],
            info.ArgumentList);
        Assert.Equal("http://localhost:8920/", info.Environment["WINGMAN_TOWER_URL"]);

        var fakes = new List<FakeAgent>();
        using var host = new AgentHost(_ => { var f = new FakeAgent(); fakes.Add(f); return f; });
        var start = new AgentStart("Wingman1", 50010, "qwen3:4b-instruct", "http://127.0.0.1:11434/v1/");
        var agent = host.Start(install, start);
        Assert.NotNull(host.Check(start)); // one agent per KSP
        Assert.NotNull(host.Check(start with { KrpcPort = 50020 })); // and per call sign
        Assert.Null(host.Check(start with { KrpcPort = 50020, CallSign = "Wingman2" }));
        Assert.NotNull(host.Check(start with { KrpcPort = 50020, CallSign = "bad name; rm" }));
        Assert.NotNull(host.Check(start with { KrpcPort = 50020, CallSign = "W2", LlmUrl = "file:///C:/x" }));
        Assert.NotNull(host.Check(start with { KrpcPort = 50020, CallSign = "W2", Model = "--mcp" })); // audit 14, W1
        Assert.NotNull(host.Check(start with { KrpcPort = 50020, CallSign = "W2", Model = "qwen3 --mcp x" }));

        var fake = fakes.Single();
        fake.Say("""{"type":"event","time":"2026-10-23T15:00:00Z","kind":"phase","text":"Parked → TakingOff","data":{"from":"Parked","to":"TakingOff"}}""");
        fake.Say("""{"type":"event","kind":"status","text":"Flying at 900 m"}""");
        fake.Say("""{"type":"event","kind":"flight","text":"Flight landed: stopped 1105 m down KSC runway 09.","data":{"result":"landed"}}""");
        fake.Say("Unhandled exception. System.IO.IOException: boom");
        fake.Say("");
        Assert.Equal("TakingOff", agent.Phase);
        Assert.Equal("Flying at 900 m", agent.Status);
        Assert.StartsWith("Flight landed", agent.LastOutcome);
        Assert.Equal(["phase", "flight", "error"], agent.Events.Select(e => e.Kind)); // status isn't an event
        Assert.Equal(new DateTime(2026, 10, 23, 15, 0, 0, DateTimeKind.Utc), agent.Events[0].Time);

        agent.Order("  land at KSC  ");
        agent.Order("   ");
        agent.AskStatus();
        agent.AskStatus(); // the first is still waiting
        Assert.Equal("land at KSC", JsonNode.Parse(fake.Sent[0])!["text"]!.GetValue<string>());
        Assert.Equal("status", JsonNode.Parse(fake.Sent[1])!["type"]!.GetValue<string>());
        Assert.Equal(2, fake.Sent.Count);

        await agent.StopAsync(TimeSpan.FromSeconds(1));
        Assert.False(agent.Running);
        Assert.Null(host.Check(start)); // its KSP is free again
        host.Start(install, start);
        Assert.Single(host.Agents); // the stopped one's card made way
    }

    [Fact]
    public void Each_agents_session_is_recorded_one_json_line_each()
    {
        var folder = Temp("sessions");
        var fake = new FakeAgent();
        using var host = new AgentHost(_ => fake, logFolder: folder);
        var agent = host.Start(new WingmanInstall("0.7.0", folder), new AgentStart("Wingman1", 50000, "qwen3:4b-instruct", "http://127.0.0.1:11434/v1/"));
        fake.Say("""{"type":"event","kind":"free","text":"Next: land on the short visual."}""");
        fake.Say("""{"type":"event","kind":"lesson","text":"Learned (land-visual): line up before F09V.","data":{"skill":"land-visual"}}""");
        var lines = File.ReadAllLines(agent.LogPath!);
        Assert.Equal(2, lines.Length);
        var first = JsonNode.Parse(lines[1])!;
        Assert.Equal("Wingman1", first["callSign"]!.GetValue<string>());
        Assert.Equal("land-visual", first["data"]!["skill"]!.GetValue<string>());
        Assert.StartsWith(Path.Combine(folder, "wingman-Wingman1-"), agent.LogPath);
    }

    [Fact]
    public void Agent_lines_are_read_safely()
    {
        var now = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        Assert.Null(AgentEvent.Parse(null, now));
        Assert.Null(AgentEvent.Parse("   ", now));
        Assert.Equal("error", AgentEvent.Parse("""{"type":"event","kind":7,"text":"x"}""", now)!.Kind);
        Assert.Equal(now, AgentEvent.Parse("""{"type":"event","kind":"phase","text":"x","time":5}""", now)!.Time);
        Assert.Equal(AgentEvent.MaxText + 1, AgentEvent.Parse(new string('x', 10_000), now)!.Text.Length);
        Assert.Equal("error", AgentEvent.Parse(new string('{', AgentEvent.MaxLine + 1), now)!.Kind);
        Assert.Equal("error", AgentEvent.Parse("[" + new string('[', 2000), now)!.Kind); // too deep for the reader
    }
}
