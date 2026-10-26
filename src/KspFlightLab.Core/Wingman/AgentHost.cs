// KSP Flight Lab · src/KspFlightLab.Core/Wingman/AgentHost.cs
// [Core] AI pilots run from Flight Lab: KSP Wingman's console started with --ipc (JSON lines in and out), one per KSP
//        (kRPC port), and what each one says: its phase, its flights' outcomes, the tower, its tool calls and answers.
// Used by: AgentsPage
// Guide:   Meat and Potatoes/04-Agents.md

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KspFlightLab.Core.Wingman;

/// <summary>One line from the console: "order", "answer", "call", "result", "status", "phase", "flight", "tower", "error", "wingman".</summary>
public sealed record AgentEvent(DateTime Time, string Kind, string Text, JsonNode? Data = null)
{
    public const int MaxLine = 64 * 1024;
    public const int MaxText = 4000;

    /// <summary>
    /// Reads one --ipc output line ({"type":"event","time","kind","text","data"}). Anything else the console prints
    /// (a crash's stack trace, .NET's own messages) comes back as an "error" line, cut short; null for a blank line.
    /// </summary>
    public static AgentEvent? Parse(string? line, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        if (line.Length > MaxLine)
            return new AgentEvent(now, "error", "(an unreadably long line from the agent)");
        try
        {
            if (JsonNode.Parse(line) is JsonObject o && Str(o, "type") == "event" && Str(o, "kind") is { Length: > 0 and <= 20 } kind)
            {
                var time = o["time"] is JsonValue t && t.GetValueKind() == JsonValueKind.String && t.TryGetValue<DateTime>(out var parsed)
                    ? parsed.ToUniversalTime() : now;
                return new AgentEvent(time, kind, Cut(Str(o, "text") ?? ""), o["data"]?.DeepClone());
            }
        }
        catch (JsonException)
        {
        }
        catch (FormatException)
        {
        }
        catch (ArgumentException)
        {
            // A lone surrogate in the text: not JSON we can read.
        }
        return new AgentEvent(now, "error", Cut(line.Trim()));
    }

    private static string? Str(JsonObject o, string name) =>
        o[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    private static string Cut(string text) => text.Length <= MaxText ? text : text[..MaxText] + "…";
}

/// <summary>A started console process; tests use a fake.</summary>
public interface IAgentProcess : IDisposable
{
    event Action<string?>? Output;
    event Action? Exited;
    bool HasExited { get; }
    void Send(string line);
    void Kill();
}

/// <summary>What to start: which Wingman, which model where, which KSP (its kRPC port), and the tower if it's on.</summary>
/// <param name="PlannerModel">A model that thinks (free-flight goals, debriefs), on the same server; null: the pilot's.</param>
public sealed record AgentStart(string CallSign, int KrpcPort, string Model, string LlmUrl, string? TowerUrl = null, string? PlannerModel = null);

/// <summary>One AI pilot: its console process and everything it said, newest last.</summary>
public sealed class WingmanAgent : IDisposable
{
    public const int MaxEvents = 500;
    public const int MaxOrder = 1000;

    private readonly IAgentProcess _process;
    private readonly List<AgentEvent> _events = [];
    private readonly Lock _gate = new();

    private readonly Func<DateTime> _now;
    private DateTime? _statusAsked;

    /// <summary>The session's log stops growing here (a runaway agent can't fill the disk).</summary>
    public const long MaxLogBytes = 20L * 1024 * 1024;

    private static readonly JsonSerializerOptions LogJson = new(JsonSerializerDefaults.Web);

