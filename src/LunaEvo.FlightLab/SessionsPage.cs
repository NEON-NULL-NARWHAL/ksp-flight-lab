// Flight Lab · src/LunaEvo.FlightLab/SessionsPage.cs
// [UI] The agents' sessions (one per agent and day, from their recorded logs): what happened, without opening a log:
//      flights and how they ended, tests, trainings and the choices made, lessons, places found, errors.
// Used by: MainWindow (rail: Records → Sessions)
// Guide:   Meat and Potatoes/04-Agents.md

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using LunaEvo.FlightLab.Core;

namespace LunaEvo.FlightLab;

internal sealed class SessionsPage
{
    /// <summary>Story lines shown, newest last.</summary>
    public const int Shown = 400;

    private readonly MainWindow _host;
    private readonly ComboBox _pick = new ComboBox { MinWidth = 420 }.WithId("sessions/pick");
    private readonly StackPanel _summary = new() { Spacing = 2 };
    private readonly StackPanel _story = new() { Spacing = 1 };
    private IReadOnlyList<Session> _sessions = [];
    private bool _updating;

    public SessionsPage(MainWindow host)
    {
        _host = host;
        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Text("Sessions", "headline"),
                Ui.Text("Each agent's day, from its recorded session (every line it said, kept next to Flight Lab's settings in logs): " +
                        "the story without the status lines and tool calls.", "sublabel"),
            },
        };
        var open = Ui.Button("Open the log folder", "text").WithId("sessions/logs");
        open.Click += (_, _) => { Directory.CreateDirectory(_host.LogFolder); Ui.Open(_host.LogFolder); };
        _pick.SelectionChanged += (_, _) =>
        {
            if (!_updating)
                ShowStory();
        };
        var (pick, pickBody, _) = Ui.Group("Session");
        pickBody.Children.Add(Ui.Row("Agent and day", "Newest first.", _pick));
        pickBody.Children.Add(_summary);
        pickBody.Children.Add(Ui.Row("Logs", _host.LogFolder, open));
        var (story, storyBody, _) = Ui.Group("What happened");
        storyBody.Children.Add(_story);
        View = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(28, 20, 28, 28), Spacing = 16, Children = { header, pick, story } } };
    }

    public Control View { get; }

    public void Refresh()
    {
        var selected = _pick.SelectedIndex >= 0 && _pick.SelectedIndex < _sessions.Count ? _sessions[_pick.SelectedIndex].Path : null;
        _sessions = SessionLog.List(_host.LogFolder);
        _updating = true;
        _pick.ItemsSource = _sessions.Select(s => $"{s.CallSign} · {s.Day:yyyy-MM-dd} · {s.Flights} flight(s), {s.Landed} landed, {s.Crashed} crashed").ToList();
        var again = _sessions.ToList().FindIndex(s => s.Path == selected);
        _pick.SelectedIndex = again >= 0 ? again : _sessions.Count > 0 ? 0 : -1;
        _updating = false;
        ShowStory();
    }

    private void ShowStory()
    {
        _summary.Children.Clear();
        _story.Children.Clear();
        if (_pick.SelectedIndex < 0 || _pick.SelectedIndex >= _sessions.Count)
        {
            _story.Children.Add(Ui.Text("No sessions yet: they're recorded while agents run (Fly → Agents).", "sublabel").WithId("sessions/none"));
            return;
        }
        var s = _sessions[_pick.SelectedIndex];
        var tests = s.Story.Count(l => l.Kind == "test");
        var lessons = s.Story.Count(l => l.Kind == "debrief" && !l.Text.Contains("nothing new", StringComparison.Ordinal));
        var places = s.Story.Count(l => l.Kind == "free" && l.Text.StartsWith("Explored:", StringComparison.Ordinal));
        _summary.Children.Add(Ui.Row($"{s.CallSign}, {s.Day:yyyy-MM-dd}", string.Create(CultureInfo.InvariantCulture,
            $"{s.Flights} flight(s): {s.Landed} landed, {s.Crashed} crashed · {tests} test(s) · {lessons} debrief(s) · {places} place(s) explored"), null).WithId("sessions/summary"));
        foreach (var line in s.Story.TakeLast(Shown))
        {
            var text = Ui.Text(string.Create(CultureInfo.InvariantCulture, $"{line.Time.ToLocalTime():HH:mm:ss}  {Label(line.Kind),-6}  {line.Text}"),
                line.Kind is "flight" or "test" or "error" ? "label" : "sublabel");
            text.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            _story.Children.Add(text);
        }
    }

    private static string Label(string kind) => kind switch
    {
        "flight" => "flight",
        "test" => "test",
        "free" => "free",
        "debrief" or "lesson" => "learn",
        "place" => "place",
        "error" => "error",
        _ => "·",
    };
}
