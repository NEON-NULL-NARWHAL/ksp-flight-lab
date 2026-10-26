// KSP Flight Lab · src/KspFlightLab/PlanesPage.cs
// [UI] Each plane's knowledge card: its flight and ground tests, its final approach limits, its records and
//      endurance, what's still to learn about it, the agents' notes on how it handles, and its last finals.
// Used by: MainWindow (rail: Knowledge → Planes)
// Guide:   Meat and Potatoes/03-Knowledge.md

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using KspFlightLab.Core.Tower;

namespace KspFlightLab;

internal sealed class PlanesPage
{
    private readonly MainWindow _host;
    private readonly StackPanel _cards = new() { Spacing = 4 };

    public PlanesPage(MainWindow host)
    {
        _host = host;
        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Text("Planes", "headline"),
                Ui.Text("What the agents know about every plane they flew: measured in its own tests (KSP Wingman's test_plane, on " +
                        "the ground and in the air), worked out into its limits, and corrected by how its landings went.", "sublabel"),
            },
        };
        View = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(28, 20, 28, 28), Spacing = 16, Children = { header, _cards } } };
    }

    public Control View { get; }

    public void Refresh()
    {
        _cards.Children.Clear();
        _host.ReadKnowledge(k =>
        {
            var crafts = k.Crafts();
            if (crafts.Count == 0)
                _cards.Children.Add(Ui.Text("No planes yet: they appear after their first flight (training on new planes tests each one).", "sublabel"));
            foreach (var c in crafts)
                _cards.Children.Add(Card(c, k.FinalsOf(c.Name)));
            return 0;
        });
    }

    private static Control Card(CraftProfile c, IReadOnlyList<FinalNumbers> finals)
    {
        var id = $"planes/{c.Name}";
        var (view, body, _) = Ui.Group("✈ " + c.Name);
        body.Children.Add(Ui.Row("Flights", string.Create(CultureInfo.InvariantCulture,
            $"{c.Flights} by {string.Join(", ", c.FlownBy)} · {c.Landed} landed, {c.Crashed} crashed, {c.GoodLandings} good landing(s)") +
            (c.BestOffCenterline is { } best ? string.Create(CultureInfo.InvariantCulture, $" · best {best:0} m off the centerline") : "") +
            string.Create(CultureInfo.InvariantCulture, $" · fastest {c.TopSpeed:0} m/s, highest {c.TopHeight:0} m above the ground"), null).WithId(id + "/flights"));
        if (c.Test is { } t)
        {
            body.Children.Add(Ui.Row("Flight test", string.Create(CultureInfo.InvariantCulture,
                $"pitch {t.PitchRate:0} °/s and roll {t.RollRate:0} °/s per full stick at 150 m/s · turns {t.TurnRate:0.0} °/s at 30° of bank · " +
                $"slowest {t.MinSpeed:0} m/s · slows {t.Deceleration:0.0} m/s² at idle · speeds up {t.Acceleration:0.0} m/s² at full throttle · " +
                $"{t.CruiseThrottle * 100:0}% throttle at 150 m/s · by {t.CallSign}, {t.Time.ToLocalTime():yyyy-MM-dd}"), null).WithId(id + "/test"));
            if (t.SteerRate > 0)
                body.Children.Add(Ui.Row("Ground test", string.Create(CultureInfo.InvariantCulture,
                    $"nose wheel {t.SteerRate:0.0} °/s per full lock at 10 m/s · veers {Math.Abs(t.Drift):0.0} °/s {(t.Drift >= 0 ? "right" : "left")} with it straight · brakes {t.Braking:0.0} m/s²"),
                    null).WithId(id + "/ground"));
            if (t.FinalMax > 0)
                body.Children.Add(Ui.Row("Final approach limits", string.Create(CultureInfo.InvariantCulture,
                    $"final speeds {t.FinalMin:0}-{t.FinalMax:0} m/s · touchdown {t.Touchdown:0} m/s · glide slope 3° up to {t.MaxSlope:0.#}° ") +
                    "(an agent may choose a final within these; a steep one that arrives high lowers the slope)", null).WithId(id + "/limits"));
        }
        else
            body.Children.Add(Ui.Row("Tests", "Not tested yet: training on new planes tests it on the ground and in the air.", null).WithId(id + "/test"));
        foreach (var item in c.ToLearn)
            body.Children.Add(Ui.Row((item.Done ? "✓ " : "○ ") + item.What, item.Note.Length > 0 ? item.Note : null, null)
                .WithId($"{id}/learn/{item.What}"));
        foreach (var note in c.Notes)
            body.Children.Add(Ui.Row("“" + note.Text + "”", $"{note.Skill} · from {note.Author} · {note.Evidence.Count} flight(s)", null).WithId($"{id}/note/{note.Id}"));
        if (finals.Count > 0)
            body.Children.Add(Ui.Row("Last finals", string.Join(" · ", finals.Select(f => string.Create(CultureInfo.InvariantCulture,
                $"{f.Weaves} crossing(s), {f.High:+0;-0;0} m 1 km out") + (f.Slope is { } s ? string.Create(CultureInfo.InvariantCulture, $" at {s:0.#}°") : ""))) +
                " (weaving softens its ailerons, arriving high slows its circuit)", null).WithId(id + "/finals"));
        return view;
    }
}
