// KSP Flight Lab · src/KspFlightLab.Core/Tower/Tower.cs
// [Core] The traffic controller (the owner's idea): KSC tower talks with AI agents (KSP Wingman) on its own channel,
//        asks their intentions, clears them for published RNAV approaches with the whole route, holds the second
//        plane while the first lands, and tells a landed plane to vacate the runway. Pure: no network, no clock.
// Used by: TowerServer, TowerPage
// Guide:   Meat and Potatoes/02-Tower.md

using System.Globalization;

namespace KspFlightLab.Core.Tower;

/// <summary>One point of a route, as the tower sends it: degrees, meters above sea level, meters a second.</summary>
public sealed record TowerFix(string Name, double Latitude, double Longitude, double Altitude, double Speed);

/// <summary>An approach the tower clears a plane for: its name, runway, and the fixes to fly before the final.</summary>
public sealed record TowerRoute(string Name, string Runway, IReadOnlyList<TowerFix> Fixes);

/// <summary>The published table: fixes, the legs allowed between them, the final fixes and the hold.</summary>
public sealed record ProcedureTable(IReadOnlyList<TowerFix> Fixes, IReadOnlyList<(string From, string To)> Legs,
    IReadOnlyList<string> Finals, string Hold);

/// <summary>Where to hold: circle this point at this altitude until cleared.</summary>
public sealed record TowerHold(string Fix, double Latitude, double Longitude, double Altitude, double Radius, int Number);

/// <summary>One line on the tower's channel. Route or Hold come with clearances and holds.</summary>
public sealed record TowerMessage(int Id, DateTime Time, string From, string To, string Kind, string Text,
    TowerRoute? Route = null, TowerHold? Hold = null);

/// <summary>What an agent reports every few seconds.</summary>
/// <param name="ApproachSpeed">The plane's own approach speed (m/s, from its flight test; 0: not said).</param>
public sealed record PlaneReport(string CallSign, double Latitude, double Longitude, double Altitude, double Heading,
    double Speed, string Situation, string Phase = "", double ApproachSpeed = 0);

public enum PlaneState { New, Asked, Local, Approach, Holding, Landed, Gone }

/// <summary>A plane as the tower sees it.</summary>
public sealed class TowerPlane(string callSign)
{
    public string CallSign { get; } = callSign;
    public PlaneState State { get; internal set; } = PlaneState.New;
    public PlaneReport? Last { get; internal set; }
    public DateTime LastSeen { get; internal set; }
    public string? Approach { get; internal set; }
    public int Number { get; internal set; }
    public string Intent { get; internal set; } = "";

    /// <summary>The plan it filed (fix names), flown when it's cleared.</summary>
    public IReadOnlyList<string>? Filed { get; internal set; }

    /// <summary>The approach it asked for, if it named one (granted when nobody else is waiting).</summary>
    public string? Asked { get; internal set; }
    internal int DeliveredId;
    internal DateTime LastGoAround = DateTime.MinValue;

    /// <summary>Cleared for departure until then (or until it's 100 m up): the runway is its.</summary>
    internal DateTime DepartureUntil = DateTime.MinValue;
    internal bool ToldClearedToLand;

    /// <summary>It called "runway in sight" on this approach (the tower clears it to land then, the owner's idea).</summary>
    public bool RunwayInSight { get; internal set; }
}

public sealed class Tower
{
    /// <summary>A go-around reported again this soon is the same one.</summary>
    public static readonly TimeSpan GoAroundRepeat = TimeSpan.FromSeconds(30);

    /// <summary>A departure clearance holds the runway this long at most (a takeoff roll and climb-out take about a minute).</summary>
    public static readonly TimeSpan DepartureTime = TimeSpan.FromSeconds(120);

    /// <summary>A plane that hasn't reported for this long is gone (and gives up its place).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public const string Name = "KSC tower";

    /// <summary>Kept in memory (audit 13, T2: a local flood can't grow it without end); the log keeps everything.</summary>
    public const int MaxChannel = 5000;

    /// <summary>Planes on the channel at once (KSC's pattern takes a handful; anything more is a flood).</summary>
    public const int MaxPlanes = 50;