    internal WingmanAgent(AgentStart start, IAgentProcess process, Func<DateTime> now, string? logPath = null)
    {
        Start = start;
        _now = now;
        LogPath = logPath;
        _process = process;
        process.Output += line =>
        {
            if (AgentEvent.Parse(line, now()) is not { } e)
                return;
            Record(e);
            lock (_gate)
            {
                if (e.Kind == "phase" && e.Data is JsonObject data && data["to"] is JsonValue to && to.GetValueKind() == JsonValueKind.String)
                    Phase = to.GetValue<string>();
                if (e.Kind == "status")
                {
                    Status = e.Text;
                    _statusAsked = null;
                }
                else
                {
                    if (e.Kind == "flight")
                        LastOutcome = e.Text;
                    if (e.Kind == "free")
                    {
                        // "Free flight for 30 minutes..." starts it; "Free flight over/stopped..." ends it; the rest is on the way.
                        FreeFlying = e.Text.StartsWith("Free flight for", StringComparison.Ordinal) || (FreeFlying && !e.Text.StartsWith("Free flight", StringComparison.Ordinal));
                        // "...: training fly-fast until it's learned": which training (null: plain free flight, or none).
                        if (e.Text.StartsWith("Free flight for", StringComparison.Ordinal))
                            Training = TrainingOf(e.Text);
                        else if (!FreeFlying)
                            Training = null;
                    }
                    _events.Add(e);
                    if (_events.Count > MaxEvents)
                        _events.RemoveRange(0, _events.Count - MaxEvents);
                }
            }
            Changed?.Invoke();
        };
        process.Exited += () =>
        {
            Exited?.Invoke();
            Changed?.Invoke();
        };
    }

    /// <summary>Its process ended (stopped, or it crashed): the tower takes it out of the sequence (AgentHost.AgentExited).</summary>
    public event Action? Exited;

    public AgentStart Start { get; }

    /// <summary>The session recorder: every line this agent said, one JSON object each, or null (tests).</summary>
    public string? LogPath { get; }

    private void Record(AgentEvent e)
    {
        if (LogPath == null)
            return;
        try
        {
            lock (LogJson)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaxLogBytes)
                    File.AppendAllText(LogPath, JsonSerializer.Serialize(new { e.Time, callSign = Start.CallSign, e.Kind, e.Text, e.Data }, LogJson) + "\n");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A full disk loses the log, not the agent.
        }
    }
    public string Phase { get; private set; } = "Starting";
    public string? Status { get; private set; }
    public string? LastOutcome { get; private set; }

    /// <summary>In free flight (from the console's own "free" lines).</summary>
    public bool FreeFlying { get; private set; }

    /// <summary>The training running (a <see cref="Trainings"/> focus), or null.</summary>
    public string? Training { get; private set; }

    /// <summary>Which training a console's "Free flight for N minutes: training ... until" line is (null: none).</summary>
    public static string? TrainingOf(string text)
    {
        var i = text.IndexOf(": training ", StringComparison.Ordinal);
        var end = text.IndexOf(" until", StringComparison.Ordinal);
        if (i < 0 || end <= i)
            return null;
        var skills = text[(i + 11)..end];
        return skills switch
        {
            _ when skills.Contains("fly-fast", StringComparison.Ordinal) => "fast",
            _ when skills.Contains("fly-high", StringComparison.Ordinal) => "high",
            _ when skills.Contains("new-craft", StringComparison.Ordinal) => "planes",
            _ when skills.Contains("explore", StringComparison.Ordinal) => "explore",
            _ when skills.Contains("approach", StringComparison.Ordinal) => "approaches",
            _ when skills.Contains("land-", StringComparison.Ordinal) => "landings",
            _ when skills.Contains("route", StringComparison.Ordinal) => "plans",
            _ => null,
        };
    }
    public bool Running => !_process.HasExited;

    /// <summary>Something new to show (an event, or the process ended). Raised on the process's thread.</summary>
    public event Action? Changed;

    public IReadOnlyList<AgentEvent> Events
    {
        get
        {
            lock (_gate)
                return _events.ToList();
        }
    }

    /// <summary>An order in plain words, as the console's own prompt would take it.</summary>
    public void Order(string text, string from = "Commander")
    {
        text = text.Trim();
        if (text.Length == 0 || !Running)
            return;
        Send(new JsonObject { ["type"] = "order", ["text"] = text.Length > MaxOrder ? text[..MaxOrder] : text, ["from"] = from });
    }

