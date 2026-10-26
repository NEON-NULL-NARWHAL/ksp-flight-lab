// KSP Flight Lab · src/KspFlightLab.Core/Tower/Knowledge.cs
// [Core] What the server's AI agents know (the owner's "tech tree", kept on the LMP server): skills graded from their
//        real flights (KSP Wingman's outcome records), and lessons that must cite those flights. Pure, plus Load/Save.
// Used by: TowerServer (/v1/flight, /v1/lesson, /v1/knowledge, /v1/test, /v1/craft, /v1/place), the Knowledge pages
// Guide:   Meat and Potatoes/03-Knowledge.md

using System.Text.Json;

namespace KspFlightLab.Core.Tower;

/// <summary>A skill in the tree: what it is, which skills come first, and how a flight shows it.</summary>
public sealed record Skill(string Id, string Name, string What, IReadOnlyList<string> Requires);

/// <summary>One finished flight as the server keeps it: Wingman's outcome, and what it says about each skill it used.</summary>
public sealed record FlightRecord(long Id, DateTime Time, string CallSign, string Craft, string Result, string? Approach,
    int GoArounds, double Minutes, double? DownRunway, double? OffCenterline, double? HeadingError, IReadOnlyList<string> Phases,
    IReadOnlyDictionary<string, bool> Skills, double MaxSpeed = 0, double MaxHeight = 0, IReadOnlyList<FinalNumbers>? Finals = null,
    FlightExtras? Extras = null);

/// <summary>
/// What Wingman 0.12 adds to a flight (2.18): the approach the agent chose ("steep 6°, final 125 m/s"), the fuel it
/// used and had left (0-1 of its tanks), and the places it explored.
/// </summary>
public sealed record FlightExtras(string? Style = null, double? FuelUsed = null, double? FuelLeft = null, int Explored = 0);

/// <summary>A place an agent explored (2.18): where, how high the ground is (m), its biome, and how often agents went there.</summary>
public sealed record Place(long Id, DateTime Time, string CallSign, double Latitude, double Longitude, double Ground, string Biome, int Visits = 1);

/// <summary>How one final went: centerline crossings (weaving), meters above the glide slope 1 km out, and the slope the agent chose (null: 4°).</summary>
public sealed record FinalNumbers(int Weaves, double High, double? Slope = null);

/// <summary>
/// A flight test of a plane (KSP Wingman's test card, 2.17): what it measured. Rates in degrees a second for a full stick
/// at 150 m/s; speeds m/s; accelerations m/s²; the throttle 0-1.
/// </summary>
/// <param name="SteerRate">The ground test (0: none): °/s of heading for a full nose wheel at 10 m/s; Drift °/s it veers
/// with the wheel straight (+ right); Braking m/s².</param>
/// <param name="FinalMin">Its knowledge card's final approach limits, worked out by Wingman from the test (0: an older
/// Wingman): final speeds FinalMin-FinalMax m/s, touching down at Touchdown m/s, glide slopes up to MaxSlope degrees.</param>
public sealed record CraftTest(long Id, DateTime Time, string CallSign, string Craft, double PitchRate, double RollRate, double TurnRate,
    double MinSpeed, double Deceleration, double Acceleration, double CruiseThrottle, double SteerRate = 0, double Drift = 0, double Braking = 0,
    double FinalMin = 0, double FinalMax = 0, double Touchdown = 0, double MaxSlope = 0)
{
    public string Describe() => FormattableString.Invariant(
        $"pitch {PitchRate:0} °/s and roll {RollRate:0} °/s per full stick, turns {TurnRate:0.0} °/s at 30° of bank, slowest {MinSpeed:0} m/s, slows {Deceleration:0.0} m/s² at idle, speeds up {Acceleration:0.0} m/s² at full throttle") +
        (SteerRate > 0 ? FormattableString.Invariant($"; on the ground: nose wheel {SteerRate:0.0} °/s per full lock at 10 m/s, veers {Math.Abs(Drift):0.0} °/s {(Drift >= 0 ? "right" : "left")} with it straight, brakes {Braking:0.0} m/s²") : "") +
        (FinalMax > 0 ? FormattableString.Invariant($"; final approach {FinalMin:0}-{FinalMax:0} m/s, touchdown {Touchdown:0} m/s, glide slope up to {MaxSlope:0}°") : "");
}

/// <summary>One thing to learn about a plane, whether it's been done, and what was found.</summary>
public sealed record CraftItem(string What, bool Done, string Note = "");