    private readonly List<TowerMessage> _channel = [];
    private readonly Dictionary<string, TowerPlane> _planes = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<TowerMessage> Channel => _channel;
    public IReadOnlyCollection<TowerPlane> Planes => _planes.Values;

    /// <summary>Whether a call sign may talk: one already on the channel, or a new one while there's room.</summary>
    public bool Accepts(string callSign) =>
        _planes.ContainsKey(callSign) || _planes.Values.Count(p => p.State != PlaneState.Gone) < MaxPlanes;

    /// <summary>A report from a plane: the tower's new messages for it (none, usually).</summary>
    public IReadOnlyList<TowerMessage> Report(PlaneReport report, DateTime now)
    {
        var plane = Plane(report.CallSign, now);
        plane.Last = report;
        plane.LastSeen = now;
        // Only planes: an agent still at the Space Center (no vessel) isn't called yet (the owner saw the tower ask
        // intentions before anyone was on the runway).
        if (plane.State == PlaneState.New && report.Situation is { Length: > 0 } situation && !OutOfPattern(situation))
        {
            plane.State = PlaneState.Asked;
            Send(now, plane.CallSign, "ask-intent", $"{plane.CallSign}, {Name}, say intentions.");
        }
        // Out of the pattern (the owner saw a crashed agent sit at the Space Center while the tower held the only other
        // plane for it): back at the Space Center, wrecked, in the sea, or out of the server. Its place goes to the next.
        if (report.Situation is { Length: > 0 } where && OutOfPattern(where) && plane.State is PlaneState.Approach or PlaneState.Holding or PlaneState.Local or PlaneState.Asked)
            Leave(plane, now, where.ToLowerInvariant() switch
            {
                "spacecenter" => "back at the Space Center",
                "destroyed" => "crashed",
                "splashed" => "down in the sea",
                _ => "left the server",
            });
        var flying = report.Situation.Equals("Flying", StringComparison.OrdinalIgnoreCase);
        var stopped = report.Situation.Equals("Landed", StringComparison.OrdinalIgnoreCase) && report.Speed < 2;
        // A departure is over once it's flying 100 m up, or crashed, splashed or offline (not at the Space Center: it asks from
        // there, before its launch), or after DepartureTime: the next one may go.
        if (plane.DepartureUntil > now && ((flying && report.Altitude > Ksc.RunwayAltitude + 100) || (report.Situation is { Length: > 0 } s2 && OutOfPattern(s2) && !s2.Equals("SpaceCenter", StringComparison.OrdinalIgnoreCase))))
            plane.DepartureUntil = DateTime.MinValue;
        if (plane.State == PlaneState.Approach)
        {
            var (along, cross) = Ksc.Frame(report.Latitude, report.Longitude);
            // Cleared to land when it calls the runway in sight (or, for an agent that never calls, 2.5 km out), on
            // final, with nobody still on the runway.
            // (The fallback only for a plane lined up on the centerline: the overhead hold circles within 2.5 km too.)
            var linedUp = Math.Abs(cross) < 300 && Math.Abs(((report.Heading - Ksc.Heading) % 360 + 540) % 360 - 180) < 30;
            if (!plane.ToldClearedToLand && flying && (plane.RunwayInSight || (along > -SightFallback && linedUp)) && along > -11_000 && along < 0 &&
                Math.Abs(cross) < 1500 && !RunwayOccupied(plane))
            {
                plane.ToldClearedToLand = true;
                Send(now, plane.CallSign, "cleared-to-land", $"{plane.CallSign}, runway 09, cleared to land.");
            }
            if (stopped)
            {
                plane.State = PlaneState.Landed;
                plane.Number = 0;
                Send(now, plane.CallSign, "vacate", $"{plane.CallSign}, welcome. Vacate the runway.");
            }
        }
        // Off the runway (taxied beside it, or recovered: back at the Space Center, not flying) or gone: the next one may come.
        if (plane.State == PlaneState.Landed && (!report.Situation.Equals("Landed", StringComparison.OrdinalIgnoreCase) ||
                                                 Math.Abs(Ksc.Frame(report.Latitude, report.Longitude).Cross) > Ksc.VacatedCross))
            plane.State = PlaneState.Gone;
        Expire(now);
        ClearNext(now);
        return Undelivered(plane);
    }

