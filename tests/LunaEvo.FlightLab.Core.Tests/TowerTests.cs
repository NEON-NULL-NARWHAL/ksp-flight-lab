// Flight Lab · tests/LunaEvo.FlightLab.Core.Tests/TowerTests.cs
// [Test] The tower: asking intentions, clearing one plane and holding the next, cleared to land, vacating, handing
//        the runway to the next plane, losing a silent plane, and the HTTP channel's answers to good and bad requests.
// Used by: dotnet test
// Guide:   Meat and Potatoes/02-Tower.md

using System.Text.Json;
using LunaEvo.FlightLab.Core.Tower;

namespace LunaEvo.FlightLab.Core.Tests;

public class TowerTests
{
    private static readonly DateTime T0 = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A report from a point in KSC's runway frame (meters along the runway from touchdown, right of the centerline).</summary>
    private static PlaneReport At(string callSign, double along, double cross, double heading = 90, string situation = "Flying", double speed = 120)
    {
        var fix = Ksc.At("", along, cross, 900, 0);
        return new PlaneReport(callSign, fix.Latitude, fix.Longitude, 900, heading, speed, situation);
    }

    [Fact]
    public void Asks_a_new_plane_its_intentions_once()
    {
        var tower = new Tower.Tower();
        Assert.Empty(tower.Report(At("Wingman", 5000, -2000, situation: "SpaceCenter"), T0.AddSeconds(-10))); // no plane yet: quiet
        var first = tower.Report(At("Wingman", 5000, -2000), T0);
        Assert.Equal("ask-intent", Assert.Single(first).Kind);
        Assert.Empty(tower.Report(At("Wingman", 5500, -2000), T0.AddSeconds(3)));
        var answer = tower.Say("Wingman", "intent", "local flight, sightseeing", T0.AddSeconds(4));
        Assert.Equal("info", Assert.Single(answer).Kind);
        Assert.Equal(PlaneState.Local, tower.Planes.Single().State);
    }

    [Fact]
    public void Clears_the_first_and_holds_the_second_then_hands_over_the_runway()
    {
        var tower = new Tower.Tower();
        tower.Report(At("Alpha", 15_000, -1000), T0); // north of the runway, too far out for the short visual
        tower.Report(At("Bravo", 3000, 2000), T0); // south of it
        var alpha = Assert.Single(tower.Say("Alpha", "request-approach", "", T0.AddSeconds(1)));
        Assert.Equal("clearance", alpha.Kind);
        Assert.Equal("RNAV 09 NORTH", alpha.Route!.Name);
        Assert.Equal(["KSC-N1", "KSC-N2", "KSC-N3", "F09"], alpha.Route.Fixes.Select(f => f.Name));

        var bravo = Assert.Single(tower.Say("Bravo", "intent", "land at KSC", T0.AddSeconds(2)));
        Assert.Equal("hold", bravo.Kind);
        Assert.Equal(2, bravo.Hold!.Number);
        Assert.Equal("KSC-OH", bravo.Hold.Fix); // over the runway
        Assert.Equal(Ksc.OverheadBase, bravo.Hold.Altitude);
        Assert.Contains("after Alpha", bravo.Text);

        // Both report every 30 s (agents report every few seconds; a silent one is lost after a minute). Alpha flies its
        // pattern, then the final: cleared to land once; down and stopped: vacate. Bravo holds all along.
        var alphaSaid = new List<string>();
        for (var t = 0.5; t <= 6; t += 0.5)
        {
            var alphaAt = t < 4 ? At("Alpha", -7000, -3000) : t < 6 ? At("Alpha", -2000, 50) : At("Alpha", 800, 5, situation: "Landed", speed: 0);
            alphaSaid.AddRange(tower.Report(alphaAt, T0.AddMinutes(t)).Select(m => m.Kind));
            Assert.Empty(tower.Report(At("Bravo", 2000, 6000), T0.AddMinutes(t)));
        }
        Assert.Equal(["cleared-to-land", "vacate"], alphaSaid);
        // Recovered (back at the Space Center): the runway is Bravo's.
        tower.Report(At("Alpha", 800, 5, situation: "", speed: 0), T0.AddMinutes(6.5));
        var cleared = Assert.Single(tower.Report(At("Bravo", 2000, 6000), T0.AddMinutes(6.6)));
        Assert.Equal("clearance", cleared.Kind);
        Assert.Equal(Ksc.VisualName, cleared.Route!.Name); // alone now, and close in: the short visual
    }

