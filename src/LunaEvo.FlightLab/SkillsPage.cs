// Flight Lab · src/LunaEvo.FlightLab/SkillsPage.cs
// [UI] The skill tree: every skill the agents can learn, what opens it, everyone's numbers and each agent's, and its
//      best lessons. Graded by the tower from real flights.
// Used by: MainWindow (rail: Knowledge → Skills)
// Guide:   Meat and Potatoes/03-Knowledge.md

using Avalonia;
using Avalonia.Controls;
using LunaEvo.FlightLab.Core.Tower;

namespace LunaEvo.FlightLab;

internal sealed class SkillsPage
{
    private readonly MainWindow _host;
    private readonly StackPanel _where = new() { Spacing = 2 };
    private readonly StackPanel _tree = new() { Spacing = 2 };

    public SkillsPage(MainWindow host)
    {
        _host = host;
        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Text("Skills", "headline"),
                Ui.Text("The server's tech tree. Every finished flight grades the skills it used (a good landing stops within 15 m " +
                        $"of the centerline, first time); a skill opens once what it needs has {Knowledge.UnlockAfter} good flights, by any agent.", "sublabel"),
            },
        };
        var (where, whereBody, _) = Ui.Group("Kept in");
        whereBody.Children.Add(_where);
        var (tree, treeBody, _) = Ui.Group("The tree");
        treeBody.Children.Add(_tree);
        View = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(28, 20, 28, 28), Spacing = 16, Children = { header, where, tree } } };
    }

    public Control View { get; }

    public void Refresh()
    {
        _where.Children.Clear();
        _tree.Children.Clear();
        _host.ReadKnowledge(k =>
        {
            var agents = k.Flights.Select(f => f.CallSign).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            _where.Children.Add(Ui.Row(_host.KnowledgePath, $"{k.Flights.Count} flight(s) by {agents.Count} agent(s), {k.Lessons.Count} lesson(s), " +
                $"{k.Tests.Count} plane test(s), {k.Places.Count} place(s) · " + (_host.Tower.Server != null ? "live from the tower" : "the tower is off: read from its file"),
                null).WithId("skills/where"));
            foreach (var skill in Knowledge.Tree)
            {
                var all = k.Of(skill.Id);
                var open = k.Unlocked(skill.Id);
                var needs = skill.Requires.Count == 0 ? "" : " · needs " + string.Join(", ", skill.Requires.Select(r =>
                    k.Of(r).Good >= Knowledge.UnlockAfter ? $"{r} ✓" : $"{r} ({k.Of(r).Good} of {Knowledge.UnlockAfter})"));
                var each = agents.Select(a => (a, k.Of(skill.Id, a))).Where(x => x.Item2.Attempts > 0).Select(x => $"{x.a} {x.Item2.Good}/{x.Item2.Attempts}");
                var lessons = k.For(skill.Id, 3).Select(l => "\n• " + l.Text);
                var row = Ui.Row((open ? "" : "🔒 ") + skill.Name, $"{skill.What} · everyone: {all}" + needs +
                    (each.Any() ? " · " + string.Join(", ", each) : "") + string.Concat(lessons), null).WithId($"skills/{skill.Id}");
                row.Classes.Set("inactive", !open);
                _tree.Children.Add(row);
            }
            return 0;
        });
    }
}