    /// <summary>
    /// Asks for the plane's status (comes back as Status, not in the events). The console answers between orders, so
    /// while one is still waiting (the model busy) another isn't queued behind it, for a minute.
    /// </summary>
    public void AskStatus()
    {
        lock (_gate)
        {
            if (_statusAsked is { } asked && _now() - asked < TimeSpan.FromMinutes(1))
                return;
            _statusAsked = _now();
        }
        Send(new JsonObject { ["type"] = "status" });
    }

    /// <summary>Asks the console to quit (the plane keeps its last orders); kills it if it hasn't gone after a while.</summary>
    public async Task StopAsync(TimeSpan? wait = null)
    {
        Quit();
        var until = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(10));
        while (Running && DateTime.UtcNow < until)
            await Task.Delay(100).ConfigureAwait(false);
        if (Running)
            _process.Kill();
    }

    /// <summary>Free flight: the agent practises landings around KSC on its own for some minutes (any order stops it).</summary>
    public void FreeFlight(double minutes) => Send(new JsonObject { ["type"] = "free", ["minutes"] = Math.Clamp(minutes, 5, 240) });

    public void StopFreeFlight() => Send(new JsonObject { ["type"] = "stop-free" });

    /// <summary>What training can focus on (the console's words), with a label for people.</summary>
    public static readonly IReadOnlyList<(string Focus, string Label)> Trainings =
        [("landings", "Work on landings"), ("fast", "Fly fast"), ("high", "Fly high"), ("planes", "Try different planes"), ("plans", "Flight plans"),
         ("approaches", "Riskier approaches"), ("explore", "Explore")];

    /// <summary>Training (the owner's idea): free flight on one ability until it's learned (3 good in a row) or the time's up.</summary>
    public void Train(string focus, double minutes = 30)
    {
        if (Trainings.Any(t => t.Focus == focus))
            Send(new JsonObject { ["type"] = "train", ["focus"] = focus, ["minutes"] = Math.Clamp(minutes, 5, 240) });
    }

    /// <summary>Asks the console to quit, without waiting.</summary>
    public void Quit() => Send(new JsonObject { ["type"] = "quit" });

    private void Send(JsonObject message)
    {
        if (!Running)
            return;
        try
        {
            _process.Send(message.ToJsonString());
        }
        catch (IOException)
        {
            // It just exited: Exited will say so.
        }
    }

    public void Dispose()
    {
        if (Running)
            _process.Kill();
        _process.Dispose();
    }
}

/// <summary>Starts and keeps the agents. One per kRPC port (two consoles flying one KSP would fight).</summary>
public sealed class AgentHost : IDisposable
{
    public const int MaxAgents = 8;

    private readonly List<WingmanAgent> _agents = [];
    private readonly Func<ProcessStartInfo, IAgentProcess> _launch;
    private readonly Func<DateTime> _now;
    private readonly string? _logFolder;

    /// <param name="logFolder">Where sessions are recorded (wingman-&lt;call sign&gt;-&lt;date&gt;.jsonl), or null.</param>
    public AgentHost(Func<ProcessStartInfo, IAgentProcess>? launch = null, Func<DateTime>? now = null, string? logFolder = null)
    {
        _logFolder = logFolder;
        _launch = launch ?? (info => new AgentProcess(info));
        _now = now ?? (() => DateTime.UtcNow);
    }

    public IReadOnlyList<WingmanAgent> Agents => _agents;

    public event Action? Changed;

    /// <summary>An agent's process ended (from its process's thread).</summary>
    public event Action<WingmanAgent>? AgentExited;