    [Fact]
    public void A_plane_that_taxied_off_the_runway_hands_it_over()
    {
        var tower = new Tower.Tower();
        tower.Report(At("Alpha", 2000, -1000), T0);
        tower.Report(At("Bravo", 3000, 2000), T0);
        tower.Say("Alpha", "request-approach", "", T0);
        tower.Say("Bravo", "request-approach", "", T0);
        tower.Report(At("Bravo", 2000, 6000), T0.AddMinutes(0.5)); // its messages so far (asked, hold)
        Assert.Equal("vacate", Assert.Single(tower.Report(At("Alpha", 900, 4, situation: "Landed", speed: 0), T0.AddMinutes(1))).Kind);
        // Taxiing off at walking pace, still landed: on the runway until it's well beside it (Wingman parks 90 m out).
        tower.Report(At("Alpha", 920, 40, situation: "Landed", speed: 5), T0.AddMinutes(1.2));
        Assert.Empty(tower.Report(At("Bravo", 2000, 6000), T0.AddMinutes(1.3)));
        tower.Report(At("Alpha", 930, 90, situation: "Landed", speed: 0), T0.AddMinutes(1.5));
        Assert.Equal("clearance", Assert.Single(tower.Report(At("Bravo", 2000, 6000), T0.AddMinutes(1.6))).Kind);
    }

    [Fact]
    public void Cleared_to_land_when_it_calls_the_runway_in_sight()
    {
        var tower = new Tower.Tower();
        tower.Report(At("Alpha", 15_000, -1000), T0);
        tower.Say("Alpha", "request-approach", "", T0);
        Assert.Empty(tower.Report(At("Alpha", -7000, 100), T0.AddMinutes(3))); // on final, no call yet: not yet
        var cleared = tower.Say("Alpha", "runway-in-sight", "runway in sight", T0.AddMinutes(3.1));
        Assert.Equal("cleared-to-land", Assert.Single(cleared).Kind);
        Assert.Empty(tower.Report(At("Alpha", -5000, 50), T0.AddMinutes(3.5))); // once

        // An agent that never calls: cleared 2.5 km out.
        var quiet = new Tower.Tower();
        quiet.Report(At("Bravo", 15_000, -1000), T0);
        quiet.Say("Bravo", "request-approach", "", T0);
        Assert.Empty(quiet.Report(At("Bravo", -6000, 50), T0.AddMinutes(3)));
        Assert.Equal("cleared-to-land", Assert.Single(quiet.Report(At("Bravo", -2000, 20), T0.AddMinutes(4))).Kind);

        // Not from the overhead hold circling near the runway: only lined up on final (the rig's early clearance).
        var hold = new Tower.Tower();
        hold.Report(At("Charlie", 15_000, -1000), T0);
        hold.Say("Charlie", "request-approach", "", T0);
        Assert.Empty(hold.Report(At("Charlie", -1100, 0, heading: 180), T0.AddMinutes(1)));
    }

