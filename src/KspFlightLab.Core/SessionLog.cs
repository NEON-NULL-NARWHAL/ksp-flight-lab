// KSP Flight Lab · src/KspFlightLab.Core/SessionLog.cs
// [Core] The agents' session logs (logs/wingman-<callsign>-<date>.jsonl, written by AgentHost): read back as each
//        agent's story: flights, tests, trainings and their choices, lessons, places, errors.
// Used by: SessionsPage
// Guide:   Meat and Potatoes/04-Agents.md

using System.Globalization;
using System.Text.Json;

namespace KspFlightLab.Core;

/// <summary>One line of a session: when (UTC), the agent, its kind, its text.</summary>
public sealed record SessionLine(DateTime Time, string CallSign, string Kind, string Text);

/// <summary>A day's session of one agent: its file, and the lines worth showing.</summary>
public sealed record Session(string CallSign, DateOnly Day, string Path, IReadOnlyList<SessionLine> Story)
{
    public int Landed => Story.Count(l => l.Kind == "flight" && l.Text.StartsWith("Flight landed", StringComparison.Ordinal));

    public int Crashed => Story.Count(l => l.Kind == "flight" && (l.Text.StartsWith("Flight crashed", StringComparison.Ordinal) ||
                                                                 l.Text.StartsWith("Flight splashed", StringComparison.Ordinal)));

    public int Flights => Story.Count(l => l.Kind == "flight");
}

public static class SessionLog
{
    /// <summary>The kinds that make the story (the rest, status and tool calls, are the detail).</summary>
    public static readonly HashSet<string> StoryKinds = ["flight", "test", "free", "debrief", "lesson", "error", "place"];

    /// <summary>Lines kept per session (a long day has thousands of status lines; its story is a few hundred).</summary>
    public const int MaxStory = 2000;

    /// <summary>A file is skipped past this size (the recorder stops at 20 MB anyway).</summary>
    public const long MaxBytes = 25L * 1024 * 1024;

    /// <summary>Every session in a log folder, newest first.</summary>
    public static IReadOnlyList<Session> List(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        var sessions = new List<Session>();
        foreach (var path in Directory.GetFiles(folder, "wingman-*-*.jsonl"))
        {
            // wingman-<callsign>-yyyy-MM-dd.jsonl
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.Length < "wingman-x-2026-01-01".Length || !DateOnly.TryParseExact(name[^10..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                continue;
            var callSign = name["wingman-".Length..^11];
            sessions.Add(new Session(callSign, day, path, Read(path, callSign)));
        }
        return sessions.OrderByDescending(s => s.Day).ThenBy(s => s.CallSign, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A session file's story lines (unreadable lines are skipped; a file too big gives none).</summary>
    public static IReadOnlyList<SessionLine> Read(string path, string callSign)
    {
        var lines = new List<SessionLine>();
        try
        {
            if (new FileInfo(path).Length > MaxBytes)
                return lines;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (Parse(line, callSign) is { } l && StoryKinds.Contains(l.Kind))
                    lines.Add(l);
            }
        }
        catch (IOException)
        {
        }
        return lines.TakeLast(MaxStory).ToList();
    }

    /// <summary>One JSON line ({"time","kind","text",...}), or null when it isn't one.</summary>
    public static SessionLine? Parse(string line, string callSign)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("time", out var t) || t.ValueKind != JsonValueKind.String ||
                !DateTime.TryParse(t.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time) ||
                !r.TryGetProperty("kind", out var k) || k.ValueKind != JsonValueKind.String)
                return null;
            var text = r.TryGetProperty("text", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "";
            return new SessionLine(time, callSign, k.GetString()!, text.Length > 600 ? text[..600] + "…" : text);
        }
        catch (Exception e) when (e is JsonException or ArgumentException) // ArgumentException: a lone surrogate, not JSON at all
        {
            return null;
        }
    }
}
