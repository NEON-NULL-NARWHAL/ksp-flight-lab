// Flight Lab · src/LunaEvo.FlightLab/TowerPage.cs
// [UI] The KSC tower page: the traffic controller for the agents (KSP Wingman), its channel (intentions, departures,
//      clearances, holds, go-arounds, vacate) and the planes it's handling. From LUNA EVO Mission Control 2.18.
// Used by: MainWindow (rail: Fly → KSC tower)
// Guide:   Meat and Potatoes/02-Tower.md

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using LunaEvo.FlightLab.Core;
using LunaEvo.FlightLab.Core.Tower;

namespace LunaEvo.FlightLab;

/// <summary>
/// The tower runs in Flight Lab (not in KSP): the agents' Wingman servers on this PC report to it every few seconds and
/// get its messages back. One per server: it keeps the agents' knowledge in the server's LunaEvo folder.
/// </summary>
internal sealed class TowerPage
{
    /// <summary>Channel lines shown, newest last (the tower keeps them all).</summary>
    public const int MaxLines = 200;

    private readonly MainWindow _host;
    private readonly StackPanel _status = new() { Spacing = 2 };
    private readonly StackPanel _planes = new() { Spacing = 2 };
    private readonly StackPanel _channel = new() { Spacing = 1 };
    private readonly ScrollViewer _channelScroll;
    private string? _problem;
    private int _shownLines = -1;