    [Fact]
    public void An_agent_files_its_own_plan_from_the_table()
    {
        var table = Ksc.Table;
        Assert.Contains(table.Fixes, f => f.Name == "KSC-DS");
        Assert.Contains(("KSC-DS", "KSC-S1"), table.Legs);
        Assert.Null(Ksc.Check(["KSC-DS", "KSC-S1", "KSC-S2", "KSC-S3", "F09"], 4000, 2000).Problem);
        Assert.Contains("isn't a published leg", Ksc.Check(["KSC-S1", "F09"], 4000, 2000).Problem);
        Assert.Contains("final fix", Ksc.Check(["KSC-DS", "KSC-S1"], 4000, 2000).Problem);
        Assert.Contains("no fix", Ksc.Check(["MUN-1", "F09"], 4000, 2000).Problem);
        Assert.Contains("25 km", Ksc.Check(["KSC-W12", "F09"], 40_000, 0).Problem);

        var tower = new Tower.Tower();
        tower.Report(At("Alpha", 3000, 2500), T0);
        Assert.Contains("unable", Assert.Single(tower.File("Alpha", ["KSC-S1", "F09"], T0), m => m.Kind == "info").Text);
        var cleared = tower.File("Alpha", ["KSC-DS", "KSC-VS1", "KSC-VS2", "KSC-VS3", "F09V"], T0.AddSeconds(5)).Single(m => m.Kind == "clearance");
        Assert.StartsWith("Alpha, KSC tower, cleared as filed: KSC-DS, KSC-VS1", cleared.Text);
        Assert.Equal(5, cleared.Route!.Fixes.Count);

        // With traffic it holds, and flies its plan when its turn comes.
        tower.Report(At("Bravo", 4000, -3000), T0);
        tower.File("Bravo", ["KSC-OH", "KSC-VN1", "KSC-VN2", "KSC-VN3", "F09V"], T0.AddSeconds(6));
        Assert.Equal(PlaneState.Holding, tower.Planes.Single(p => p.CallSign == "Bravo").State);
        tower.Report(At("Alpha", 900, 90, situation: "Landed", speed: 0), T0.AddSeconds(30));
        var bravo = tower.Report(At("Bravo", 900, 0), T0.AddSeconds(31)).Single(m => m.Kind == "clearance");
        Assert.StartsWith("Bravo, KSC tower, cleared as filed: KSC-OH, KSC-VN1", bravo.Text);
    }

    [Fact]
    public void A_go_around_is_resequenced_with_traffic_or_cleared_again_for_the_short_visual()
    {
        // Alone: cleared again at once for the short visual.
        var alone = new Tower.Tower();
        alone.Report(At("Alpha", 15_000, -1000), T0);
        alone.Say("Alpha", "request-approach", "", T0);
        alone.Report(At("Alpha", -500, 40), T0.AddMinutes(4));
        var again = alone.Say("Alpha", "go-around", "going around", T0.AddMinutes(4.1)).Single(m => m.Kind == "clearance");
        Assert.StartsWith("Alpha, KSC tower, roger the go-around, cleared VISUAL 09", again.Text);
        // The same go-around said again a few seconds later is answered once (the rig's Aeris 4A repeated it every 2-4 s).
        Assert.Empty(alone.Say("Alpha", "go-around", "going around", T0.AddMinutes(4.1).AddSeconds(3)));
        Assert.Single(alone.Say("Alpha", "go-around", "going around", T0.AddMinutes(5)), m => m.Kind == "clearance"); // a new one later

        // Traffic waiting: to the back of the hold, with traffic information; the next one is cleared.
        var busy = new Tower.Tower();
        busy.Report(At("Alpha", 15_000, -1000), T0);
        busy.Report(At("Bravo", 3000, 2000), T0);
        busy.Say("Alpha", "request-approach", "", T0);
        busy.Say("Bravo", "request-approach", "", T0);
        busy.Report(At("Bravo", 900, 0), T0.AddMinutes(0.5));
        busy.Report(At("Alpha", -500, 40), T0.AddMinutes(0.5));
        var held = busy.Say("Alpha", "go-around", "going around", T0.AddMinutes(0.6)).Single(m => m.Kind == "hold");
        Assert.Contains("roger the go-around. number 2. Hold at KSC-OH", held.Text);
        Assert.Contains("Traffic: Bravo", held.Text);
        Assert.Equal(PlaneState.Approach, busy.Planes.Single(p => p.CallSign == "Bravo").State);
        Assert.Contains("Traffic: Alpha holding over the runway", busy.Report(At("Bravo", 900, 0), T0.AddMinutes(0.7)).Single(m => m.Kind == "clearance").Text);
    }