/// <summary>
/// What the server's agents know about one plane (the owner's idea): its flights, how they went, what it can do, the
/// notes about its handling, and the list of what's still to learn about it.
/// </summary>
public sealed record CraftProfile(string Name, int Flights, int Landed, int Crashed, int GoodLandings, double? BestOffCenterline,
    double TopSpeed, double TopHeight, IReadOnlyList<string> FlownBy, IReadOnlyList<CraftItem> ToLearn, IReadOnlyList<Lesson> Notes,
    CraftTest? Test = null);

/// <summary>A short fact an agent learned, with the flights it came from. Confirmed when another flight says it again.</summary>
/// <param name="Craft">The plane the lesson is about (its handling, speeds), or null for one about flying in general.</param>
public sealed record Lesson(long Id, string Skill, string Text, string Author, DateTime Created, DateTime Confirmed,
    IReadOnlyList<long> Evidence, int Confirmations, string? Craft = null)
{
    /// <summary>Lessons with more evidence come first.</summary>
    public int Weight => Evidence.Count + Confirmations;
}

/// <summary>A skill's numbers: good flights out of attempts.</summary>
public readonly record struct Proficiency(int Good, int Attempts)
{
    public override string ToString() => $"{Good} of {Attempts} good";
}

public sealed class Knowledge
{
    /// <summary>The tree, easiest first. A skill is open to practise once each one it requires has this many good flights.</summary>
    public const int UnlockAfter = 3;

    public static readonly IReadOnlyList<Skill> Tree =
    [
        new("takeoff", "Take off", "off the runway and climbing out", []),
        new("route", "Fly a route", "an approach's fixes, to the final", ["takeoff"]),
        new("hold", "Hold", "circling a hold point until cleared", ["takeoff"]),
        new("land-visual", "Land, visual", "a circuit or the short visual, then the final", ["takeoff"]),
        new("land-rnav", "Land, RNAV", "a published RNAV approach, then the final", ["route", "land-visual"]),
        new("sequencing", "Land in sequence", "hold for the traffic, then land when the tower clears it", ["hold", "land-rnav"]),
        new("fly-fast", "Fly fast", "over 200 m/s, and back down to land", ["takeoff"]),
        new("fly-high", "Fly high", "above 5,000 m over the ground, and back down to land", ["takeoff"]),
        new("new-craft", "A new plane", "a good landing in a plane this agent hadn't landed before", ["land-visual"]),
        // 2.18 (the owner's ideas): riskier finals, exploring the map, longer flights on the fuel it has.
        new("approaches", "Approach variety", "a final of its own choosing (steep, shallow, fast or slow), landed well", ["land-visual"]),
        new("explore", "Explore", "out to three places of its own choosing, noting what's there, and back to land", ["takeoff"]),
        new("long-flight", "Long flight", "20 minutes or more, landing with a quarter of its fuel or more", ["takeoff"]),
    ];

    /// <summary>A long flight lasts this long (minutes) and lands with this much fuel left (0-1), at least.</summary>
    public const double LongFlightMinutes = 20, FuelReserve = 0.25;

    /// <summary>Explored places within this far (m) of one already known count as visiting it again.</summary>
    public const double SamePlace = 1000;

    public const int MaxPlaces = 1000;

    /// <summary>Fast enough (m/s) and high enough (m above the ground) for fly-fast and fly-high.</summary>
    public const double FastSpeed = 200, HighHeight = 5000;

    public const int MaxFlights = 2000;
    public const int MaxLessons = 300;
    public const int MaxLessonsPerSkill = 20;
    public const int MaxLessonText = 200;
    public const int MaxTests = 200;

    /// <summary>Lessons nobody confirmed are dropped after this long.</summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromDays(14);

    /// <summary>A good landing stops within this far of the centerline, without a go-around.</summary>
    public const double GoodCenterline = 15;

    private readonly List<FlightRecord> _flights = [];
    private readonly List<Lesson> _lessons = [];
    private long _nextFlight = 1, _nextLesson = 1;

    public IReadOnlyList<FlightRecord> Flights => _flights;
    public IReadOnlyList<Lesson> Lessons => _lessons;