    /// <summary>
    /// Something a plane says: "intent" (land, local, depart...) or "request-approach". The tower's answer comes back
    /// with the plane's undelivered messages.
    /// </summary>
    public IReadOnlyList<TowerMessage> Say(string callSign, string kind, string text, DateTime now)
    {
        var plane = Plane(callSign, now);
        plane.LastSeen = now;
        Log(now, callSign, Name, kind, text);
        // Departures, one at a time (two agents launching in the same second were put on the same spot in the rig), and
        // none while a plane is cleared to land.
        if (kind == "departure")
        {
            var busy = _planes.Values.FirstOrDefault(p => p != plane && p.DepartureUntil > now && p.State != PlaneState.Gone);
            var landing = _planes.Values.FirstOrDefault(p => p != plane && p.State == PlaneState.Approach && p.ToldClearedToLand);
            if (busy != null || landing != null)
                Send(now, plane.CallSign, "hold-departure", busy != null
                    ? $"{plane.CallSign}, {Name}, hold for departure: {busy.CallSign} is departing."
                    : $"{plane.CallSign}, {Name}, hold for departure: {landing!.CallSign} is landing.");
            else
            {
                plane.DepartureUntil = now + DepartureTime;
                Send(now, plane.CallSign, "cleared-departure", $"{plane.CallSign}, {Name}, runway 09, cleared for departure.");
            }
            return Undelivered(plane);
        }
        // Leaving (the agent stopped, or its player left the server): out of the sequence now, not after the timeout.
        if (kind == "leaving")
        {
            Leave(plane, now, "left the channel");
            plane.State = PlaneState.Gone;
            ClearNext(now);
            return Undelivered(plane);
        }
        // A go-around (the owner's idea): back into the sequence. With others waiting it holds at the back and the next
        // one is cleared; alone it's cleared again at once for the short visual from where it is, for another try.
        // The same go-around again within 30 s is the same one (a client repeating itself): answered once.
        if (kind == "go-around" && plane.State == PlaneState.Approach && now - plane.LastGoAround < GoAroundRepeat)
            return Undelivered(plane);
        if (kind == "go-around" && plane.State == PlaneState.Approach)
        {
            plane.LastGoAround = now;
            plane.ToldClearedToLand = false;
            plane.RunwayInSight = false;
            // The next one goes first, unless it's still far out (the rig: cleared from 46 km away, it kept the plane that
            // went around over the runway waiting ten minutes): then this one tries again first.
            if (_planes.Values.Where(p => p != plane && p.State == PlaneState.Holding).OrderBy(p => p.Number).FirstOrDefault() is { } next &&
                Distance(next) < NearRunway)
            {
                // The next one goes first, then this one joins the back of the hold.
                plane.State = PlaneState.Holding; // counted as traffic in the next one's clearance
                plane.Number = int.MaxValue;
                Clear(next, now);
                foreach (var p in _planes.Values.Where(p => p.State == PlaneState.Holding))
                    p.Number--;
                Hold(plane, now, "roger the go-around. ");
            }
            else
            {
                plane.Asked = Fast(plane) ? null : Ksc.VisualName;
                Clear(plane, now, "roger the go-around, ", ignoreHolding: true);
            }
            return Undelivered(plane);
        }
        if (kind == "runway-in-sight" && plane.State == PlaneState.Approach)
        {
            plane.RunwayInSight = true;
            if (!plane.ToldClearedToLand && RunwayOccupied(plane))
                Send(now, callSign, "info", $"{callSign}, {Name}, continue approach, traffic on the runway.");
            else if (!plane.ToldClearedToLand)
            {
                plane.ToldClearedToLand = true;
                Send(now, callSign, "cleared-to-land", $"{callSign}, runway 09, cleared to land.");
            }
            return Undelivered(plane);
        }
        var wantsToLand = kind == "request-approach" || (kind == "intent" && text.Contains("land", StringComparison.OrdinalIgnoreCase));
        if (wantsToLand && plane.State is not (PlaneState.Approach or PlaneState.Holding))
        {
            plane.Intent = "land";
            plane.Asked = kind == "request-approach" ? Ksc.Named(text) : null; // "request RNAV 09 NORTH", "request visual"
            if (_planes.Values.Any(p => p.State == PlaneState.Approach || (p.State == PlaneState.Landed && p != plane)))
                Hold(plane, now);
            else
                Clear(plane, now);
        }
        else if (kind == "intent" && !wantsToLand)
        {
            plane.Intent = text;
            if (plane.State is PlaneState.New or PlaneState.Asked)
                plane.State = PlaneState.Local;
            Send(now, callSign, "info", $"{callSign}, {Name}, roger. Report when you want to land.");
        }
        return Undelivered(plane);
    }