    /// <summary>The console's command line for an agent: --ipc, the model, the KSP; the tower through its variable.</summary>
    public static ProcessStartInfo StartInfo(WingmanInstall install, AgentStart start)
    {
        var info = new ProcessStartInfo(install.Console)
        {
            WorkingDirectory = install.Folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "--ipc", "--model", start.Model, "--llm", start.LlmUrl, "--krpc-port", start.KrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "--call-sign", start.CallSign, "--mcp", install.McpServer })
            info.ArgumentList.Add(arg);
        if (start.PlannerModel is { Length: > 0 } planner)
        {
            info.ArgumentList.Add("--planner-model");
            info.ArgumentList.Add(planner);
        }
        if (start.TowerUrl != null)
            info.Environment["WINGMAN_TOWER_URL"] = start.TowerUrl;
        return info;
    }

    /// <summary>What's wrong with starting this agent, or null.</summary>
    public string? Check(AgentStart start)
    {
        if (start.KrpcPort is < 1 or > 65535)
            return "The kRPC port must be between 1 and 65535.";
        if (string.IsNullOrWhiteSpace(start.Model))
            return "Pick a model.";
        // Audit 14, W1: a value starting with "-" would be read by the console as one of its options.
        if (!ModelName(start.Model) || (start.PlannerModel is { Length: > 0 } p && !ModelName(p)))
            return "A model name is up to 100 characters, without spaces, and doesn't start with \"-\" (e.g. qwen3:4b-instruct).";
        if (!Uri.TryCreate(start.LlmUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            return "The model server's address must be an http:// or https:// address.";
        if (start.CallSign.Length is 0 or > 20 || !start.CallSign.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return "A call sign is 1 to 20 letters, digits, - or _.";
        if (_agents.Any(a => a.Running && a.Start.KrpcPort == start.KrpcPort))
            return $"An agent is already flying the KSP on kRPC port {start.KrpcPort}.";
        if (_agents.Any(a => a.Running && a.Start.CallSign.Equals(start.CallSign, StringComparison.OrdinalIgnoreCase)))
            return $"{start.CallSign} is already flying.";
        if (_agents.Count(a => a.Running) >= MaxAgents)
            return $"At most {MaxAgents} agents at once.";
        return null;
    }

    private static bool ModelName(string name) =>
        name.Length <= 100 && !name.StartsWith('-') && !name.Any(c => char.IsControl(c) || char.IsWhiteSpace(c));

    public WingmanAgent Start(WingmanInstall install, AgentStart start)
    {
        if (Check(start) is { } problem)
            throw new InvalidOperationException(problem);
        _agents.RemoveAll(a => !a.Running && a.Start.KrpcPort == start.KrpcPort); // its old card goes
        var log = _logFolder == null ? null : Path.Combine(_logFolder, $"wingman-{start.CallSign}-{DateTime.Now:yyyy-MM-dd}.jsonl");
        var agent = new WingmanAgent(start, _launch(StartInfo(install, start)), _now, log);
        agent.Changed += () => Changed?.Invoke();
        agent.Exited += () => AgentExited?.Invoke(agent);
        _agents.Add(agent);
        Changed?.Invoke();
        return agent;
    }

    public void Remove(WingmanAgent agent)
    {
        if (agent.Running)
            return;
        _agents.Remove(agent);
        agent.Dispose();
        Changed?.Invoke();
    }

    /// <summary>Flight Lab closing: every agent asked to quit, a few seconds to do it, then the rest killed (on Dispose).</summary>
    public void QuitAll(TimeSpan wait)
    {
        foreach (var agent in _agents.Where(a => a.Running))
            agent.Quit();
        var until = DateTime.UtcNow + wait;
        while (_agents.Any(a => a.Running) && DateTime.UtcNow < until)
            Thread.Sleep(100);
    }

    public void Dispose()
    {
        foreach (var agent in _agents)
            agent.Dispose();
        _agents.Clear();
    }

    private sealed class AgentProcess : IAgentProcess
    {
        private readonly Process _process;

        public AgentProcess(ProcessStartInfo info)
        {
            _process = new Process { StartInfo = info, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => Output?.Invoke(e.Data);
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Output?.Invoke(e.Data); // not JSON: shown as an error line
            };
            _process.Exited += (_, _) => Exited?.Invoke();
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public event Action<string?>? Output;
        public event Action? Exited;
        public bool HasExited => _process.HasExited;

        public void Send(string line)
        {
            _process.StandardInput.WriteLine(line);
            _process.StandardInput.Flush();
        }

        public void Kill()
        {
            try
            {
                _process.Kill(entireProcessTree: true); // the console's MCP server with it
            }
            catch (InvalidOperationException)
            {
            }
        }

        public void Dispose() => _process.Dispose();
    }
}