    /// <summary>
    /// How a flight shows each skill it used (true = done well). From the phases it went through, in order, and how it
    /// ended: a landing counts as good when it stopped on the runway within 15 m of the centerline, first time.
    /// </summary>
    public static Dictionary<string, bool> Grade(string result, string? approach, int goArounds, double? offCenterline, IReadOnlyList<string> phases,
        double maxSpeed = 0, double maxHeight = 0, FlightExtras? extras = null, double minutes = 0)
    {
        var skills = new Dictionary<string, bool>();
        int At(string phase) => phases.ToList().IndexOf(phase);
        bool Has(string phase) => At(phase) >= 0;
        var airborne = Has("Climbing") || Has("Cruising") || Has("OnRoute") || Has("Holding") || Has("OnFinal");
        if (Has("TakingOff") && airborne)
            skills["takeoff"] = Has("Cruising") || Has("OnRoute") || Has("Holding") || Has("OnFinal") || result != "crashed";
        var landedWell = result == "landed" && goArounds == 0 && offCenterline is { } off && off <= GoodCenterline;
        if (Has("OnRoute"))
            skills["route"] = Has("OnFinal");
        if (Has("Holding"))
            skills["hold"] = (At("OnRoute") > At("Holding") || At("OnFinal") > At("Holding")) && result != "crashed";
        if (Has("OnFinal"))
        {
            // An RNAV approach: a published one, or a filed plan or a direct ending on F09 (F09V's short final is the visual's).
            var rnav = approach != null && (approach.StartsWith("RNAV", StringComparison.OrdinalIgnoreCase) ||
                       ((approach.StartsWith("FILED", StringComparison.OrdinalIgnoreCase) || approach.StartsWith("DIRECT", StringComparison.OrdinalIgnoreCase)) &&
                        approach.EndsWith(" F09", StringComparison.OrdinalIgnoreCase)));
            skills[rnav ? "land-rnav" : "land-visual"] = landedWell;
            if (Has("Holding") && rnav)
                skills["sequencing"] = landedWell;
        }
        // The training drills (2.15): flown fast or high, and got back down.
        if (maxSpeed >= FastSpeed * 0.9)
            skills["fly-fast"] = maxSpeed >= FastSpeed && result == "landed";
        if (maxHeight >= HighHeight * 0.8)
            skills["fly-high"] = maxHeight >= HighHeight && result == "landed";
        // 2.18: a final of the agent's own choosing, exploring, and a long flight on the fuel it had.
        if (Has("OnFinal") && extras?.Style is { Length: > 0 })
            skills["approaches"] = landedWell;
        if (extras?.Explored > 0)
            skills["explore"] = extras.Explored >= 3 && result == "landed";
        if (minutes >= LongFlightMinutes && extras?.FuelLeft is { } left)
            skills["long-flight"] = result == "landed" && left >= FuelReserve;
        return skills;
    }

    public FlightRecord AddFlight(string callSign, string craft, string result, string? approach, int goArounds, double minutes,
        double? downRunway, double? offCenterline, double? headingError, IReadOnlyList<string> phases, DateTime now,
        double maxSpeed = 0, double maxHeight = 0, IReadOnlyList<FinalNumbers>? finals = null, FlightExtras? extras = null)
    {
        var skills = Grade(result, approach, goArounds, offCenterline, phases, maxSpeed, maxHeight, extras, minutes);
        // A plane it hadn't landed well before (the owner's "try different planes"): the landing in it counts once more.
        var landing = skills.FirstOrDefault(s => s.Key.StartsWith("land-", StringComparison.Ordinal));
        if (craft.Length > 0 && landing.Key != null && !_flights.Any(f => f.CallSign.Equals(callSign, StringComparison.OrdinalIgnoreCase) &&
                f.Craft.Equals(craft, StringComparison.OrdinalIgnoreCase) && f.Skills.Any(s => s.Key.StartsWith("land-", StringComparison.Ordinal) && s.Value)))
        {
            var others = _flights.Any(f => f.CallSign.Equals(callSign, StringComparison.OrdinalIgnoreCase) && !f.Craft.Equals(craft, StringComparison.OrdinalIgnoreCase));
            if (others) // its first plane isn't "a new plane"
                skills["new-craft"] = landing.Value;
        }
        var record = new FlightRecord(_nextFlight++, now, callSign, craft, result, approach, goArounds, minutes, downRunway, offCenterline,
            headingError, phases, skills, maxSpeed, maxHeight, finals is { Count: > 0 } ? finals : null,
            extras is { Style: not null } or { FuelUsed: not null } or { FuelLeft: not null } or { Explored: > 0 } ? extras : null);
        _flights.Add(record);
        if (_flights.Count > MaxFlights)
            _flights.RemoveRange(0, _flights.Count - MaxFlights);
        return record;
    }