    /// <summary>The other planes the tower is handling, for traffic information: who, doing what, how high.</summary>
    private string Traffic(TowerPlane plane)
    {
        var others = _planes.Values.Where(p => p != plane && p.State is PlaneState.Approach or PlaneState.Holding or PlaneState.Landed && p.Last != null)
            .OrderBy(p => p.State == PlaneState.Landed ? 0 : p.Number)
            .Select(p => p.CallSign + (p.State switch
            {
                PlaneState.Approach => string.Create(CultureInfo.InvariantCulture, $" on approach at {p.Last!.Altitude:0} m"),
                PlaneState.Holding => string.Create(CultureInfo.InvariantCulture, $" holding over the runway at {p.Last!.Altitude:0} m"),
                _ => " on the runway",
            })).ToList();
        return others.Count == 0 ? "" : " Traffic: " + string.Join("; ", others) + ".";
    }

    /// <summary>A plane whose own approach speed is above this (m/s) never gets the short visual (built for 85-100 m/s).</summary>
    public const double FastApproach = 105;

    /// <summary>Within this far of the runway (m) a waiting plane is near; further, it's still coming in.</summary>
    public const double NearRunway = 15_000;

    /// <summary>
    /// Its turns are too wide for the short visual (the rig's Aeris 4A, 116 m/s on final, turned onto it 1-2 km off the
    /// centerline twice).
    /// </summary>
    private static bool Fast(TowerPlane plane) => plane.Last?.ApproachSpeed > FastApproach;

    private static double Distance(TowerPlane plane)
    {
        if (plane.Last is not { } r)
            return double.MaxValue;
        var (along, cross) = Ksc.Frame(r.Latitude, r.Longitude);
        return Math.Sqrt(along * along + cross * cross);
    }

    /// <param name="ignoreHolding">After a go-around with only far-off planes waiting: cleared as if alone.</param>
    private void Clear(TowerPlane plane, DateTime now, string intro = "", bool ignoreHolding = false)
    {
        var r = plane.Last;
        var (along, cross) = r == null ? (0.0, 0.0) : Ksc.Frame(r.Latitude, r.Longitude);
        // Alone on the runway (nobody holding), it may have what it asked for, or the short visual when that's quicker.
        var alone = !_planes.Values.Any(p => p != plane && (p.State is PlaneState.Approach or PlaneState.Landed || (p.State == PlaneState.Holding && !ignoreHolding)));
        if (Fast(plane) && plane.Asked == Ksc.VisualName)
            plane.Asked = null; // the tower's pick instead
        // A filed plan, if it still checks out from where the plane is now.
        var filed = plane.Filed is { } names ? Ksc.Check(names, along, cross).Route : null;
        var route = filed ?? (alone && plane.Asked != null
            ? Ksc.Route(plane.Asked, cross)
            : Ksc.Pick(along, cross, r?.Heading ?? 90, visualAllowed: alone && !Fast(plane)));
        plane.Filed = null;
        plane.State = PlaneState.Approach;
        plane.Approach = route.Name;
        plane.Number = 1;
        plane.ToldClearedToLand = false;
        plane.RunwayInSight = false;
        var first = route.Fixes[0];
        Send(now, plane.CallSign, "clearance", $"{plane.CallSign}, {Name}, {intro}" + (filed != null
            ? $"cleared as filed: {string.Join(", ", route.Fixes.Select(f => f.Name))}, runway 09."
            : string.Create(CultureInfo.InvariantCulture,
            $"cleared {route.Name}. Proceed to {first.Name} at {first.Altitude:0} m, {first.Speed:0} m/s, " +
            $"then {string.Join(", ", route.Fixes.Skip(1).Select(f => f.Name))}, runway 09.")) + Traffic(plane), Route: route);
    }

