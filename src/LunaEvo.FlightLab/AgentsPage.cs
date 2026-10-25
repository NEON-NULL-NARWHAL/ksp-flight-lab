// Flight Lab · src/LunaEvo.FlightLab/AgentsPage.cs
// [UI] The Agents page: AI pilots (KSP Wingman agents), one per KSP (kRPC port), each with its phase, what it's
//      training, its last flight, its plane's status, its events, and a box for orders; free flight and training.
//      From LUNA EVO Mission Control's Wingman page (2.18).
// Used by: MainWindow (rail: Fly → Agents)
// Guide:   Meat and Potatoes/04-Agents.md

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using LunaEvo.FlightLab.Core.Wingman;

namespace LunaEvo.FlightLab;

/// <summary>
/// Each agent is KSP Wingman's console run with --ipc: Flight Lab writes orders to it and reads one JSON event a line
/// back (AgentHost). The agents run on this PC next to their KSPs and the model server; nothing goes on the network
/// except to the model server's address and the tower on localhost.
/// </summary>
internal sealed class AgentsPage
{
    /// <summary>Events shown on a card, newest last (the agent keeps more).</summary>
    public const int ShownEvents = 40;

    private readonly MainWindow _host;
    private readonly StackPanel _setup = new() { Spacing = 2 };
    private readonly StackPanel _agents = new() { Spacing = 12 };
    private readonly TextBox _callSign = new TextBox { Text = "Wingman1", Width = 160 }.WithId("agents/callSign");
    private readonly TextBox _port = new TextBox { Text = "50000", Width = 100 }.WithId("agents/port");
    private readonly TextBox _model = new TextBox { Width = 200 }.WithId("agents/model");
    private readonly TextBox _llm = new TextBox { Width = 260 }.WithId("agents/llm");
    private readonly TextBox _planner = new TextBox { PlaceholderText = "e.g. gemma4:e4b (recommended)", Width = 200 }.WithId("agents/planner");
    private readonly CheckBox _tower = new CheckBox { Content = "Use the KSC tower", IsChecked = true }.WithId("agents/useTower");
    private readonly TextBlock _result = Ui.Text("", "sublabel").WithId("agents/result");
    private readonly TextBlock _none = Ui.Text("No agents yet.", "sublabel").WithId("agents/none");
    private readonly Dictionary<WingmanAgent, AgentCard> _cards = [];
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private AgentHost _agentHost;

