// Flight Lab · src/LunaEvo.FlightLab/LessonsPage.cs
// [UI] The lessons the agents wrote after their flights (each citing the flights it came from), by skill, with the
//      notes about planes apart; delete any you don't like.
// Used by: MainWindow (rail: Knowledge → Lessons)
// Guide:   Meat and Potatoes/03-Knowledge.md

using Avalonia;
using Avalonia.Controls;

namespace LunaEvo.FlightLab;

internal sealed class LessonsPage
{
    private readonly MainWindow _host;
    private readonly StackPanel _general = new() { Spacing = 2 };
    private readonly StackPanel _planes = new() { Spacing = 2 };

    public LessonsPage(MainWindow host)
    {
        _host = host;
        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Text("Lessons", "headline"),
                Ui.Text("What the agents learned, each from a flight of theirs (the debrief): agents read them as facts before they fly. " +
                        "A lesson nobody confirms for 14 days is dropped; one another flight says again is confirmed.", "sublabel"),
            },
        };
        var (general, generalBody, _) = Ui.Group("Flying");
        generalBody.Children.Add(_general);
        var (planes, planesBody, _) = Ui.Group("About planes");
        planesBody.Children.Add(_planes);
        View = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(28, 20, 28, 28), Spacing = 16, Children = { header, general, planes } } };
    }

    public Control View { get; }

    public void Refresh()
    {
        _general.Children.Clear();
        _planes.Children.Clear();
        _host.ReadKnowledge(k =>
        {
            foreach (var lesson in k.Lessons.OrderBy(l => l.Craft ?? "").ThenBy(l => l.Skill).ThenByDescending(l => l.Weight))
            {
                var delete = Ui.Button("Delete", "text").WithId($"lessons/{lesson.Id}/delete");
                var id = lesson.Id;
                delete.Click += (_, _) => _host.DeleteLesson(id);
                var row = Ui.Row(lesson.Text, (lesson.Craft != null ? $"✈ {lesson.Craft} · " : "") + $"{lesson.Skill} · from {lesson.Author} · " +
                    $"{lesson.Evidence.Count} flight(s)" + (lesson.Confirmations > 0 ? $", confirmed {lesson.Confirmations}×" : "") +
                    $" · {lesson.Created.ToLocalTime():yyyy-MM-dd}", delete).WithId($"lessons/{lesson.Id}");
                (lesson.Craft != null ? _planes : _general).Children.Add(row);
            }
            return 0;
        });
        if (_general.Children.Count == 0)
            _general.Children.Add(Ui.Text("No lessons yet: they come from the debrief after each flight (with a planner model).", "sublabel"));
        if (_planes.Children.Count == 0)
            _planes.Children.Add(Ui.Text("No notes about planes yet: training on new planes writes them from their tests.", "sublabel"));
    }
}