    private void Hold(TowerPlane plane, DateTime now, string intro = "")
    {
        var number = _planes.Values.Count(p => p.State is PlaneState.Approach or PlaneState.Holding or PlaneState.Landed && p != plane) + 1;
        var r = plane.Last;
        var (_, cross) = r == null ? (0.0, 1.0) : Ksc.Frame(r.Latitude, r.Longitude);
        // Its own side's downwind fix, stacked 300 m above anyone already holding, well clear of the final.
        // Over the runway (the owner's idea, 2.15): the next plane is right there when the runway frees, stacked 300 m
        // apart from 1,500 m, well above the short visual's downwind (650 m) and the RNAV patterns (900 m).
        var hold = Ksc.OverheadHold(Ksc.OverheadBase + 300 * (number - 2), number);
        plane.State = PlaneState.Holding;
        plane.Number = number;
        var ahead = _planes.Values.FirstOrDefault(p => p.State is PlaneState.Approach or PlaneState.Landed && p != plane);
        Send(now, plane.CallSign, "hold", string.Create(CultureInfo.InvariantCulture,
            $"{plane.CallSign}, {Name}, {intro}number {number}. Hold at {hold.Fix}, {hold.Altitude:0} m, right turns. " +
            $"Expect the approach after {ahead?.CallSign ?? "the traffic"}.") + Traffic(plane), Hold: hold);
    }

    /// <summary>An agent that never calls the runway in sight is cleared this close (m before touchdown).</summary>
    public const double SightFallback = 2500;

    private bool RunwayOccupied(TowerPlane plane) => _planes.Values.Any(p => p != plane && p.State == PlaneState.Landed);

    /// <summary>
    /// A flight plan an agent filed (fix names from the table): checked, then cleared as filed when the runway is free,
    /// or held for traffic and flown when its turn comes; "unable" with the reason otherwise.
    /// </summary>
    public IReadOnlyList<TowerMessage> File(string callSign, IReadOnlyList<string> fixes, DateTime now)
    {
        var plane = Plane(callSign, now);
        plane.LastSeen = now;
        Log(now, callSign, Name, "file-plan", string.Join(" ", fixes));
        var r = plane.Last;
        var (along, cross) = r == null ? (0.0, 0.0) : Ksc.Frame(r.Latitude, r.Longitude);
        var (route, problem) = Ksc.Check(fixes, along, cross);
        if (route == null)
        {
            Send(now, callSign, "info", $"{callSign}, {Name}, unable: {problem}.");
            return Undelivered(plane);
        }
        plane.Filed = route.Fixes.Select(f => f.Name).ToList();
        plane.Intent = "land";
        if (plane.State is PlaneState.Approach)
            Clear(plane, now); // already cleared: the new plan replaces the route
        else if (plane.State is not PlaneState.Holding)
        {
            if (_planes.Values.Any(p => p.State == PlaneState.Approach || (p.State == PlaneState.Landed && p != plane)))
                Hold(plane, now);
            else
                Clear(plane, now);
        }
        return Undelivered(plane);
    }

    /// <summary>When the runway is free (no one on approach or still on it), the next holding plane is cleared.</summary>
    private void ClearNext(DateTime now)
    {
        if (_planes.Values.Any(p => p.State is PlaneState.Approach or PlaneState.Landed))
            return;
        var next = _planes.Values.Where(p => p.State == PlaneState.Holding).OrderBy(p => p.Number).FirstOrDefault();
        if (next != null)
            Clear(next, now);
        foreach (var p in _planes.Values.Where(p => p.State == PlaneState.Holding))
            p.Number--;
    }

    /// <summary>Situations that put a plane out of the pattern (SpaceCenter, Destroyed, Offline are Wingman's own words).</summary>
    public static bool OutOfPattern(string situation) =>
        situation.ToLowerInvariant() is "spacecenter" or "destroyed" or "splashed" or "offline";

