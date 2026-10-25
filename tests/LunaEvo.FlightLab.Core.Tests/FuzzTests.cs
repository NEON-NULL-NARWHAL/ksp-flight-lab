// Flight Lab · tests/LunaEvo.FlightLab.Core.Tests/FuzzTests.cs
// [Test] Every parser that reads text from outside (the tower's channel, agents' console lines, session logs, the
//        settings file, GitHub's checksums and versions) fed thousands of mutated and random inputs. A parser may only
//        fail the way it documents; anything else would be a crash in the app. From Mission Control's FuzzTests.
// Used by: dotnet test
// Guide:   Meat and Potatoes/01-Architecture.md

using System.Text;
using LunaEvo.FlightLab.Core.Install;

namespace LunaEvo.FlightLab.Core.Tests;

public class FuzzTests
{
    private const int Rounds = 1500;
    private static readonly string[] Nasty = ["\0", "\r", "\n", "\t", "{", "}", "[", "]", "\"", "\\", "=", ":", ",", "(", ")", "//", "\u202e", "😀", "\uffff", "\ud800", "%", "-", "..", "\u0007"];

    /// <summary>Feeds mutations of the samples (and pure noise) to <paramref name="parse"/>; only the listed exception types may escape.</summary>
    private static void Fuzz(string[] samples, Action<string> parse, params Type[] allowed)
    {
        var random = new Random(4242); // fixed: a failure always reproduces
        for (var i = 0; i < Rounds; i++)
        {
            var input = i % 10 == 0 ? Noise(random) : Mutate(samples[random.Next(samples.Length)], random);
            try
            {
                parse(input);
            }
            catch (Exception e) when (allowed.Any(t => t.IsInstanceOfType(e)))
            {
                // Documented failure for bad input.
            }
            catch (Exception e)
            {
                Assert.Fail($"{e.GetType().Name} for input #{i} ({input.Length} chars): {e.Message}\n---\n{(input.Length > 300 ? input[..300] + "..." : input)}");
            }
        }
    }

    private static string Mutate(string sample, Random random)
    {
        var text = new StringBuilder(sample);
        var edits = random.Next(1, 8);
        for (var e = 0; e < edits && text.Length > 0; e++)
        {
            var at = random.Next(text.Length);
            switch (random.Next(7))
            {
                case 0: text.Remove(at, Math.Min(random.Next(1, 20), text.Length - at)); break;
                case 1: text.Insert(at, Nasty[random.Next(Nasty.Length)]); break;
                case 2: text[at] = (char)random.Next(0, 0x2FF); break;
                case 3: text.Length = at; break; // truncated
                case 4: text.Insert(at, text.ToString(at, Math.Min(50, text.Length - at)), random.Next(1, 40)); break; // repeated
                case 5: text.Insert(at, new string('[', random.Next(1, 300))); break; // deep nesting
                default: text.Insert(at, random.Next().ToString()); break;
            }
        }
        return text.ToString();
    }

    private static string Noise(Random random)
    {
        var chars = new char[random.Next(0, 400)];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = random.Next(4) == 0 ? Nasty[random.Next(Nasty.Length)][0] : (char)random.Next(1, 0x7F);
        return new string(chars);
    }

    [Fact]
    public void Tower_channel_requests()
    {
        // Whatever an agent sends, the tower answers with a status code, never an exception.
        using var server = new Tower.TowerServer(0, () => new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc));
        Fuzz(["""{"callSign":"Wingman","latitude":-0.05,"longitude":-74.7,"altitude":900,"heading":90,"speed":120,"situation":"Flying","phase":"x"}""",
              """{"callSign":"Wingman","kind":"request-approach","text":"land at KSC please"}""",
              """{"callSign":"Wingman","kind":"departure","text":"ready for departure"}""",
              """{"callSign":"Wingman","craft":"Aeris 3A","result":"landed","approach":"RNAV 09 SOUTH","goArounds":1,"minutes":8.3,"landing":{"downRunway":1329,"offCenterline":8,"headingError":4},"phases":["TakingOff","Climbing","OnRoute","OnFinal","Stopped"]}""",
              """{"callSign":"Wingman","skill":"land-rnav","text":"Slow to 85 m/s before F09.","evidence":[1,2,3]}""",
              """{"callSign":"Wingman","fixes":["KSC-DS","KSC-S1","KSC-S2","KSC-S3","F09"]}""",
              """{"callSign":"Wingman","craft":"Gull","pitchRate":30,"rollRate":90,"turnRate":2.1,"minSpeed":62,"deceleration":1.2,"acceleration":3,"cruiseThrottle":0.3,"finalMin":70,"finalMax":95,"touchdown":60,"maxSlope":4.5}""",
              """{"callSign":"Wingman","craft":"Gull","result":"landed","finals":[{"weaves":3,"high":40,"slope":4.5}]}""",
              """{"callSign":"Wingman","latitude":0.12,"longitude":-74.5,"ground":420,"biome":"Highlands"}""",
              """{"callSign":"Wingman","craft":"Gull","result":"landed","minutes":24,"style":"steep 6°","fuelUsed":0.4,"fuelLeft":0.55,"explored":3}"""],
            text =>
            {
                server.Answer("POST", "/v1/report", text);
                server.Answer("POST", "/v1/say", text);
                server.Answer("POST", "/v1/flight", text);
                server.Answer("POST", "/v1/lesson", text);
                server.Answer("POST", "/v1/knowledge", text);
                server.Answer("POST", "/v1/plan", text);
                server.Answer("POST", "/v1/procedures", text);
                server.Answer("POST", "/v1/test", text);
                server.Answer("POST", "/v1/craft", text);
                server.Answer("POST", "/v1/place", text);
            });
    }

    [Fact]
    public void Wingman_agent_lines() => Fuzz(
        ["""{"type":"event","time":"2026-10-23T15:00:00Z","kind":"phase","text":"Parked → TakingOff","data":{"from":"Parked","to":"TakingOff"}}""",
         """{"type":"event","kind":"flight","text":"Flight landed.","data":{"result":"landed","landing":{"downRunway":1105}}}""",
         "Unhandled exception. System.IO.IOException: boom"],
        text => Wingman.AgentEvent.Parse(text, DateTime.UtcNow));

    [Fact]
    public void Session_log_lines() => Fuzz(
        ["""{"time":"2026-10-24T15:00:00Z","kind":"flight","text":"Flight landed: 1 m off the centerline."}""",
         """{"time":"2026-10-24T15:01:00Z","kind":"free","text":"Explored: Highlands, 420 m"}""",
         """{"time":"x","kind":7}"""],
        text => SessionLog.Parse(text, "Alpha"));

    [Fact]
    public void Settings_files()
    {
        var path = Path.Combine(TestFolders.Temp("settings"), "settings.json");
        Fuzz(["""{"serverFolder":"D:\\server","kspFolder":null,"pilotModel":"gemma4:e4b","towerPort":8920,"towerOnStart":true,"theme":"Light"}"""],
            text =>
            {
                File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text)); // as a file has it (a lone surrogate becomes U+FFFD)
                LabSettingsStore.Load(path);
            });
    }

    [Fact]
    public void GitHub_digests_and_versions() => Fuzz(
        ["sha256:" + new string('a', 64), "v0.12.0", "0.12.0-beta", "KspWingman-0.12.0.zip"],
        text =>
        {
            GitHubReleases.Digest(text);
            GitHubReleases.ParseVersion(text);
            VerifiedDownload.SafeFileName(text);
        });
}