    public AgentsPage(MainWindow host)
    {
        _host = host;
        _agentHost = new AgentHost(logFolder: host.LogFolder);
        // The models and server from Settings (gemma4:e4b by default: the tested pilot and planner).
        _model.Text = host.Settings.PilotModel;
        _planner.Text = host.Settings.PlannerModel;
        _llm.Text = host.Settings.LlmUrl;
        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Text("Agents", "headline"),
                Ui.Text("AI pilots for your KSPs. Each agent flies one KSP through kRPC with a local model (Ollama), takes orders " +
                        "in plain words (\"take off and fly east at 1000 m\", \"land at KSC\", \"go explore north\"), practises on " +
                        "its own (free flight, training) and works with the KSC tower.", "sublabel"),
            },
        };
        _result.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var start = Ui.Button("Start the agent", "fab").WithId("agents/start");
        start.Click += (_, _) => StartAgent();
        var form = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, box) in new (string, Control)[] { ("Call sign", _callSign), ("kRPC port", _port), ("Pilot model", _model), ("Model server", _llm), ("Planner (thinks)", _planner) })
            form.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 12, 8), Spacing = 2, Children = { Ui.Text(label, "sublabel"), box } });
        var (setup, setupBody, _) = Ui.Group("Start an agent");
        setupBody.Children.Add(_setup);
        setupBody.Children.Add(form);
        setupBody.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { start, _tower } });
        var (agents, agentsBody, _) = Ui.Group("Agents");
        agentsBody.Children.Add(_none);
        agentsBody.Children.Add(_agents);
        View = new ScrollViewer
        {
            Content = new StackPanel { Margin = new Thickness(28, 20, 28, 28), Spacing = 16, Children = { header, _result, setup, agents } },
        };
        Host = _agentHost;
        // Their planes' numbers now and then (the status tool, no model involved), while Flight Lab is open.
        _statusTimer.Tick += (_, _) =>
        {
            foreach (var agent in _agentHost.Agents.Where(a => a.Running))
                agent.AskStatus();
        };
        _statusTimer.Start();
    }

    public Control View { get; }

    /// <summary>Starts and keeps the agents' processes. Tests put in one with fake processes.</summary>
    internal AgentHost Host
    {
        get => _agentHost;
        set
        {
            _agentHost = value;
            _agentHost.Changed += () => Dispatcher.UIThread.Post(_host.Changed);
            // A stopped agent leaves the tower's sequence at once: its MCP server is killed with it, so it can't say so.
            _agentHost.AgentExited += agent => _host.Tower.Server?.Leaving(agent.Start.CallSign);
            _cards.Clear();
            _agents.Children.Clear();
            _none.IsVisible = true;
        }
    }

    internal string LastMessage => _result.Text ?? "";

    /// <summary>
    /// The command line's --agent CallSign:port[:model] (and --planner, --free minutes, --train): start agents at launch,
    /// and their free flight or training once they're connected (rig tests).
    /// </summary>
    public void StartFromCommandLine(IEnumerable<string> specs, string? planner = null, double? freeMinutes = null, IReadOnlyList<string>? train = null)
    {
        if (planner != null)
            _planner.Text = planner;
        foreach (var spec in specs)
        {
            var parts = spec.Split(':', 3);
            _callSign.Text = parts[0];
            if (parts.Length > 1)
                _port.Text = parts[1];
            if (parts.Length > 2)
                _model.Text = parts[2];
            StartAgent();
        }
        if (freeMinutes != null || train is { Count: > 0 })
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                var running = _agentHost.Agents.Where(a => a.Running).ToList();
                for (var i = 0; i < running.Count; i++)
                    if (train is { Count: > 0 } && i < train.Count)
                        running[i].Train(train[i], freeMinutes ?? 30);
                    else if (freeMinutes is { } minutes)
                        running[i].FreeFlight(minutes);
            }), TaskScheduler.Default);
    }

    internal void StartAgent()
    {
        if (WingmanTools.Installed() is not { } install)
        {
            _result.Text = "KSP Wingman isn't installed: Setup → Install.";
            return;
        }
        if (!int.TryParse(_port.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port))
        {
            _result.Text = "The kRPC port is a number (kRPC's default is 50000).";
            return;
        }
        var tower = _tower.IsChecked == true && _host.Tower.Server != null ? $"http://localhost:{_host.Tower.Server.Port}/" : null;
        var start = new AgentStart(_callSign.Text?.Trim() ?? "", port, _model.Text?.Trim() ?? "", _llm.Text?.Trim() ?? "", tower,
            _planner.Text?.Trim() is { Length: > 0 } planner ? planner : null);
        if (_agentHost.Check(start) is { } problem)
        {
            _result.Text = problem;
            return;
        }
        try
        {
            _agentHost.Start(install, start);
            _result.Text = $"{start.CallSign} started on kRPC port {port} with {start.Model}" + (tower != null ? ", with the tower." : ".");
            if (int.TryParse(start.CallSign[^1..], out var n) && n < 9) // ready for the next one
                _callSign.Text = start.CallSign[..^1] + (n + 1).ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _result.Text = "Couldn't start the agent: " + e.Message;
        }
        _host.Changed();
        Refresh();
    }

    public void Refresh()
    {
        _setup.Children.Clear();
        var install = WingmanTools.Installed();
        var towerOn = _host.Tower.Server != null;
        _setup.Children.Add(Ui.Row("KSP Wingman", install == null
            ? "Not installed: Setup → Install installs it, its mod for your KSP and a model."
            : $"{install.Version} · each KSP needs kRPC and Wingman's mod, its kRPC server running (on its own port), and a game loaded. " +
              (towerOn ? "The tower is on." : "The tower is off (Fly → KSC tower)."), null).WithId("agents/status"));

        foreach (var gone in _cards.Keys.Except(_agentHost.Agents).ToList())
        {
            _agents.Children.Remove(_cards[gone].View);
            _cards.Remove(gone);
        }
        foreach (var agent in _agentHost.Agents)
        {
            if (!_cards.TryGetValue(agent, out var card))
            {
                card = new AgentCard(this, agent);
                _cards[agent] = card;
                _agents.Children.Add(card.View);
            }
            card.Update();
        }
        _none.IsVisible = _agentHost.Agents.Count == 0;
    }

    /// <summary>Stops every agent (Flight Lab closing). Their planes keep their last orders.</summary>
    public void StopAll()
    {
        _statusTimer.Stop();
        _agentHost.QuitAll(TimeSpan.FromSeconds(3));
        _agentHost.Dispose();
    }

    private sealed class AgentCard
    {
        private readonly AgentsPage _page;
        private readonly WingmanAgent _agent;
        private readonly TextBlock _summary;
        private readonly TextBlock _status;
        private readonly StackPanel _events = new() { Spacing = 1 };
        private readonly ScrollViewer _scroll;
        private readonly TextBox _order;
        private readonly Button _stop;
        private readonly Button _free;
        private readonly ComboBox _training;
        private readonly Button _train;
        private bool _updating;
        private int _shown = -1;

        public AgentCard(AgentsPage page, WingmanAgent agent)
        {
            _page = page;
            _agent = agent;
            var id = $"agents/agent/{agent.Start.CallSign}";
            _summary = Ui.Text("", "sublabel").WithId(id + "/summary");
            _status = Ui.Text("", "sublabel").WithId(id + "/plane");
            _status.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            _scroll = new ScrollViewer { Content = _events, MaxHeight = 260 }.WithId(id + "/events");
            _order = new TextBox { PlaceholderText = "An order, e.g. \"take off and fly east at 1000 m\" or \"land at KSC\"", MinWidth = 420 }.WithId(id + "/order");
            var send = Ui.Button("Send", "fab").WithId(id + "/send");
            send.Click += (_, _) => Send();
            _order.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                    Send();
            };
            _free = Ui.Button("Free flight", "text").WithId(id + "/free");
            _free.Click += (_, _) =>
            {
                if (_agent.FreeFlying)
                    _agent.StopFreeFlight();
                else
                    _agent.FreeFlight(30);
            };
            ToolTip.SetTip(_free, "30 minutes of practice on its own: it picks what to practise (its weakest landings first), flies it, and learns from each flight. Any order stops it.");
            _training = new ComboBox { ItemsSource = WingmanAgent.Trainings.Select(t => t.Label).ToList(), SelectedIndex = 0, MinWidth = 170 }.WithId(id + "/training");
            var train = Ui.Button("Train", "text").WithId(id + "/train");
            ToolTip.SetTip(train, "Practise one ability on its own until it goes well (3 good flights in a row) or 30 minutes are up, learning as it goes. Any order stops it.");
            train.Click += (_, _) => _agent.Train(WingmanAgent.Trainings[Math.Max(0, _training.SelectedIndex)].Focus);
            // While it trains, picking another one switches to it (the owner found the menu did nothing mid-training).
            _training.SelectionChanged += (_, _) =>
            {
                if (!_updating && _agent.Running && _agent.Training != null && _training.SelectedIndex >= 0 &&
                    WingmanAgent.Trainings[_training.SelectedIndex].Focus != _agent.Training)
                    _agent.Train(WingmanAgent.Trainings[_training.SelectedIndex].Focus);
            };
            _train = train;
            var status = Ui.Button("Status", "text").WithId(id + "/askStatus");
            status.Click += (_, _) => _agent.AskStatus();
            _stop = Ui.Button("Stop", "text").WithId(id + "/stop");
            _stop.Click += async (_, _) =>
            {
                if (_agent.Running)
                    await _agent.StopAsync();
                else
                    _page._agentHost.Remove(_agent);
                _page.Refresh();
            };
            View = new Border
            {
                Classes = { "row" },
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new DockPanel { Children = { Dock(_stop, Avalonia.Controls.Dock.Right), Dock(status, Avalonia.Controls.Dock.Right), Dock(_free, Avalonia.Controls.Dock.Right), Dock(train, Avalonia.Controls.Dock.Right), Dock(_training, Avalonia.Controls.Dock.Right), Ui.Text(agent.Start.CallSign, "label") } },
                        _summary,
                        _status,
                        _scroll,
                        new DockPanel { Children = { Dock(send, Avalonia.Controls.Dock.Right), _order } },
                    },
                },
            }.WithId(id);
        }

        public Control View { get; }

        private static Control Dock(Control c, Dock dock)
        {
            DockPanel.SetDock(c, dock);
            c.Margin = new Thickness(6, 0, 0, 0);
            return c;
        }

        private void Send()
        {
            var text = _order.Text?.Trim() ?? "";
            if (text.Length == 0)
                return;
            _agent.Order(text);
            _order.Text = "";
        }

        public void Update()
        {
            var s = _agent.Start;
            _free.Content = Ui.IconAndText(_agent.FreeFlying ? "Stop free flight" : "Free flight", null);
            _free.IsEnabled = _agent.Running;
            // The menu shows the training running, and the button says what it does.
            var current = _agent.Training is { } t ? WingmanAgent.Trainings.ToList().FindIndex(x => x.Focus == t) : -1;
            if (current >= 0 && _training.SelectedIndex != current)
            {
                _updating = true;
                _training.SelectedIndex = current;
                _updating = false;
            }
            _train.Content = Ui.IconAndText(_agent.Training != null ? "Switch" : "Train", null);
            _train.IsEnabled = _agent.Running;
            _training.IsEnabled = _agent.Running;
            var doing = _agent.Training is { } tr ? " · training: " + WingmanAgent.Trainings.First(x => x.Focus == tr).Label.ToLowerInvariant()
                : _agent.FreeFlying ? " · free flight" : "";
            _summary.Text = $"{(_agent.Running ? Ui.Humanize(_agent.Phase).ToLowerInvariant() : "stopped")}{doing} · kRPC port {s.KrpcPort} · {s.Model}" +
                            (s.PlannerModel != null ? $" (planner {s.PlannerModel})" : "") +
                            (s.TowerUrl != null ? " · with the tower" : "") + (_agent.LastOutcome is { } last ? " · last flight: " + last : "");
            _status.Text = _agent.Status ?? "";
            _status.IsVisible = _agent.Status != null;
            _stop.Content = Ui.IconAndText(_agent.Running ? "Stop" : "Remove", null);
            _order.IsEnabled = _agent.Running;
            var events = _agent.Events;
            if (events.Count == _shown)
                return;
            _shown = events.Count;
            _events.Children.Clear();
            foreach (var e in events.TakeLast(ShownEvents))
            {
                var line = Ui.Text(string.Create(CultureInfo.InvariantCulture, $"{e.Time.ToLocalTime():HH:mm:ss}  {Label(e.Kind)}  {e.Text}"),
                    e.Kind is "answer" or "flight" or "error" or "debrief" ? "label" : "sublabel");
                line.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
                _events.Children.Add(line);
            }
            _scroll.ScrollToEnd();
        }

        private static string Label(string kind) => kind switch
        {
            "order" => "you",
            "chat" => "chat",
            "call" => "tool",
            "result" => "  →",
            "answer" => "ai",
            "flight" => "flight",
            "tower" => "tower",
            "phase" => "phase",
            "free" => "free",
            "debrief" or "lesson" or "knowledge" => "learn",
            "error" => "error",
            _ => "·",
        };
    }
}