    public TowerPage(MainWindow host)
    {
        _host = host;
        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Text("KSC tower", "headline"),
                Ui.Text("A traffic controller for the agents at KSC's runway 09. It asks each agent's intentions once it's flying, " +
                        "clears departures one at a time, clears one plane at a time for an approach (published RNAV, the short " +
                        "visual, a direct, or the agent's own flight plan), holds the others over the runway, clears them to land " +
                        "when they call the runway in sight, resequences go-arounds with traffic information, and takes planes " +
                        "that are gone (crashed, recovered, offline) out of the sequence at once.", "sublabel"),
            },
        };
        var (status, statusBody, _) = Ui.Group("Tower");
        statusBody.Children.Add(_status);
        var (traffic, trafficBody, _) = Ui.Group("Traffic");
        trafficBody.Children.Add(_planes);
        var (channel, channelBody, _) = Ui.Group("Channel");
        _channelScroll = new ScrollViewer { Content = _channel, MaxHeight = 460 }.WithId("tower/channel");
        channelBody.Children.Add(_channelScroll);
        View = new ScrollViewer
        {
            Content = new StackPanel { Margin = new Thickness(28, 20, 28, 28), Spacing = 16, Children = { header, status, traffic, channel } },
        };
    }

    public Control View { get; }

    /// <summary>The running tower, if any (tests talk to it through Answer, without the network).</summary>
    public TowerServer? Server { get; private set; }

    /// <summary>Starts the tower on a port (the command line's --tower, the settings, or the button). A busy port is reported.</summary>
    public void Start(int port)
    {
        Stop();
        try
        {
            var server = new TowerServer(port)
            {
                LogPath = Path.Combine(_host.LogFolder, $"tower-{DateTime.Now:yyyy-MM-dd}.jsonl"),
                KnowledgePath = _host.KnowledgePath,
                RadioPath = _host.Settings.TowerChat ? LabSettingsStore.RadioPath(_host.Settings) : null,
            };
            server.Changed += () => Dispatcher.UIThread.Post(_host.Changed);
            server.Start();
            Server = server;
            _problem = null;
        }
        catch (System.Net.HttpListenerException e)
        {
            _problem = $"Couldn't listen on localhost:{port}: {e.Message}. Another program (LUNA EVO Mission Control's own tower?) may have it.";
        }
        _host.Changed();
    }

    public void Stop()
    {
        Server?.Dispose();
        Server = null;
        _host.Changed();
    }

    /// <summary>The radio on or off (the checkbox, or the settings): it writes to the server's radio file for the plugin.</summary>
    internal void SetChat(bool on)
    {
        _host.Save(_host.Settings with { TowerChat = on });
        if (Server != null)
            Server.RadioPath = on ? LabSettingsStore.RadioPath(_host.Settings) : null;
    }

    public void Refresh()
    {
        _status.Children.Clear();
        var running = Server != null;
        var port = Server?.Port ?? _host.Settings.TowerPort;
        var button = Ui.Button(running ? "Stop the tower" : "Start the tower", running ? "text" : "fab").WithId("tower/start");
        button.Click += (_, _) => { if (Server != null) Stop(); else Start(_host.Settings.TowerPort); };
        var state = _problem ?? (running
            ? $"On, on localhost:{port}. Agents started here use it; others connect with WINGMAN_TOWER_URL=http://localhost:{port}/."
            : "Off. Start it before the agents fly (it starts with Flight Lab unless Settings say otherwise).");
        _status.Children.Add(Ui.Row("KSC tower · runway 09", state, button).WithId("tower/status"));
        _status.Children.Add(Ui.Row("Knowledge", Server?.KnowledgePath ?? _host.KnowledgePath, null).WithId("tower/knowledge"));
        var radio = LabSettingsStore.RadioPath(_host.Settings);
        var onChat = new CheckBox { IsChecked = _host.Settings.TowerChat, IsEnabled = radio != null }.WithId("tower/onChat");
        onChat.IsCheckedChanged += (_, _) => SetChat(onChat.IsChecked == true);
        _status.Children.Add(Ui.Row("Talk on LMP chat", radio == null
            ? "Choose the server's folder in Settings to use this: the tower's lines and the agents' calls are said in its chat, as \"KSC tower\", by the LUNA EVO server plugin."
            : "The tower's lines and the agents' calls, said in the server's chat as \"KSC tower\" (needs the LUNA EVO server plugin, which Mission Control installs), so players hear the traffic.", onChat).WithId("tower/radio"));
        var open = Ui.Button("Open the log folder", "text").WithId("tower/logs");
        open.Click += (_, _) => { Directory.CreateDirectory(_host.LogFolder); Ui.Open(_host.LogFolder); };
        _status.Children.Add(Ui.Row("Channel log", "Every message, with its route or hold, as data, one JSON line each: " +
            (Server?.LogPath ?? Path.Combine(_host.LogFolder, "tower-<date>.jsonl")), open).WithId("tower/log"));

        _planes.Children.Clear();
        if (Server is not { } server)
        {
            _planes.Children.Add(Ui.Text("No tower running.", "sublabel"));
            _channel.Children.Clear();
            _shownLines = -1;
            return;
        }
        List<TowerPlane> planes;
        List<TowerMessage> lines;
        int count;
        lock (server.Tower)
        {
            planes = server.Tower.Planes.Where(p => p.State != PlaneState.Gone).OrderBy(p => p.Number == 0 ? 99 : p.Number).ToList();
            count = server.Tower.Channel.Count;
            lines = server.Tower.Channel.TakeLast(MaxLines).ToList();
        }
        if (planes.Count == 0)
            _planes.Children.Add(Ui.Text("No planes on the channel yet.", "sublabel"));
        foreach (var plane in planes)
            _planes.Children.Add(Ui.Row(plane.CallSign, Describe(plane), null).WithId($"tower/plane/{plane.CallSign}"));
        if (count == _shownLines)
            return;
        _shownLines = count;
        _channel.Children.Clear();
        foreach (var m in lines)
        {
            var line = Ui.Text(string.Create(CultureInfo.InvariantCulture, $"{m.Time.ToLocalTime():HH:mm:ss}  {m.From} → {m.To}:  {m.Text}"),
                m.From == Tower.Name ? "label" : "sublabel");
            line.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            _channel.Children.Add(line);
        }
        _channelScroll.ScrollToEnd();
    }

    private static string Describe(TowerPlane plane)
    {
        var state = plane.State switch
        {
            PlaneState.Approach => $"number 1, on the {plane.Approach}" + (plane.ToldClearedToLand ? ", cleared to land" : ""),
            PlaneState.Holding => $"number {plane.Number}, holding",
            PlaneState.Landed => "landed, vacating the runway",
            PlaneState.Local => $"local flight ({plane.Intent})",
            PlaneState.Asked => "asked for its intentions",
            _ => "on the ground or at the Space Center",
        };
        var r = plane.Last;
        return r == null ? state : string.Create(CultureInfo.InvariantCulture,
            $"{state} · {r.Situation.ToLowerInvariant()} · {r.Altitude:0} m · {r.Speed:0} m/s · heading {r.Heading:0}°") +
            (r.ApproachSpeed > 0 ? string.Create(CultureInfo.InvariantCulture, $" · approach speed {r.ApproachSpeed:0} m/s") : "");
    }
}
