// Flight Lab · tests/LunaEvo.FlightLab.Core.Tests/KnowledgeTests.cs
// [Test] The agents' knowledge: skills graded from flights, lessons that must cite them (and merge when they say the
//        same), unlocking the tree, saving and loading, and the tower's knowledge endpoints. Also the short visual.
// Used by: dotnet test
// Guide:   Meat and Potatoes/03-Knowledge.md

using System.Text.Json;
using LunaEvo.FlightLab.Core.Tower;

namespace LunaEvo.FlightLab.Core.Tests;

public class KnowledgeTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string[] RnavLanding = ["Parked", "TakingOff", "Climbing", "OnRoute", "OnFinal", "Flaring", "RollingOut", "Stopped"];

    [Fact]
    public void Grades_each_skill_a_flight_used()
    {
        var good = Knowledge.Grade("landed", "RNAV 09 SOUTH", 0, 8, RnavLanding);
        Assert.Equal(new Dictionary<string, bool> { ["takeoff"] = true, ["route"] = true, ["land-rnav"] = true }, good);

        // Off the centerline or a go-around: the landing wasn't a good one (the takeoff and the route still were).
        Assert.False(Knowledge.Grade("landed", "RNAV 09 SOUTH", 0, 22, RnavLanding)["land-rnav"]);
        Assert.False(Knowledge.Grade("landed", "RNAV 09 SOUTH", 1, 3, RnavLanding)["land-rnav"]);

        // Held, then cleared: hold and sequencing; a visual landing is its own skill.
        var held = Knowledge.Grade("landed", "RNAV 09 SOUTH", 0, 5, ["TakingOff", "Climbing", "Holding", "OnRoute", "OnFinal", "Stopped"]);
        Assert.True(held["hold"] && held["sequencing"]);
        Assert.True(Knowledge.Grade("landed", "VISUAL 09", 0, 5, ["TakingOff", "Climbing", "OnRoute", "OnFinal", "Stopped"])["land-visual"]);
        // Its own plan (the rig's): ending on F09 it's an RNAV landing, on F09V a visual one.
        Assert.True(Knowledge.Grade("landed", "FILED KSC-OH KSC-N1 KSC-N2 KSC-N3 F09", 0, 4, ["TakingOff", "OnRoute", "OnFinal", "Stopped"])["land-rnav"]);
        Assert.True(Knowledge.Grade("landed", "FILED KSC-OH KSC-VN1 KSC-VN2 KSC-VN3 F09V", 0, 4, ["TakingOff", "OnRoute", "OnFinal", "Stopped"])["land-visual"]);

        // A crash on final: the landing failed, the takeoff didn't.
        var crash = Knowledge.Grade("crashed", "RNAV 09 NORTH", 0, null, ["TakingOff", "Climbing", "OnRoute", "OnFinal", "Crashed"]);
        Assert.False(crash["land-rnav"]);
        Assert.True(crash["takeoff"]);
        Assert.Empty(Knowledge.Grade("ended in the air", null, 0, null, ["Cruising", "NotFlying"])); // joined in the air: nothing
    }

    [Fact]
    public void Training_drills_and_new_planes_are_skills_too()
    {
        string[] visual = ["TakingOff", "Climbing", "Cruising", "OnRoute", "OnFinal", "Stopped"];
        var fast = Knowledge.Grade("landed", "VISUAL 09", 0, 30, visual, maxSpeed: 226);
        Assert.True(fast["fly-fast"]);
        Assert.False(fast.ContainsKey("fly-high"));
        Assert.False(Knowledge.Grade("crashed", null, 0, null, ["TakingOff", "Climbing", "Crashed"], maxHeight: 6100)["fly-high"]); // didn't get back down
        Assert.False(Knowledge.Grade("landed", "VISUAL 09", 0, 5, visual, maxHeight: 4300)["fly-high"]); // tried, not high enough

        var k = new Knowledge();
        Assert.False(k.AddFlight("Alpha", "Aeris 3A", "landed", "VISUAL 09", 0, 5, 1000, 5, 1, visual, T0).Skills.ContainsKey("new-craft")); // its first plane
        Assert.True(k.AddFlight("Alpha", "Stearwing A300", "landed", "VISUAL 09", 0, 5, 1000, 8, 1, visual, T0).Skills["new-craft"]);
        Assert.False(k.AddFlight("Alpha", "Stearwing A300", "landed", "VISUAL 09", 0, 5, 1000, 8, 1, visual, T0).Skills.ContainsKey("new-craft")); // not new now
        Assert.Equal(12, Knowledge.Tree.Count);
    }

    [Fact]
    public void A_chosen_final_exploring_and_long_flights_are_graded_and_places_are_mapped()
    {
        string[] visual = ["TakingOff", "Climbing", "OnRoute", "OnFinal", "Stopped"];
        var k = new Knowledge();
        // A steep final of its own choosing, landed 5 m off: approaches good. The same without a style: not graded.
        var steep = k.AddFlight("Alpha", "Aeris 4A", "landed", "VISUAL 09", 0, 6, 1000, 5, 1, visual, T0, extras: new FlightExtras("steep 6°", 0.2, 0.7));
        Assert.True(steep.Skills["approaches"]);
        Assert.False(k.AddFlight("Alpha", "Aeris 4A", "landed", "VISUAL 09", 0, 6, 1000, 5, 1, visual, T0).Skills.ContainsKey("approaches"));
        // Three places and back: explore good; two: not yet. 24 minutes, landing with 55% fuel: a long flight.
        var trip = k.AddFlight("Alpha", "Aeris 4A", "landed", null, 0, 24, 1000, 20, 1, visual, T0, extras: new FlightExtras(null, 0.4, 0.55, 3));
        Assert.True(trip.Skills["explore"] && trip.Skills["long-flight"]);
        Assert.False(k.AddFlight("Alpha", "Aeris 4A", "landed", null, 0, 25, 1000, 20, 1, visual, T0, extras: new FlightExtras(null, 0.85, 0.1, 2)).Skills["long-flight"]);
        // The plane's endurance from the fuel its flights used: 0.2 in 6 min, 0.4 in 24, 0.85 in 25 → about 45 min on full tanks.
        Assert.Contains(k.Crafts().Single().ToLearn, i => i.What.StartsWith("Its endurance") && i.Done && i.Note.StartsWith("about "));
        Assert.Contains(k.Crafts().Single().ToLearn, i => i.What == "A final of its own choosing" && i.Done && i.Note == "steep 6°");

        // Places: a new one, then one within 1 km is a second visit.
        var (first, isNew) = k.AddPlace("Alpha", 0.1, -74.5, 420, "Highlands", T0);
        Assert.True(isNew);
        var (again, second) = k.AddPlace("Bravo", 0.105, -74.5, 430, "Highlands", T0);
        Assert.False(second);
        Assert.Equal((first.Id, 2), (again.Id, again.Visits));
        Assert.True(k.AddPlace("Bravo", 0.3, -74.5, 900, "Mountains", T0).New);

        var path = Path.Combine(Path.GetTempPath(), "FlightLabTests", "knowledge-" + Guid.NewGuid().ToString("N"), "knowledge.json");
        k.Save(path);
        var back = Knowledge.Load(path);
        Assert.Equal(2, back.Places.Count);
        Assert.Equal("steep 6°", back.Flights[0].Extras!.Style);

        // Through the tower: a place, a flight with extras, a test with its limits; the knowledge answer maps them.
        using var server = new TowerServer(0, () => T0);
        Assert.Equal(400, server.Answer("POST", "/v1/place", """{"callSign":"Alpha","latitude":95,"longitude":0}""").Status);
        Assert.Equal(200, server.Answer("POST", "/v1/place", """{"callSign":"Alpha","latitude":0.3,"longitude":-74.5,"ground":900,"biome":"Mountains<script>"}""").Status);
        Assert.Equal("Mountainsscript", server.Knowledge.Places.Single().Biome); // only letters, digits, spaces, dashes
        Assert.Equal(200, server.Answer("POST", "/v1/test", """{"callSign":"Alpha","craft":"Gull","pitchRate":30,"rollRate":90,"turnRate":2.1,"minSpeed":62,"deceleration":1.2,"acceleration":3,"cruiseThrottle":0.3,"finalMin":84,"finalMax":119,"touchdown":84,"maxSlope":5}""").Status);
        Assert.Contains("final approach 84-119 m/s, touchdown 84 m/s, glide slope up to 5°", server.Knowledge.TestOf("Gull")!.Describe());
        var known = JsonDocument.Parse(server.Answer("POST", "/v1/knowledge", """{"callSign":"Alpha"}""").Body).RootElement;
        Assert.Equal(1, known.GetProperty("places").GetProperty("count").GetInt32());
        Assert.Equal("Mountainsscript", known.GetProperty("places").GetProperty("highest").GetProperty("biome").GetString());
    }

    [Fact]
    public void Each_plane_gets_a_profile_and_a_list_of_what_to_learn_about_it()
    {
        string[] visual = ["TakingOff", "Climbing", "OnRoute", "OnFinal", "Stopped"];
        var k = new Knowledge();
        k.AddFlight("Alpha", "Aeris 3A", "landed", "VISUAL 09", 0, 5, 1000, 6, 1, visual, T0, maxSpeed: 150, maxHeight: 900);
        var splash = k.AddFlight("Alpha", "Gull", "splashed", null, 0, 0.8, null, null, null, ["TakingOff", "Climbing", "Splashed"], T0);
        var gull = k.AddFlight("Bravo", "Gull", "landed", "VISUAL 09", 0, 6, 1100, 25, 3, visual, T0, maxSpeed: 230);

        Assert.Contains("one plane", k.AddLesson("Alpha", "takeoff", "The Gull rolls left on takeoff: hold the wings level.", [splash.Id, 1], T0, aboutCraft: true).Problem);
        var (note, _) = k.AddLesson("Bravo", "land-visual", "The Gull floats in the flare: cut the throttle earlier.", [gull.Id], T0, aboutCraft: true);
        Assert.Equal("Gull", note!.Craft);

        var profile = k.Crafts().Single(c => c.Name == "Gull");
        Assert.Equal((2, 1, 1, 0), (profile.Flights, profile.Landed, profile.Crashed, profile.GoodLandings));
        Assert.Equal(230, profile.TopSpeed);
        Assert.Equal(["Alpha", "Bravo"], profile.FlownBy);
        Assert.Contains(profile.ToLearn, i => i.What.StartsWith("A good landing") && !i.Done && i.Note == "best 25 m off");
        Assert.Contains(profile.ToLearn, i => i.What == "Its top speed" && i.Done);
        Assert.Contains(profile.ToLearn, i => i.What == "Notes on how it handles" && i.Done);
        Assert.Equal("The Gull floats in the flare: cut the throttle earlier.", Assert.Single(profile.Notes).Text);
        Assert.Empty(k.Crafts().Single(c => c.Name == "Aeris 3A").Notes); // a plane's notes stay with that plane
    }

    [Fact]
    public void Lessons_need_evidence_and_merge_when_they_say_the_same()
    {
        var k = new Knowledge();
        var flight = k.AddFlight("Alpha", "Aeris 3A", "landed", "RNAV 09 SOUTH", 1, 9, 1300, 20, 4, RnavLanding, T0);
        var bravo = k.AddFlight("Bravo", "Aeris 3A", "landed", "RNAV 09 SOUTH", 0, 9, 1100, 5, 2, RnavLanding, T0);

        Assert.Contains("must cite", k.AddLesson("Alpha", "land-rnav", "Slow to 85 m/s before F09 to avoid a go-around.", [], T0).Problem);
        Assert.Contains("must cite", k.AddLesson("Alpha", "land-rnav", "Slow to 85 m/s before F09 to avoid a go-around.", [bravo.Id], T0).Problem); // not Alpha's
        Assert.Contains("must cite", k.AddLesson("Alpha", "hold", "Hold tighter turns at KSC-HS please.", [flight.Id], T0).Problem); // didn't hold
        Assert.Contains("no skill", k.AddLesson("Alpha", "warp", "Warp is fun but not allowed.", [flight.Id], T0).Problem);
        Assert.Contains("10 to", k.AddLesson("Alpha", "land-rnav", "short", [flight.Id], T0).Problem);

        var (lesson, _) = k.AddLesson("Alpha", "land-rnav", "Slow to 85 m/s before F09 to avoid a go-around.", [flight.Id], T0);
        Assert.NotNull(lesson);
        // Bravo says much the same from its own flight: confirmed, not a second lesson.
        var (same, _) = k.AddLesson("Bravo", "land-rnav", "Slow to 85 m/s before F09 to avoid a go-around next time.", [bravo.Id], T0.AddHours(1));
        Assert.Equal(lesson!.Id, same!.Id);
        Assert.Equal(1, same.Confirmations);
        Assert.Equal([flight.Id, bravo.Id], same.Evidence);
        Assert.Single(k.Lessons);

        // Unconfirmed lessons expire; confirmed ones stay.
        var (other, _) = k.AddLesson("Alpha", "takeoff", "Rotate at 60 m/s with the Aeris 3A on the runway.", [flight.Id], T0);
        k.Expire(T0 + Knowledge.Expiry + TimeSpan.FromHours(2));
        Assert.DoesNotContain(k.Lessons, l => l.Id == other!.Id);
        Assert.Contains(k.Lessons, l => l.Id == lesson.Id);
    }

    [Fact]
    public void Skills_unlock_from_good_flights_by_any_agent_and_survive_a_restart()
    {
        var k = new Knowledge();
        Assert.True(k.Unlocked("takeoff"));
        Assert.False(k.Unlocked("land-rnav")); // needs route and land-visual first
        for (var i = 0; i < Knowledge.UnlockAfter; i++)
        {
            k.AddFlight(i % 2 == 0 ? "Alpha" : "Bravo", "Aeris 3A", "landed", "RNAV 09 SOUTH", 0, 9, 1200, 5, 2, RnavLanding, T0);
            k.AddFlight("Alpha", "Aeris 3A", "landed", "VISUAL 09", 0, 5, 1200, 5, 2, ["TakingOff", "Climbing", "OnRoute", "OnFinal", "Stopped"], T0);
        }
        Assert.True(k.Unlocked("land-rnav"));
        Assert.Equal(new Proficiency(2, 2), k.Of("land-rnav", "Alpha"));
        Assert.Equal(new Proficiency(3, 3), k.Of("land-rnav"));
        k.AddLesson("Alpha", "land-rnav", "Slow to 85 m/s before F09 to avoid a go-around.", [1], T0);
        k.AddTest("Alpha", "Gull", 30, 90, 2.1, 62, 1.2, 3, 0.3, T0);
        k.AddFlight("Alpha", "Gull", "landed", "VISUAL 09", 0, 5, 1200, 5, 2, ["TakingOff"], T0, finals: [new FinalNumbers(3, 40)]);

        var path = Path.Combine(Path.GetTempPath(), "FlightLabTests", "knowledge-" + Guid.NewGuid().ToString("N"), "knowledge.json");
        k.Save(path);
        var back = Knowledge.Load(path);
        Assert.Equal(k.Flights.Count, back.Flights.Count);
        Assert.Equal(k.Of("land-rnav"), back.Of("land-rnav"));
        Assert.Single(back.Lessons);
        Assert.Equal(62, back.TestOf("Gull")!.MinSpeed); // flight tests and finals are kept too
        Assert.Equal([new FinalNumbers(3, 40)], back.FinalsOf("Gull"));
        Assert.Equal(2, back.AddTest("B", "Gull", 1, 1, 1, 1, 1, 1, 1, T0).Id);
        Assert.Equal(k.Flights.Count + 1, back.AddFlight("C", "", "crashed", null, 0, 1, null, null, null, [], T0).Id); // ids go on

        File.WriteAllText(path, "{ not json");
        Assert.Empty(Knowledge.Load(path).Flights); // unreadable: starts empty, the file kept as .bad
        Assert.True(File.Exists(path + ".bad"));
    }

    [Fact]
    public void The_tower_records_flights_takes_lessons_and_answers_what_an_agent_knows()
    {
        using var server = new TowerServer(0, () => T0);
        var flight = server.Answer("POST", "/v1/flight", JsonSerializer.Serialize(new
        {
            callSign = "Alpha", craft = "Aeris 3A", result = "landed", approach = "RNAV 09 SOUTH", goArounds = 0, minutes = 8.3,
            landing = new { downRunway = 1329, offCenterline = 8, headingError = 4 }, phases = RnavLanding,
        }));
        Assert.Equal(200, flight.Status);
        var id = JsonDocument.Parse(flight.Body).RootElement.GetProperty("flight").GetProperty("id").GetInt64();
        Assert.True(JsonDocument.Parse(flight.Body).RootElement.GetProperty("flight").GetProperty("skills").GetProperty("land-rnav").GetBoolean());

        Assert.Equal(422, server.Answer("POST", "/v1/lesson", """{"callSign":"Alpha","skill":"land-rnav","text":"No evidence here at all.","evidence":[]}""").Status);
        Assert.Equal(200, server.Answer("POST", "/v1/lesson", JsonSerializer.Serialize(new { callSign = "Alpha", skill = "land-rnav", text = "Brakes on below 60 m/s keep it straight.", evidence = new[] { id } })).Status);

        var known = JsonDocument.Parse(server.Answer("POST", "/v1/knowledge", """{"callSign":"Alpha"}""").Body).RootElement;
        var rnav = known.GetProperty("skills").EnumerateArray().First(s => s.GetProperty("id").GetString() == "land-rnav");
        Assert.Equal(1, rnav.GetProperty("mine").GetProperty("good").GetInt32());
        Assert.False(rnav.GetProperty("unlocked").GetBoolean());
        Assert.Equal("Brakes on below 60 m/s keep it straight.", rnav.GetProperty("lessons")[0].GetProperty("text").GetString());
        Assert.Equal(id, known.GetProperty("flights")[0].GetProperty("id").GetInt64());

        Assert.Equal(400, server.Answer("POST", "/v1/flight", """{"callSign":"Alpha"}""").Status); // no result

        // A plane's flight test and its finals (2.17): kept, checked, and answered for that plane's setup.
        Assert.Equal(400, server.Answer("POST", "/v1/test", """{"callSign":"Alpha","craft":"Gull","pitchRate":30}""").Status); // numbers missing
        Assert.Equal(400, server.Answer("POST", "/v1/test", """{"callSign":"Alpha","craft":"Gull","pitchRate":-1,"rollRate":90,"turnRate":2,"minSpeed":62,"deceleration":1,"acceleration":3,"cruiseThrottle":0.3}""").Status);
        Assert.Equal(200, server.Answer("POST", "/v1/test", """{"callSign":"Alpha","craft":"Gull","pitchRate":30,"rollRate":90,"turnRate":2.1,"minSpeed":62,"deceleration":1.2,"acceleration":3,"cruiseThrottle":0.3,"steerRate":30,"drift":-0.5,"braking":4}""").Status);
        Assert.Equal(200, server.Answer("POST", "/v1/flight", """{"callSign":"Alpha","craft":"Gull","result":"landed","phases":["TakingOff"],"finals":[{"weaves":3,"high":40},{"weaves":0,"high":-5}]}""").Status);
        var gull = JsonDocument.Parse(server.Answer("POST", "/v1/craft", """{"callSign":"Bravo","craft":"gull"}""").Body).RootElement;
        Assert.Equal(62, gull.GetProperty("test").GetProperty("minSpeed").GetDouble());
        Assert.Equal(3, gull.GetProperty("finals")[0].GetProperty("weaves").GetInt32());
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(server.Answer("POST", "/v1/craft", """{"callSign":"Bravo","craft":"Nothing"}""").Body).RootElement.GetProperty("test").ValueKind);
        var profile = server.Knowledge.Crafts().Single(c => c.Name == "Gull");
        Assert.Contains(profile.ToLearn, i => i.What.StartsWith("A flight test") && i.Done && i.Note.Contains("slowest 62 m/s") && i.Note.Contains("veers 0.5 °/s left"));
        Assert.True(server.DeleteLesson(1));
        Assert.Empty(server.Knowledge.Lessons);
    }

    private static PlaneReport At(string callSign, double along, double cross, double heading = 90) =>
        new(callSign, Ksc.At("", along, cross, 900, 0).Latitude, Ksc.At("", along, cross, 900, 0).Longitude, 900, heading, 120, "Flying");

    [Fact]
    public void Close_in_and_alone_a_plane_gets_the_short_visual_or_what_it_asked_for()
    {
        // The owner saw agents fly far out for an RNAV route: close in with nobody waiting, the short visual is quicker.
        var tower = new Tower.Tower();
        tower.Report(At("Alpha", 3000, 1500), T0);
        var visual = tower.Say("Alpha", "request-approach", "request approach, KSC runway 09", T0).Single(m => m.Kind == "clearance");
        Assert.Equal(Ksc.VisualName, visual.Route!.Name);
        Assert.Equal(["KSC-VS1", "KSC-VS2", "KSC-VS3", "F09V"], visual.Route.Fixes.Select(f => f.Name));
        Assert.All(visual.Route.Fixes, f => Assert.InRange(f.Altitude, 300, 700)); // Wingman flies fixes from 300 m (audit 15)
        Assert.True(visual.Route.Fixes[0].Latitude < Ksc.ThresholdLatitude); // its own side (south)

        // Far out: an RNAV route. Asking for one by name (alone): that one.
        var far = new Tower.Tower();
        far.Report(At("Alpha", 20_000, 2000), T0);
        Assert.Equal("RNAV 09 SOUTH", far.Say("Alpha", "request-approach", "", T0).Single(m => m.Kind == "clearance").Route!.Name);
        var asked = new Tower.Tower();
        asked.Report(At("Alpha", 3000, 1500), T0);
        Assert.Equal("RNAV 09 NORTH", asked.Say("Alpha", "request-approach", "request RNAV 09 NORTH", T0).Single(m => m.Kind == "clearance").Route!.Name);

        // Traffic: with another plane still waiting behind it, the next one gets the published route, not its wish.
        var busy = new Tower.Tower();
        busy.Report(At("Alpha", 3000, 1500), T0);
        busy.Report(At("Bravo", 3000, -2000), T0);
        busy.Report(At("Charlie", 5000, -3000), T0);
        busy.Say("Alpha", "request-approach", "visual", T0);
        busy.Say("Bravo", "request-approach", "visual", T0); // holds
        busy.Say("Charlie", "request-approach", "visual", T0); // holds behind Bravo
        busy.Report(At("Charlie", 5000, -3000), T0.AddSeconds(29));
        busy.Report(At("Alpha", 900, 90) with { Situation = "Landed", Speed = 0 }, T0.AddSeconds(30));
        var next = busy.Report(At("Bravo", 3000, -2000), T0.AddSeconds(31));
        Assert.Equal("RNAV 09 NORTH", next.Single(m => m.Kind == "clearance").Route!.Name);
    }
}
