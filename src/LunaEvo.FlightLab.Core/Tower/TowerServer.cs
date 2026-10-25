// Flight Lab · src/LunaEvo.FlightLab.Core/Tower/TowerServer.cs
// [Core] The tower's channel over HTTP, for KSP Wingman agents on this PC: POST /v1/report and /v1/say with a small
//        JSON body; the answer is the tower's new messages for that plane. Also the agents' knowledge (Knowledge.cs):
//        /v1/flight (a finished flight), /v1/lesson (a lesson citing flights), /v1/knowledge (skills and lessons),
//        /v1/test (a plane's flight test), /v1/craft (a plane's test and last finals, for its setup) and /v1/place (an
//        explored place).
//        Listens on localhost only.
// Used by: TowerPage
// Guide:   Meat and Potatoes/02-Tower.md

using System.Net;
using System.Text;
using System.Text.Json;

namespace LunaEvo.FlightLab.Core.Tower;

public sealed class TowerServer : IDisposable
{
    public const int DefaultPort = 8920;
    private const int MaxBody = 16 * 1024;

    /// <summary>The day's channel log stops growing here (audit 13, T2): a local flood fills no disk.</summary>
    public const long MaxLogBytes = 20 * 1024 * 1024;

    private readonly HttpListener _listener = new();
    private readonly Func<DateTime> _clock;
    private readonly CancellationTokenSource _stop = new();