    [Fact]
    public void Departures_go_one_at_a_time_and_not_while_a_plane_is_cleared_to_land()
    {
        // Two agents launching in the same second in the rig were put on the same spot of the runway.
        var tower = new Tower.Tower();
        Assert.Equal("cleared-departure", Assert.Single(tower.Say("Alpha", "departure", "ready", T0)).Kind);
        var held = Assert.Single(tower.Say("Bravo", "departure", "ready", T0.AddSeconds(1)));
        Assert.Equal("hold-departure", held.Kind);
        Assert.Contains("Alpha is departing", held.Text);
        // Alpha still at the Space Center (launching): still its runway. Then 100 m up: Bravo's turn.
        tower.Report(At("Alpha", 0, 0, situation: "SpaceCenter"), T0.AddSeconds(5));
        Assert.Equal("hold-departure", Assert.Single(tower.Say("Bravo", "departure", "ready", T0.AddSeconds(6))).Kind);
        tower.Report(At("Alpha", 2000, 0), T0.AddSeconds(60)); // flying at 900 m
        tower.Say("Alpha", "intent", "local flight", T0.AddSeconds(61));
        Assert.Contains(tower.Say("Bravo", "departure", "ready", T0.AddSeconds(62)), m => m.Kind == "cleared-departure");

        // A plane cleared to land: no departures until it's down.
        var busy = new Tower.Tower();
        busy.Report(At("Alpha", 2000, -1000), T0);
        busy.Say("Alpha", "request-approach", "", T0);
        busy.Say("Alpha", "runway-in-sight", "runway in sight", T0.AddSeconds(30));
        busy.Report(At("Alpha", -2000, 0), T0.AddSeconds(31));
        Assert.Contains("Alpha is landing", Assert.Single(busy.Say("Bravo", "departure", "ready", T0.AddSeconds(32))).Text);
    }

    [Fact]
    public void A_fast_plane_gets_the_long_approaches_and_a_far_one_waits_its_turn()
    {
        // A plane that flies its approach at 115 m/s (the Aeris 4A, from its flight test): never the short visual.
        var tower = new Tower.Tower();
        tower.Report(At("Alpha", 2000, -1000) with { ApproachSpeed = 115 }, T0);
        var cleared = tower.Say("Alpha", "request-approach", "request VISUAL 09", T0).Single(m => m.Kind == "clearance");
        Assert.Equal("RNAV 09 NORTH", cleared.Route!.Name);
        tower.Report(At("Alpha", -500, 40) with { ApproachSpeed = 115 }, T0.AddMinutes(4));
        Assert.DoesNotContain("VISUAL", tower.Say("Alpha", "go-around", "going around", T0.AddMinutes(4.1)).Single(m => m.Kind == "clearance").Text);

        // Bravo waits 46 km out when Alpha goes around over the runway: Alpha tries again first, Bravo keeps holding.
        var busy = new Tower.Tower();
        busy.Report(At("Alpha", 2000, -1000), T0);
        busy.Report(At("Bravo", 2000, 46_000), T0);
        busy.Say("Alpha", "request-approach", "", T0);
        Assert.Equal("hold", busy.Say("Bravo", "request-approach", "", T0).Single().Kind);
        busy.Report(At("Alpha", -500, 40), T0.AddSeconds(40));
        busy.Report(At("Bravo", 2000, 46_000), T0.AddSeconds(40));
        var again = busy.Say("Alpha", "go-around", "going around", T0.AddSeconds(45)).Single(m => m.Kind == "clearance");
        Assert.Contains("roger the go-around, cleared", again.Text);
        Assert.Equal(PlaneState.Holding, busy.Planes.Single(p => p.CallSign == "Bravo").State);
    }