    /// <summary>Good flights out of attempts for a skill: one agent's, or everyone's (null).</summary>
    public Proficiency Of(string skill, string? callSign = null)
    {
        var attempts = _flights.Where(f => f.Skills.ContainsKey(skill) && (callSign == null || f.CallSign.Equals(callSign, StringComparison.OrdinalIgnoreCase))).ToList();
        return new Proficiency(attempts.Count(f => f.Skills[skill]), attempts.Count);
    }

    /// <summary>Open to practise: everything it requires has been done well enough, by any agent on the server (shared).</summary>
    public bool Unlocked(string skill) =>
        Tree.First(s => s.Id == skill).Requires.All(r => Of(r).Good >= UnlockAfter);

    /// <summary>
    /// Adds a lesson, or confirms one that says the same. Refused (with why) without evidence: it must cite flights by
    /// the same agent that used the skill. Lessons are data shown to agents as facts, never as orders.
    /// </summary>
    /// <param name="aboutCraft">The lesson is about the plane of the flights it cites (they must all be the same plane).</param>
    public (Lesson? Lesson, string? Problem) AddLesson(string callSign, string skill, string text, IReadOnlyList<long> evidence, DateTime now,
        bool aboutCraft = false)
    {
        text = text.Trim();
        if (Tree.All(s => s.Id != skill))
            return (null, $"no skill \"{skill}\" (skills: {string.Join(", ", Tree.Select(s => s.Id))})");
        if (text.Length is < 10 or > MaxLessonText)
            return (null, $"a lesson is 10 to {MaxLessonText} characters");
        var cited = evidence.Distinct().Take(10).Where(id => _flights.Any(f => f.Id == id && f.CallSign.Equals(callSign, StringComparison.OrdinalIgnoreCase) && f.Skills.ContainsKey(skill))).ToList();
        if (cited.Count == 0)
            return (null, $"a lesson must cite a flight of yours that used {skill} (its flight id)");
        string? craft = null;
        if (aboutCraft)
        {
            var crafts = _flights.Where(f => cited.Contains(f.Id)).Select(f => f.Craft).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (crafts.Count != 1 || crafts[0].Length == 0)
                return (null, "a note about a plane cites flights in that one plane");
            craft = crafts[0];
        }

        var same = _lessons.Where(l => l.Skill == skill && string.Equals(l.Craft, craft, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => Similarity(l.Text, text)).FirstOrDefault();
        if (same != null && Similarity(same.Text, text) >= 0.6)
        {
            var fresh = cited.Except(same.Evidence).ToList();
            var confirmed = same with
            {
                Evidence = same.Evidence.Concat(fresh).TakeLast(20).ToList(),
                Confirmations = same.Confirmations + (fresh.Count > 0 ? 1 : 0),
                Confirmed = now,
            };
            _lessons[_lessons.IndexOf(same)] = confirmed;
            return (confirmed, null);
        }
        var lesson = new Lesson(_nextLesson++, skill, text, callSign, now, now, cited, 0, craft);
        _lessons.Add(lesson);
        // Too many for a skill or overall: the weakest, oldest goes.
        foreach (var group in new[] { _lessons.Where(l => l.Skill == skill).ToList() })
            if (group.Count > MaxLessonsPerSkill)
                _lessons.Remove(group.OrderBy(l => l.Weight).ThenBy(l => l.Confirmed).First());
        if (_lessons.Count > MaxLessons)
            _lessons.Remove(_lessons.OrderBy(l => l.Weight).ThenBy(l => l.Confirmed).First());
        return (lesson, null);
    }

    /// <summary>Word overlap (Jaccard) of two lessons, 0..1: "the same lesson" from 0.6.</summary>
    public static double Similarity(string a, string b)
    {
        static HashSet<string> Words(string s) => s.ToLowerInvariant()
            .Split([' ', ',', '.', ';', ':', '!', '?', '(', ')', '"'], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2).ToHashSet();
        var x = Words(a);
        var y = Words(b);
        return x.Count + y.Count == 0 ? 0 : (double)x.Intersect(y).Count() / x.Union(y).Count();
    }

    private readonly List<CraftTest> _tests = [];
    private long _nextTest = 1;

    public IReadOnlyList<CraftTest> Tests => _tests;

    /// <summary>A flight test's numbers (the newest per plane is the one used).</summary>
    public CraftTest AddTest(string callSign, string craft, double pitchRate, double rollRate, double turnRate, double minSpeed,
        double deceleration, double acceleration, double cruiseThrottle, DateTime now, double steerRate = 0, double drift = 0, double braking = 0,
        (double Min, double Max, double Touchdown, double Slope)? limits = null)
    {
        var l = limits ?? (0, 0, 0, 0);
        var test = new CraftTest(_nextTest++, now, callSign, craft, pitchRate, rollRate, turnRate, minSpeed, deceleration, acceleration, cruiseThrottle,
            steerRate, drift, braking, l.Min, l.Max, l.Touchdown, l.Slope);
        _tests.Add(test);
        if (_tests.Count > MaxTests)
            _tests.RemoveRange(0, _tests.Count - MaxTests);
        return test;
    }

    /// <summary>A plane's newest flight test, or null.</summary>
    public CraftTest? TestOf(string craft) => _tests.LastOrDefault(t => t.Craft.Equals(craft, StringComparison.OrdinalIgnoreCase));

    /// <summary>A plane's last finals (any agent's), oldest first: what its setup is corrected by.</summary>
    public IReadOnlyList<FinalNumbers> FinalsOf(string craft, int max = 5) => _flights
        .Where(f => f.Craft.Equals(craft, StringComparison.OrdinalIgnoreCase) && f.Finals != null)
        .SelectMany(f => f.Finals!).TakeLast(max).ToList();

    /// <summary>Every plane flown on the server, most flown first, with what's known and what's still to learn about it.</summary>
    public IReadOnlyList<CraftProfile> Crafts() => _flights.Where(f => f.Craft.Length > 0)
        .GroupBy(f => f.Craft, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(g => g.Count())
        .Select(g => Profile(g.First().Craft, g.ToList())).ToList();

    private CraftProfile Profile(string name, List<FlightRecord> flights)
    {
        bool Good(FlightRecord f, string skill) => f.Skills.TryGetValue(skill, out var good) && good;
        var landed = flights.Count(f => f.Result == "landed");
        var crashed = flights.Count(f => f.Result is "crashed" or "splashed");
        var goodLandings = flights.Count(f => Good(f, "land-visual") || Good(f, "land-rnav"));
        var best = flights.Where(f => f.Result == "landed" && f.OffCenterline != null).Select(f => f.OffCenterline!.Value).DefaultIfEmpty(double.NaN).Min();
        var topSpeed = flights.Select(f => f.MaxSpeed).DefaultIfEmpty(0).Max();
        var topHeight = flights.Select(f => f.MaxHeight).DefaultIfEmpty(0).Max();
        var notes = _lessons.Where(l => string.Equals(l.Craft, name, StringComparison.OrdinalIgnoreCase)).OrderByDescending(l => l.Weight).Take(5).ToList();
        var lastThree = flights.TakeLast(3).ToList();
        var test = TestOf(name);
        var toLearn = new List<CraftItem>
        {
            new("A flight test (how it handles)", test != null, test?.Describe() ?? ""),
            new("Take off", flights.Any(f => Good(f, "takeoff"))),
            new("Land it", landed > 0, landed > 0 ? $"{landed} of {flights.Count}" : ""),
            new("A good landing (within 15 m of the centerline)", goodLandings > 0, double.IsNaN(best) ? "" : $"best {best:0} m off"),
            new("Its top speed", topSpeed >= FastSpeed * 0.9, topSpeed > 0 ? $"fastest so far {topSpeed:0} m/s" : ""),
            new("Its ceiling", topHeight >= HighHeight * 0.8, topHeight > 0 ? $"highest so far {topHeight:0} m above the ground" : ""),
            new("Notes on how it handles", notes.Count > 0, notes.Count > 0 ? $"{notes.Count} note(s)" : ""),
            new("Three flights in a row without a crash", lastThree.Count == 3 && lastThree.All(f => f.Result is not ("crashed" or "splashed"))),
            // 2.18: its endurance (from the fuel its flights used), and finals of the agents' own choosing.
            new("Its endurance (fuel)", Endurance(flights) != null, Endurance(flights) is { } e ? $"about {e:0} min on full tanks" : ""),
            new("A final of its own choosing", flights.Any(f => Good(f, "approaches")),
                string.Join(", ", flights.Where(f => f.Extras?.Style != null && Good(f, "approaches")).Select(f => f.Extras!.Style!).Distinct().Take(4))),
        };
        return new CraftProfile(name, flights.Count, landed, crashed, goodLandings, double.IsNaN(best) ? null : best, topSpeed, topHeight,
            flights.Select(f => f.CallSign).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), toLearn, notes, test);
    }

    /// <summary>Minutes on full tanks, from the fuel its flights used (flights of 3 minutes or more), or null.</summary>
    private static double? Endurance(List<FlightRecord> flights)
    {
        var burns = flights.Where(f => f.Extras?.FuelUsed is > 0.01 && f.Minutes >= 3).Select(f => f.Extras!.FuelUsed!.Value / f.Minutes).ToList();
        return burns.Count == 0 ? null : 1 / burns.Average();
    }

    private readonly List<Place> _places = [];
    private long _nextPlace = 1;

    public IReadOnlyList<Place> Places => _places;

    /// <summary>
    /// An explored place (2.18): a new one, or a visit to one already known within <see cref="SamePlace"/> (its count
    /// goes up). Returns it and whether it was new.
    /// </summary>
    public (Place Place, bool New) AddPlace(string callSign, double latitude, double longitude, double ground, string biome, DateTime now)
    {
        // Kerbin's radius is close enough for "the same place".
        const double radius = 600_000;
        var near = _places.FirstOrDefault(p =>
        {
            var north = (p.Latitude - latitude) * Math.PI / 180 * radius;
            var east = (p.Longitude - longitude) * Math.PI / 180 * radius * Math.Cos(latitude * Math.PI / 180);
            return Math.Sqrt(north * north + east * east) < SamePlace;
        });
        if (near != null)
        {
            var again = near with { Visits = near.Visits + 1 };
            _places[_places.IndexOf(near)] = again;
            return (again, false);
        }
        var place = new Place(_nextPlace++, now, callSign, latitude, longitude, ground, biome);
        _places.Add(place);
        if (_places.Count > MaxPlaces)
            _places.RemoveRange(0, _places.Count - MaxPlaces);
        return (place, true);
    }

    public bool Delete(long lessonId) => _lessons.RemoveAll(l => l.Id == lessonId) > 0;

    /// <summary>Drops lessons nobody confirmed for <see cref="Expiry"/>.</summary>
    public void Expire(DateTime now) => _lessons.RemoveAll(l => l.Confirmations == 0 && now - l.Confirmed > Expiry);

    /// <summary>A skill's best lessons, for an agent to read before it tries it.</summary>
    public IReadOnlyList<Lesson> For(string skill, int max = 5) =>
        _lessons.Where(l => l.Skill == skill).OrderByDescending(l => l.Weight).ThenByDescending(l => l.Confirmed).Take(max).ToList();

    private sealed record Saved(int Version, long NextFlight, long NextLesson, List<FlightRecord> Flights, List<Lesson> Lessons,
        List<CraftTest>? Tests = null, List<Place>? Places = null);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public const long MaxFileBytes = 20L * 1024 * 1024;

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Saved(1, _nextFlight, _nextLesson, _flights, _lessons, _tests, _places), Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The saved knowledge, or an empty one when there's no file (or it can't be read: it's kept as .bad).</summary>
    public static Knowledge Load(string path)
    {
        var knowledge = new Knowledge();
        if (!File.Exists(path))
            return knowledge;
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
                throw new JsonException("too big");
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path), Json) ?? throw new JsonException("empty");
            knowledge._flights.AddRange((saved.Flights ?? []).Where(f => f is { CallSign: not null, Skills: not null, Phases: not null }).TakeLast(MaxFlights));
            knowledge._lessons.AddRange((saved.Lessons ?? []).Where(l => l is { Text: not null, Skill: not null, Evidence: not null } && Tree.Any(s => s.Id == l.Skill)).Take(MaxLessons));
            knowledge._nextFlight = Math.Max(saved.NextFlight, knowledge._flights.Select(f => f.Id).DefaultIfEmpty(0).Max() + 1);
            knowledge._tests.AddRange((saved.Tests ?? []).Where(t => t is { Craft: not null, CallSign: not null }).TakeLast(MaxTests));
            knowledge._nextTest = knowledge._tests.Select(t => t.Id).DefaultIfEmpty(0).Max() + 1;
            knowledge._places.AddRange((saved.Places ?? []).Where(p => p is { CallSign: not null, Biome: not null }).TakeLast(MaxPlaces));
            knowledge._nextPlace = knowledge._places.Select(p => p.Id).DefaultIfEmpty(0).Max() + 1;
            knowledge._nextLesson = Math.Max(saved.NextLesson, knowledge._lessons.Select(l => l.Id).DefaultIfEmpty(0).Max() + 1);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or IOException)
        {
            try { File.Copy(path, path + ".bad", overwrite: true); } catch (IOException) { }
            return new Knowledge();
        }
        return knowledge;
    }
}