    public TowerServer(int port, Func<DateTime>? clock = null)
    {
        Port = port;
        _clock = clock ?? (() => DateTime.UtcNow);
        // localhost only: agents on this PC (the rig, or a Wingman next to the host). No firewall prompt, no LAN.
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public int Port { get; }
    public Tower Tower { get; } = new();

    /// <summary>The agents' skills and lessons. Lock it to read; saved to <see cref="KnowledgePath"/> after each change.</summary>
    public Knowledge Knowledge { get; private set; } = new();

    /// <summary>
    /// The radio: when set (the Tower page's "Talk on LMP chat"), every channel message is also appended here, one JSON
    /// line each, for the LUNA EVO server plugin to say in LMP's chat (&lt;server&gt;/LunaEvo/radio.jsonl).
    /// </summary>
    public string? RadioPath { get; set; }

    /// <summary>The radio file stops growing here (the plugin only reads what's new).</summary>
    public const long MaxRadioBytes = 1024 * 1024;

    /// <summary>Where the knowledge is kept (the server's LunaEvo/agents/knowledge.json), or null for memory only (tests).</summary>
    public string? KnowledgePath { get; init; }

    /// <summary>
    /// Where the channel is kept as data, one JSON line per message (with its route or hold), or null for nowhere:
    /// tower-&lt;date&gt;.jsonl in LUNA EVO's logs folder, so a test or a session can be looked at afterwards.
    /// </summary>
    public string? LogPath { get; init; }

    private int _logged;

    /// <summary>Raised (on a pool thread) after the channel changed.</summary>
    public event Action? Changed;

    public void Start()
    {
        if (KnowledgePath != null)
            Knowledge = Knowledge.Load(KnowledgePath);
        _listener.Start();
        _ = Task.Run(Loop);
    }

    private async Task Loop()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return; // stopped
            }
            _ = Task.Run(() => Handle(context));
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            var request = context.Request;
            var (status, body) = Refuse(request.Headers["Origin"], request.Headers["Host"], request.ContentType, Port) is { } why
                ? (403, Error(why))
                : Answer(request.HttpMethod, request.Url?.AbsolutePath ?? "", Read(request));
            var bytes = Encoding.UTF8.GetBytes(body);
            response.StatusCode = status;
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes);
        }
        catch (Exception e) when (e is HttpListenerException or IOException or ObjectDisposedException)
        {
            // the agent went away mid-answer
        }
        finally
        {
            try { response.Close(); } catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { }
        }
    }

    private static string? Read(HttpListenerRequest request)
    {
        if (request.ContentLength64 > MaxBody)
            return null;
        var buffer = new byte[MaxBody + 1];
        var total = 0;
        int read;
        while ((read = request.InputStream.Read(buffer, total, buffer.Length - total)) > 0)
        {
            total += read;
            if (total > MaxBody)
                return null;
        }
        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    /// <summary>
    /// Why a request isn't an agent's, or null (audit 13, T1). A web page in the user's browser can send a "simple" POST
    /// to localhost without asking first; browsers mark it with an Origin header, a DNS-rebinding page has the wrong
    /// Host, and a JSON content type makes the browser ask first (and the tower never answers yes). Agents send none of
    /// that.
    /// </summary>
    public static string? Refuse(string? origin, string? host, string? contentType, int port)
    {
        if (!string.IsNullOrEmpty(origin))
            return "not from a browser";
        var hostName = host?.Split(':')[0];
        if (hostName is not ("localhost" or "127.0.0.1") || (port != 0 && host != $"{hostName}:{port}"))
            return "localhost only";
        if (contentType?.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase) != true)
            return "application/json only";
        return null;
    }

    /// <summary>One request, without the network (tested directly): the status code and the JSON answer.</summary>
    public (int Status, string Body) Answer(string method, string path, string? body)
    {
        if (method != "POST")
            return (405, Error("POST only"));
        if (body == null)
            return (413, Error("too big"));
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (Exception e) when (e is JsonException or ArgumentException) // ArgumentException: text that isn't valid UTF-16
        {
            return (400, Error("not JSON"));
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "callSign", 40) is not { Length: > 0 } callSign)
                return (400, Error("needs a callSign"));
            IReadOnlyList<TowerMessage> messages;
            lock (Tower)
            {
                if (!Tower.Accepts(callSign))
                    return (429, Error("too many planes on the channel"));
                switch (path)
                {
                    case "/v1/report":
                        if (Number(root, "latitude") is not { } lat || Number(root, "longitude") is not { } lon)
                            return (400, Error("needs latitude and longitude"));
                        messages = Tower.Report(new PlaneReport(callSign, lat, lon, Number(root, "altitude") ?? 0, Number(root, "heading") ?? 0,
                            Number(root, "speed") ?? 0, Text(root, "situation", 20) ?? "", Text(root, "phase", 200) ?? "",
                            Math.Clamp(Number(root, "approachSpeed") ?? 0, 0, 1000)), _clock());
                        break;
                    case "/v1/say":
                        if (Text(root, "kind", 30) is not { Length: > 0 } kind)
                            return (400, Error("needs a kind"));
                        messages = Tower.Say(callSign, kind, Text(root, "text", 300) ?? "", _clock());
                        break;
                    case "/v1/procedures":
                        return (200, Procedures());
                    case "/v1/plan":
                        var names = root.TryGetProperty("fixes", out var fx) && fx.ValueKind == JsonValueKind.Array
                            ? fx.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Take(13)
                                .Select(e => new string((e.GetString() ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').Take(12).ToArray())).ToList()
                            : [];
                        messages = Tower.File(callSign, names, _clock());
                        break;
                    case "/v1/flight" or "/v1/lesson" or "/v1/knowledge" or "/v1/test" or "/v1/craft" or "/v1/place":
                        var answer = Learn(path, callSign, root);
                        if (answer.Status == 200)
                            Changed?.Invoke();
                        return answer;
                    default:
                        return (404, Error("unknown"));
                }
            }
            WriteLog();
            Changed?.Invoke();
            return (200, JsonSerializer.Serialize(new { messages }, Json));
        }
    }

    /// <summary>The published table as JSON: fixes, legs, final fixes, the hold.</summary>
    private static string Procedures()
    {
        var t = Ksc.Table;
        return JsonSerializer.Serialize(new
        {
            runway = "KSC 09",
            fixes = t.Fixes,
            legs = t.Legs.Select(l => new[] { l.From, l.To }),
            finals = t.Finals,
            hold = t.Hold,
        }, Json);
    }

    /// <summary>The knowledge endpoints. Inputs are checked like the channel's: cut strings, finite numbers, short lists.</summary>
    private (int Status, string Body) Learn(string path, string callSign, JsonElement root)
    {
        var now = _clock();
        lock (Knowledge)
        {
            switch (path)
            {
                case "/v1/flight":
                {
                    if (Text(root, "result", 30) is not { Length: > 0 } result)
                        return (400, Error("needs a result"));
                    var phases = root.TryGetProperty("phases", out var p) && p.ValueKind == JsonValueKind.Array
                        ? p.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Take(40)
                            .Select(e => new string((e.GetString() ?? "").Where(char.IsAsciiLetter).Take(20).ToArray())).ToList()
                        : [];
                    var landing = root.TryGetProperty("landing", out var l) && l.ValueKind == JsonValueKind.Object ? l : default;
                    double? FromLanding(string name) => landing.ValueKind == JsonValueKind.Object ? Number(landing, name) : null;
                    var goArounds = (int)Math.Clamp(Number(root, "goArounds") ?? 0, 0, 100);
                    var finals = root.TryGetProperty("finals", out var fl) && fl.ValueKind == JsonValueKind.Array
                        ? fl.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Take(10)
                            .Select(e => new FinalNumbers((int)Math.Clamp(Number(e, "weaves") ?? 0, 0, 100), Math.Round(Math.Clamp(Number(e, "high") ?? 0, -10_000, 10_000)),
                                Number(e, "slope") is { } sl && sl is >= 1 and <= 15 ? sl : null)).ToList()
                        : [];
                    // Wingman 0.12's extras: its chosen final, its fuel, the places it explored (each optional, each checked).
                    double? Fraction(string name) => Number(root, name) is { } f && f is >= 0 and <= 1 ? f : null;
                    var extras = new FlightExtras(Text(root, "style", 40), Fraction("fuelUsed"), Fraction("fuelLeft"),
                        (int)Math.Clamp(Number(root, "explored") ?? 0, 0, 100));
                    var record = Knowledge.AddFlight(callSign, Text(root, "craft", 60) ?? "", result, Text(root, "approach", 40), goArounds,
                        Math.Clamp(Number(root, "minutes") ?? 0, 0, 10_000), FromLanding("downRunway"), FromLanding("offCenterline"),
                        FromLanding("headingError"), phases, now,
                        Math.Clamp(Number(root, "maxSpeed") ?? 0, 0, 5000), Math.Clamp(Number(root, "maxHeight") ?? 0, 0, 100_000), finals, extras);
                    SaveKnowledge();
                    return (200, JsonSerializer.Serialize(new { flight = new { record.Id, record.Skills } }, Json));
                }
                case "/v1/lesson":
                {
                    var evidence = root.TryGetProperty("evidence", out var ev) && ev.ValueKind == JsonValueKind.Array
                        ? ev.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out _)).Select(e => e.GetInt64()).Take(10).ToList()
                        : [];
                    var aboutCraft = root.TryGetProperty("aboutCraft", out var ac) && ac.ValueKind == JsonValueKind.True;
                    var (lesson, problem) = Knowledge.AddLesson(callSign, Text(root, "skill", 30) ?? "", Text(root, "text", Knowledge.MaxLessonText + 50) ?? "", evidence, now, aboutCraft);
                    if (lesson == null)
                        return (422, Error(problem!));
                    SaveKnowledge();
                    return (200, JsonSerializer.Serialize(new { lesson }, Json));
                }
                case "/v1/test":
                {
                    // A flight test (Wingman 0.11): every number must be there and in a range a plane can have.
                    if (Text(root, "craft", 60) is not { Length: > 0 } craft)
                        return (400, Error("needs a craft"));
                    double? In(string name, double max) => Number(root, name) is { } v && v >= 0 && v <= max ? v : null;
                    if (In("pitchRate", 2000) is not { } pitch || In("rollRate", 5000) is not { } roll || In("turnRate", 100) is not { } turn ||
                        In("minSpeed", 1000) is not { } min || In("deceleration", 100) is not { } decel || In("acceleration", 100) is not { } accel ||
                        In("cruiseThrottle", 1) is not { } throttle)
                        return (400, Error("a flight test needs every number, each in range"));
                    // The ground test's numbers are optional (Wingman 0.11 sends them once it has run one).
                    var drift = Number(root, "drift") is { } d && Math.Abs(d) <= 100 ? d : 0;
                    // Its final approach limits (Wingman 0.12): all four, in range and in order, or none.
                    var limits = In("finalMin", 1000) is { } fMin && In("finalMax", 1000) is { } fMax && In("touchdown", 1000) is { } td &&
                                 In("maxSlope", 20) is { } slope && fMin <= fMax ? (fMin, fMax, td, slope) : ((double, double, double, double)?)null;
                    var test = Knowledge.AddTest(callSign, craft, pitch, roll, turn, min, decel, accel, throttle, now,
                        In("steerRate", 1000) ?? 0, drift, In("braking", 100) ?? 0, limits);
                    SaveKnowledge();
                    return (200, JsonSerializer.Serialize(new { test }, Json));
                }
                case "/v1/place":
                {
                    // An explored place (Wingman 0.12): on Kerbin's map, with a ground height and a biome name.
                    if (Number(root, "latitude") is not { } lat || lat is < -90 or > 90 || Number(root, "longitude") is not { } lon || lon is < -180 or > 180)
                        return (400, Error("needs a latitude and a longitude"));
                    var biome = new string((Text(root, "biome", 40) ?? "").Where(c => char.IsLetterOrDigit(c) || c is ' ' or '\'' or '-').ToArray()).Trim();
                    var (place, isNew) = Knowledge.AddPlace(callSign, Math.Round(lat, 4), Math.Round(lon, 4),
                        Math.Round(Math.Clamp(Number(root, "ground") ?? 0, -5000, 20_000)), biome.Length > 0 ? biome : "unknown", now);
                    SaveKnowledge();
                    return (200, JsonSerializer.Serialize(new { place, @new = isNew, known = Knowledge.Places.Count }, Json));
                }
                case "/v1/craft":
                {
                    if (Text(root, "craft", 60) is not { Length: > 0 } craft)
                        return (400, Error("needs a craft"));
                    return (200, JsonSerializer.Serialize(new { test = Knowledge.TestOf(craft), finals = Knowledge.FinalsOf(craft) }, Json));
                }
                default:
                {
                    Knowledge.Expire(now);
                    var skills = Knowledge.Tree.Select(s => new
                    {
                        s.Id, s.Name, s.What, s.Requires,
                        Unlocked = Knowledge.Unlocked(s.Id),
                        Mine = Knowledge.Of(s.Id, callSign),
                        All = Knowledge.Of(s.Id),
                        Lessons = Knowledge.For(s.Id).Select(x => new { x.Id, x.Text, x.Confirmations, Flights = x.Evidence.Count }),
                    });
                    var flights = Knowledge.Flights.Where(f => f.CallSign.Equals(callSign, StringComparison.OrdinalIgnoreCase)).TakeLast(5)
                        .Select(f => new { f.Id, f.Result, f.Approach, f.Craft, f.Skills });
                    var crafts = Knowledge.Crafts().Take(12).Select(c => new
                    {
                        c.Name, c.Flights, c.Landed, c.Crashed, c.TopSpeed, c.TopHeight, c.BestOffCenterline,
                        ToLearn = c.ToLearn.Where(i => !i.Done).Select(i => i.What),
                        Notes = c.Notes.Select(n => n.Text),
                        Test = c.Test?.Describe(),
                    });
                    // The map so far: how many places, which biomes, the highest ground, the last few found.
                    var places = new
                    {
                        Count = Knowledge.Places.Count,
                        Biomes = Knowledge.Places.GroupBy(x => x.Biome).OrderByDescending(g => g.Count()).Select(g => new { Biome = g.Key, Places = g.Count() }),
                        Highest = Knowledge.Places.OrderByDescending(x => x.Ground).FirstOrDefault(),
                        Recent = Knowledge.Places.TakeLast(8),
                    };
                    return (200, JsonSerializer.Serialize(new { skills, flights, crafts, places }, Json));
                }
            }
        }
    }

    private void SaveKnowledge()
    {
        if (KnowledgePath == null)
            return;
        try
        {
            Knowledge.Save(KnowledgePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A failing disk loses the update on disk, not the knowledge in memory.
        }
    }

    /// <summary>An agent stopped (Wingman page): out of the tower's sequence now, and the next plane is cleared.</summary>
    public void Leaving(string callSign)
    {
        lock (Tower)
        {
            if (!Tower.Planes.Any(p => p.CallSign.Equals(callSign, StringComparison.OrdinalIgnoreCase)))
                return;
            Tower.Say(callSign, "leaving", "leaving the channel", _clock());
            WriteLog();
        }
        Changed?.Invoke();
    }

    /// <summary>The host deletes a lesson (Wingman page).</summary>
    public bool DeleteLesson(long id)
    {
        lock (Knowledge)
        {
            if (!Knowledge.Delete(id))
                return false;
            SaveKnowledge();
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Appends the channel's new messages to the log (a failing disk loses the log, not the tower).</summary>
    private void WriteLog()
    {
        List<TowerMessage> fresh; // always taken, so turning the radio on later doesn't replay old lines
        lock (Tower)
        {
            fresh = Tower.Channel.Where(m => m.Id > _logged).ToList();
            _logged = Tower.Channel.Count == 0 ? _logged : Tower.Channel[^1].Id;
        }
        if (fresh.Count == 0 || (LogPath == null && RadioPath == null))
            return;
        if (RadioPath is { } radio)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(radio)!);
                // Full: start a new one (the plugin reads a shorter file from the top). Stopping there went silent for good
                // after ~5,000 lines (audit 21, D3).
                if (File.Exists(radio) && new FileInfo(radio).Length >= MaxRadioBytes)
                    File.Move(radio, radio + ".old", overwrite: true);
                File.AppendAllLines(radio, fresh.Select(m => JsonSerializer.Serialize(new { m.Id, m.Time, m.From, m.To, m.Text }, Json)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        if (LogPath == null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes)
                return;
            File.AppendAllLines(LogPath, fresh.Select(m => JsonSerializer.Serialize(m, Json)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Error(string why) => JsonSerializer.Serialize(new { error = why });

    /// <summary>A string field, cut to a length, without control characters; null when missing or not a string.</summary>
    private static string? Text(JsonElement root, string name, int max)
    {
        if (!root.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String)
            return null;
        var s = new string((v.GetString() ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return s.Length <= max ? s : s[..max];
    }

    private static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch (ObjectDisposedException) { }
    }
}