    [Theory]
    [InlineData("SpaceCenter")] // crashed and recovered: the owner saw the tower hold the only plane flying for it
    [InlineData("Destroyed")]
    [InlineData("Splashed")]
    [InlineData("Offline")] // its player left the LMP server
    public void A_plane_out_of_the_pattern_leaves_the_sequence_at_once(string situation)
    {
        var tower = new Tower.Tower();
        tower.Report(At("Alpha", 2000, -1000), T0);
        tower.Report(At("Bravo", 3000, 2000), T0);
        tower.Say("Alpha", "request-approach", "", T0);
        Assert.Equal("hold", tower.Say("Bravo", "request-approach", "", T0).Single().Kind);
        tower.Report(At("Alpha", 0, 0, situation: situation, speed: 0), T0.AddSeconds(5));
        var cleared = Assert.Single(tower.Report(At("Bravo", 2000, 6000), T0.AddSeconds(6)));
        Assert.Equal("clearance", cleared.Kind);
        Assert.DoesNotContain("Traffic", cleared.Text); // nobody else around now
        Assert.Contains(tower.Channel, m => m.Text == $"Alpha {(situation switch { "SpaceCenter" => "back at the Space Center", "Destroyed" => "crashed", "Splashed" => "down in the sea", _ => "left the server" })}: out of the sequence.");
        // Not asked its intentions while it has no plane in the air; asked again once it's flying.
        Assert.Empty(tower.Report(At("Alpha", 0, 0, situation: situation, speed: 0), T0.AddSeconds(10)));
        Assert.Equal("ask-intent", Assert.Single(tower.Report(At("Alpha", 2000, -1000), T0.AddSeconds(60))).Kind);
    }

    [Fact]
    public void An_agent_that_stops_leaves_the_sequence_and_the_ones_behind_move_up()
    {
        var tower = new Tower.Tower();
        foreach (var cs in new[] { "Alpha", "Bravo", "Charlie" })
            tower.Report(At(cs, 2000, -1000), T0);
        tower.Say("Alpha", "request-approach", "", T0);
        Assert.Equal(2, tower.Say("Bravo", "request-approach", "", T0).Single().Hold!.Number);
        Assert.Equal(3, tower.Say("Charlie", "request-approach", "", T0).Single().Hold!.Number);
        tower.Say("Bravo", "leaving", "leaving the channel", T0.AddSeconds(5)); // number 2 stopped: Charlie is next
        Assert.Equal(2, tower.Planes.Single(p => p.CallSign == "Charlie").Number);
        tower.Say("Alpha", "leaving", "leaving the channel", T0.AddSeconds(6)); // and the one on approach: Charlie is cleared
        Assert.Equal(PlaneState.Approach, tower.Planes.Single(p => p.CallSign == "Charlie").State);
        Assert.Equal("clearance", Assert.Single(tower.Report(At("Charlie", 2000, 6000), T0.AddSeconds(7))).Kind);
    }

    [Fact]
    public void A_silent_plane_gives_up_its_place()
    {
        var tower = new Tower.Tower();
        tower.Report(At("Alpha", 2000, -1000), T0);
        tower.Report(At("Bravo", 3000, 2000), T0);
        tower.Say("Alpha", "request-approach", "", T0);
        Assert.Equal("hold", tower.Say("Bravo", "request-approach", "", T0).Single().Kind);
        // Alpha crashed (its agent stopped reporting): after the timeout Bravo is cleared.
        Assert.Empty(tower.Report(At("Bravo", 2000, 6000), T0.AddSeconds(30)));
        Assert.Equal("clearance", Assert.Single(tower.Report(At("Bravo", 2000, 6000), T0 + Tower.Tower.Timeout + TimeSpan.FromSeconds(1))).Kind);
    }

    [Fact]
    public void Ksc_frame_and_fixes_agree()
    {
        var fix = Ksc.At("x", -8000, 0, 628, 90);
        var (along, cross) = Ksc.Frame(fix.Latitude, fix.Longitude);
        Assert.InRange(along, -8002, -7998);
        Assert.InRange(cross, -2, 2);
        Assert.Equal("DIRECT F09", Ksc.Pick(-12_000, 500, 85).Name); // west and facing it: direct
        Assert.Equal("DIRECT F09V", Ksc.Pick(-9_000, -1500, 120).Name);
        Assert.Null(Ksc.Direct(-9_000, -6000, 0)); // more than 45° to join: a route
        Assert.Null(Ksc.Direct(-12_000, 0, 180)); // flying away
        Assert.Equal("RNAV 09 NORTH", Ksc.Pick(1000, -800, 90).Name);
    }