    /// <summary>
    /// A plane leaves the sequence: those behind it move up, and (from Report and Say) the next one is cleared when the
    /// runway is free. It's asked its intentions again when it next has a plane in the air.
    /// </summary>
    private void Leave(TowerPlane plane, DateTime now, string why)
    {
        var inSequence = plane.State is PlaneState.Approach or PlaneState.Holding;
        if (plane.State == PlaneState.Holding)
            foreach (var p in _planes.Values.Where(p => p != plane && p.State == PlaneState.Holding && p.Number > plane.Number))
                p.Number--;
        (plane.State, plane.Number, plane.Approach, plane.Filed, plane.Asked) = (PlaneState.New, 0, null, null, null);
        (plane.ToldClearedToLand, plane.RunwayInSight, plane.DepartureUntil) = (false, false, DateTime.MinValue);
        if (inSequence)
            Log(now, Name, "all", "info", $"{plane.CallSign} {why}: out of the sequence.");
    }

    private void Expire(DateTime now)
    {
        foreach (var p in _planes.Values.Where(p => p.State != PlaneState.Gone && now - p.LastSeen > Timeout).ToList())
        {
            p.State = PlaneState.Gone;
            Log(now, Name, "all", "info", $"{p.CallSign} lost from the channel.");
        }
    }

    private TowerPlane Plane(string callSign, DateTime now)
    {
        if (!_planes.TryGetValue(callSign, out var plane) || plane.State == PlaneState.Gone && now - plane.LastSeen > Timeout)
            _planes[callSign] = plane = new TowerPlane(callSign);
        return plane;
    }

    private void Send(DateTime now, string to, string kind, string text, TowerRoute? Route = null, TowerHold? Hold = null) =>
        Log(now, Name, to, kind, text, Route, Hold);

    private int _nextId = 1;

    private void Log(DateTime now, string from, string to, string kind, string text, TowerRoute? route = null, TowerHold? hold = null)
    {
        _channel.Add(new TowerMessage(_nextId++, now, from, to, kind, text, route, hold));
        if (_channel.Count > MaxChannel)
            _channel.RemoveRange(0, _channel.Count - MaxChannel);
        foreach (var gone in _planes.Where(p => p.Value.State == PlaneState.Gone && now - p.Value.LastSeen > Timeout * 10).Select(p => p.Key).ToList())
            _planes.Remove(gone);
    }

    private IReadOnlyList<TowerMessage> Undelivered(TowerPlane plane)
    {
        var mine = _channel.Where(m => m.Id > plane.DeliveredId && m.From == Name && m.To.Equals(plane.CallSign, StringComparison.OrdinalIgnoreCase)).ToList();
        plane.DeliveredId = _nextId - 1;
        return mine;
    }
}

/// <summary>
/// KSC's runway 09 and its published approaches (the same as KSP Wingman's, which flies them without a tower): the
/// runway frame is meters along the runway from the touchdown point and to the right of the centerline.
/// </summary>
public static class Ksc
{
    public const double ThresholdLatitude = -0.0486, ThresholdLongitude = -74.7244, Heading = 90, TouchdownAfterThreshold = 300;

    /// <summary>The runway's height above sea level (m).</summary>
    public const double RunwayAltitude = 69;
    public const double KerbinRadius = 600_000;
    private const double MetersPerDegree = KerbinRadius * Math.PI / 180;

    /// <summary>This far beside the centerline (m), a plane has left the runway (Wingman parks 90 m out).</summary>
    public const double VacatedCross = 60;

    public static (double Along, double Cross) Frame(double latitude, double longitude)
    {
        var north = (latitude - ThresholdLatitude) * MetersPerDegree;
        var east = (longitude - ThresholdLongitude) * MetersPerDegree * Math.Cos(latitude * Math.PI / 180);
        return (east - TouchdownAfterThreshold, -north); // landing east: along is east, right of the centerline is south
    }

    public static TowerFix At(string name, double along, double cross, double altitude, double speed)
    {
        var lat = ThresholdLatitude - cross / MetersPerDegree;
        var lon = ThresholdLongitude + (along + TouchdownAfterThreshold) / (MetersPerDegree * Math.Cos(lat * Math.PI / 180));
        return new TowerFix(name, Math.Round(lat, 6), Math.Round(lon, 6), altitude, speed);
    }

    private static TowerFix Final => At("F09", -8000, 0, 69 + 8000 * Math.Tan(4 * Math.PI / 180), 90);

    public static TowerRoute StraightIn => new("RNAV 09 STRAIGHT-IN", "KSC 09", [At("KSC-W12", -12_000, 0, 900, 120), Final]);
    public static TowerRoute North => new("RNAV 09 NORTH", "KSC 09",
        [At("KSC-N1", -1000, -4000, 900, 110), At("KSC-N2", -11_000, -4000, 800, 90), At("KSC-N3", -11_000, 0, 750, 90), Final]);
    public static TowerRoute South => new("RNAV 09 SOUTH", "KSC 09",
        [At("KSC-S1", -1000, 4000, 900, 110), At("KSC-S2", -11_000, 4000, 800, 90), At("KSC-S3", -11_000, 0, 750, 90), Final]);

    /// <summary>
    /// The short visual (the owner's "visual override"): the RNAV patterns' shape made smaller, on the plane's side:
    /// downwind 3 km out, a 90° base turn 7 km before the runway onto the centerline, and a 4.5 km final (about 16 km
    /// flown instead of 25). A diagonal base into a 4 km final overshot the centerline by 2 km three times in the rig:
    /// at 90 m/s the 30° turn is 1.4 km wide, so each turn needs the room.
    /// </summary>
    public static TowerRoute Visual(double cross)
    {
        var s = cross < 0 ? -1 : 1;
        var side = s < 0 ? "N" : "S";
        return new TowerRoute(VisualName, "KSC 09",
        [
            At($"KSC-V{side}1", 0, s * 3000, 650, 100), At($"KSC-V{side}2", -8000, s * 3000, 620, 90), At($"KSC-V{side}3", -8000, 0, 600, 85),
            At("F09V", -VisualFinal, 0, Math.Round(69 + VisualFinal * Math.Tan(4 * Math.PI / 180)), 85),
        ]);
    }

    public const string VisualName = "VISUAL 09";

    /// <summary>Where the short visual's final starts (m before touchdown).</summary>
    public const double VisualFinal = 5500; // 4500 left it swinging across the centerline at the threshold (rig, 2.13)

    /// <summary>Within this distance of the touchdown point, the short visual is quicker than any RNAV route.</summary>
    public const double VisualWithin = 12_000;

    /// <summary>
    /// The same rule as Wingman's own: far out west and facing the runway, straight in; close in (and allowed: nobody else
    /// waiting), the short visual; otherwise its side's pattern.
    /// </summary>
    public static TowerRoute Pick(double along, double cross, double heading, bool visualAllowed = false)
    {
        var off = ((heading - Heading) % 360 + 540) % 360 - 180;
        if (Direct(along, cross, off) is { } direct)
            return direct;
        if (along < -9000 && Math.Abs(cross) < 3000 && Math.Abs(off) < 60)
            return StraightIn;
        if (visualAllowed && Math.Sqrt(along * along + cross * cross) < VisualWithin)
            return Visual(cross);
        return cross < 0 ? North : South;
    }

    /// <summary>
    /// A direct (the owner's idea, 2.15): west of the runway, roughly facing it, and able to join the final at 45° or
    /// less, a plane goes straight to the final fix (F09 from far out, F09V closer in) instead of a whole route.
    /// </summary>
    public static TowerRoute? Direct(double along, double cross, double headingOff)
    {
        var fixAlong = along < -10_500 ? -8000.0 : -VisualFinal;
        var toGo = fixAlong - along; // meters still to fly along the runway's line to the fix
        if (toGo < 2000 || Math.Abs(cross) > toGo || Math.Abs(headingOff) > 90 || Math.Sqrt(toGo * toGo + cross * cross) > 25_000)
            return null;
        var fix = fixAlong == -8000 ? At("F09", -8000, 0, Math.Round(69 + 8000 * Math.Tan(4 * Math.PI / 180)), 90)
                                    : At("F09V", -VisualFinal, 0, Math.Round(69 + VisualFinal * Math.Tan(4 * Math.PI / 180)), 85);
        return new TowerRoute("DIRECT " + fix.Name, "KSC 09", [fix]);
    }

    /// <summary>Departure fixes 5 km past the runway, 4 km to each side: the climb-out joins a pattern from there.</summary>
    public static TowerFix DepartNorth => At("KSC-DN", 5000, -4000, 900, 130);
    public static TowerFix DepartSouth => At("KSC-DS", 5000, 4000, 900, 130);