    [Fact]
    public void Browsers_and_other_hosts_are_refused_and_floods_are_capped()
    {
        // Audit 13: T1 (a web page posting to localhost, DNS rebinding), T2 (planes and the channel can't grow without end).
        Assert.Null(TowerServer.Refuse(null, "localhost:8920", "application/json; charset=utf-8", 8920));
        Assert.Null(TowerServer.Refuse(null, "127.0.0.1:8920", "application/json", 8920));
        Assert.NotNull(TowerServer.Refuse("https://evil.example", "localhost:8920", "application/json", 8920));
        Assert.NotNull(TowerServer.Refuse(null, "evil.example:8920", "application/json", 8920));
        Assert.NotNull(TowerServer.Refuse(null, "localhost:9999", "application/json", 8920));
        Assert.NotNull(TowerServer.Refuse(null, "localhost:8920", "text/plain", 8920));
        Assert.NotNull(TowerServer.Refuse(null, null, null, 8920));

        using var server = new TowerServer(0, () => T0);
        string Say(string callSign, string text) => JsonSerializer.Serialize(new { callSign, kind = "intent", text });
        for (var i = 0; i < Tower.Tower.MaxPlanes; i++)
            Assert.Equal(200, server.Answer("POST", "/v1/say", Say($"p{i}", "local")).Status);
        Assert.Equal(429, server.Answer("POST", "/v1/say", Say("one too many", "local")).Status);
        Assert.Equal(200, server.Answer("POST", "/v1/say", Say("p0", "still me")).Status);
        for (var i = 0; i < Tower.Tower.MaxChannel; i++)
            server.Answer("POST", "/v1/say", Say("p1", "spam"));
        Assert.True(server.Tower.Channel.Count <= Tower.Tower.MaxChannel);
    }

    [Fact]
    public void The_channel_is_kept_as_data_one_json_line_per_message()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FlightLabTests", "tower-" + Guid.NewGuid().ToString("N"));
        var log = Path.Combine(folder, "tower.jsonl");
        try
        {
            using var server = new TowerServer(0, () => T0) { LogPath = log };
            server.Answer("POST", "/v1/report", """{"callSign":"Alpha","latitude":-0.05,"longitude":-74.7,"situation":"Flying"}""");
            server.Answer("POST", "/v1/say", """{"callSign":"Alpha","kind":"request-approach","text":""}""");
            var lines = File.ReadAllLines(log);
            Assert.Equal(["ask-intent", "request-approach", "clearance"], lines.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("kind").GetString()));
            Assert.Equal(Ksc.VisualName, JsonDocument.Parse(lines[2]).RootElement.GetProperty("route").GetProperty("name").GetString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_channel_answers_good_requests_and_refuses_bad_ones()
    {
        using var server = new TowerServer(0, () => T0);
        var (status, body) = server.Answer("POST", "/v1/report", """{"callSign":"Wingman","latitude":-0.05,"longitude":-74.7,"altitude":900,"heading":90,"speed":120,"situation":"Flying"}""");
        Assert.Equal(200, status);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("ask-intent", doc.RootElement.GetProperty("messages")[0].GetProperty("kind").GetString());

        Assert.Equal(405, server.Answer("GET", "/v1/report", "{}").Status);
        Assert.Equal(400, server.Answer("POST", "/v1/report", "not json").Status);
        Assert.Equal(400, server.Answer("POST", "/v1/report", """{"latitude":1,"longitude":2}""").Status); // no call sign
        Assert.Equal(400, server.Answer("POST", "/v1/report", """{"callSign":"x","latitude":"north"}""").Status);
        Assert.Equal(400, server.Answer("POST", "/v1/say", """{"callSign":"x"}""").Status); // no kind
        Assert.Equal(404, server.Answer("POST", "/v1/launch", """{"callSign":"x"}""").Status);
        Assert.Equal(413, server.Answer("POST", "/v1/say", null).Status);
        // Long or odd text is cut and cleaned, not refused.
        var (ok, _) = server.Answer("POST", "/v1/say", JsonSerializer.Serialize(new { callSign = new string('W', 99), kind = "intent", text = "land\u0007" + new string('x', 900) }));
        Assert.Equal(200, ok);
        Assert.All(server.Tower.Channel, m => Assert.True(m.Text.Length <= 300 && m.From.Length <= 40 && !m.Text.Contains('\u0007')));
    }
}