    /// <summary>
    /// The published table (the owner's idea, 2.15): every fix, the legs allowed between them (a small airway graph),
    /// the final fixes and the hold, for an agent to build its own flight plan from, like a pilot from the charts.
    /// </summary>
    public static ProcedureTable Table { get; } = BuildTable();

    private static ProcedureTable BuildTable()
    {
        var routes = new[] { North, South, StraightIn, Visual(-1), Visual(1) };
        var fixes = routes.SelectMany(r => r.Fixes).Concat([DepartNorth, DepartSouth, OverheadHoldFix])
            .GroupBy(f => f.Name).Select(g => g.First()).ToList();
        var legs = routes.SelectMany(r => r.Fixes.Zip(r.Fixes.Skip(1), (a, b) => (a.Name, b.Name))).ToList();
        legs.AddRange([("KSC-DN", "KSC-N1"), ("KSC-DS", "KSC-S1"), ("KSC-DN", "KSC-VN1"), ("KSC-DS", "KSC-VS1"),
            ("KSC-OH", "KSC-VN1"), ("KSC-OH", "KSC-VS1"), ("KSC-OH", "KSC-N1"), ("KSC-OH", "KSC-S1")]);
        return new ProcedureTable(fixes, legs.Distinct().ToList(), ["F09", "F09V"], "KSC-OH");
    }

    private static TowerFix OverheadHoldFix => At("KSC-OH", 900, 0, OverheadBase, 100);

    /// <summary>
    /// Checks a filed plan against the table: known fixes, each step a published leg, ending on a final fix, the first
    /// fix within 25 km of the plane, 12 fixes at most. Returns the route to fly, or why not.
    /// </summary>
    public static (TowerRoute? Route, string? Problem) Check(IReadOnlyList<string> names, double along, double cross)
    {
        var table = Table;
        if (names.Count is 0 or > 12)
            return (null, "a plan is 1 to 12 fixes");
        var byName = table.Fixes.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var fixes = new List<TowerFix>();
        foreach (var n in names)
        {
            if (!byName.TryGetValue(n.Trim(), out var f))
                return (null, $"no fix called {n.Trim()}");
            fixes.Add(f);
        }
        for (var i = 1; i < fixes.Count; i++)
            if (!table.Legs.Contains((fixes[i - 1].Name, fixes[i].Name)))
                return (null, $"{fixes[i - 1].Name} to {fixes[i].Name} isn't a published leg");
        if (!table.Finals.Contains(fixes[^1].Name))
            return (null, $"a plan ends on a final fix ({string.Join(" or ", table.Finals)})");
        var (fa, fc) = Frame(fixes[0].Latitude, fixes[0].Longitude);
        if (Math.Sqrt(Math.Pow(fa - along, 2) + Math.Pow(fc - cross, 2)) > 25_000)
            return (null, $"{fixes[0].Name} is more than 25 km away");
        return (new TowerRoute("FILED " + string.Join(" ", fixes.Select(f => f.Name)), "KSC 09", fixes), null);
    }

    /// <summary>The overhead hold: circles centered over the middle of the runway.</summary>
    public static TowerHold OverheadHold(double altitude, int number)
    {
        var fix = At("KSC-OH", 900, 0, altitude, 100);
        return new TowerHold(fix.Name, fix.Latitude, fix.Longitude, altitude, 2000, number);
    }

    /// <summary>The overhead hold's lowest level (number 2), m.</summary>
    public const double OverheadBase = 1500;

    /// <summary>An approach named in a request ("request RNAV 09 NORTH", "visual", "straight-in"), or null.</summary>
    public static string? Named(string text)
    {
        var t = text.ToUpperInvariant();
        return t.Contains("VISUAL") ? VisualName
            : t.Contains("STRAIGHT") ? StraightIn.Name
            : t.Contains("NORTH") ? North.Name
            : t.Contains("SOUTH") ? South.Name
            : null;
    }

    public static TowerRoute Route(string name, double cross) =>
        name == VisualName ? Visual(cross) : name == StraightIn.Name ? StraightIn : name == North.Name ? North : South;

}
