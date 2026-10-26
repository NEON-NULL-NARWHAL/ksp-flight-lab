// KSP Flight Lab · src/KspFlightLab/MainWindow.cs
// [UI] The window: the navigation rail (Fly: agents, tower · Knowledge: skills, planes, map, lessons · Records:
//      sessions · Setup: install, settings), the pages, and the tower and agents they share.
// Used by: App
// Guide:   Meat and Potatoes/06-App-UI.md

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KspFlightLab.Core;
using KspFlightLab.Core.Install;
using KspFlightLab.Core.Wingman;

namespace KspFlightLab;

/// <summary>
/// The same shell as LUNA EVO Mission Control (a NOMM-style rail, rounded groups of rows), so the family looks like one.
/// The KSC tower lives here: it starts with Flight Lab (unless the settings say not to), and every page reads it.
/// </summary>
internal sealed class MainWindow : Window
{
    internal const string AgentsKey = "agents", TowerKey = "tower", SkillsKey = "skills", PlanesKey = "planes", MapKey = "map",
        LessonsKey = "lessons", SessionsKey = "sessions", SetupKey = "setup", SettingsKey = "settings";

    private readonly StackPanel _nav = new() { Spacing = 2 };
    private readonly ContentControl _page = new();
    private readonly Dictionary<string, (Button Nav, Control Page, Action? Refresh)> _pages = [];
    private readonly Avalonia.Controls.Shapes.Ellipse _towerDot = new() { Width = 8, Height = 8 };
    private readonly TextBlock _towerText = Ui.Text("", "sublabel").WithId("footer/tower");
    private readonly TextBlock _versionsText = Ui.Text("", "sublabel").WithId("footer/versions");
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _changed;
    private string? _current;

    internal MainWindow(StartupOptions options)
    {
        Title = "KSP Flight Lab";
        Width = 1240;
        Height = 860;
        MinWidth = 960;
        MinHeight = 600;
        try
        {
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://KspFlightLab/Assets/icon.png")));
        }
        catch (Exception e) when (e is FileNotFoundException or InvalidOperationException)
        {
            // Headless tests have no assets loader for icons: no icon.
        }

        SettingsPath = options.SettingsPath ?? LabSettingsStore.DefaultPath;
        var settings = LabSettingsStore.Load(SettingsPath);
        // The command line wins (the rig's scripts pass the server and the KSP).
        Settings = settings with
        {
            ServerFolder = options.Server ?? settings.ServerFolder,
            KspFolder = options.Ksp ?? settings.KspFolder,
            TowerChat = options.TowerChat || settings.TowerChat,
        };
        LogFolder = Path.Combine(Path.GetDirectoryName(SettingsPath)!, "logs");
        ErrorLog.Path = Path.Combine(LogFolder, "errors.log");
        // With its own settings file (portable, tests), Wingman's installs go next to it too.
        if (options.SettingsPath != null)
            WingmanTools.Root = Path.Combine(Path.GetDirectoryName(SettingsPath)!, "Tools", "KspWingman");
        ThemeManager.Apply(ThemeManager.Parse(Settings.AccentColor, Color.Parse(LabSettings.DefaultAccent)), Settings.Theme);

        Tower = new TowerPage(this);
        Agents = new AgentsPage(this);
        Skills = new SkillsPage(this);
        Planes = new PlanesPage(this);
        Map = new MapPage(this);
        Lessons = new LessonsPage(this);
        Sessions = new SessionsPage(this);
        Setup = new SetupPage(this);
        SettingsPage = new SettingsPage(this);

        var brand = new StackPanel { Margin = new Thickness(16, 4, 0, 16), Children = { Ui.Text("KSP", "brand"), Ui.Text("Flight Lab", "sublabel") } };
        _towerDot.Classes.Add("statusDot");
        _towerText.TextWrapping = TextWrapping.Wrap;
        _versionsText.TextWrapping = TextWrapping.Wrap;
        var footer = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(8, 12, 8, 0),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _towerDot, _towerText } },
                _versionsText,
            },
        };
        AddHeading("Fly");
        AddNav(AgentsKey, "Agents", Agents.View, Agents.Refresh);
        AddNav(TowerKey, "KSC tower", Tower.View, Tower.Refresh);
        AddHeading("Knowledge");
        AddNav(SkillsKey, "Skills", Skills.View, Skills.Refresh);
        AddNav(PlanesKey, "Planes", Planes.View, Planes.Refresh);
        AddNav(MapKey, "Explored map", Map.View, Map.Refresh);
        AddNav(LessonsKey, "Lessons", Lessons.View, Lessons.Refresh);
        AddHeading("Records");
        AddNav(SessionsKey, "Sessions", Sessions.View, Sessions.Refresh);
        AddHeading("Setup");
        AddNav(SetupKey, "Install", Setup.View, Setup.Refresh);
        AddNav(SettingsKey, "Settings", SettingsPage.View, SettingsPage.Refresh);

        var rail = new DockPanel { Margin = new Thickness(12, 16, 12, 16) };
        DockPanel.SetDock(brand, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        rail.Children.Add(brand);
        rail.Children.Add(footer);
        rail.Children.Add(new ScrollViewer { Content = _nav });
        var divider = new Border { Width = 1, Margin = new Thickness(0, 16) };
        divider[!Border.BackgroundProperty] = divider.GetResourceObservable("OutlineVariant").ToBinding();
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("240,Auto,*") };
        root.Children.Add(rail);
        Grid.SetColumn(divider, 1);
        root.Children.Add(divider);
        Grid.SetColumn(_page, 2);
        root.Children.Add(_page);
        Content = root;

        // The tower: on with Flight Lab (the settings), or asked for on the command line.
        if (options.TowerPort is { } port)
            Tower.Start(port);
        else if (Settings.TowerOnStart)
            Tower.Start(Settings.TowerPort);
        // What the agents and the tower say comes in fast: the page on screen is refreshed every 2 s at most.
        _refresh.Tick += (_, _) => RefreshIfChanged();
        _refresh.Start();
        Navigate(WingmanTools.Installed() == null ? SetupKey : AgentsKey);
        if (options.Agents.Count > 0)
            Agents.StartFromCommandLine(options.Agents, options.Planner, options.Free, options.Train);
        // After an update: the previous version's exe goes (it may still be closing, so again a little later).
        var exe = CurrentExe;
        AppUpdater.CleanUp(exe);
        _ = Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(_ => AppUpdater.CleanUp(exe), TaskScheduler.Default);
        if (Settings.CheckForUpdates)
            _ = CheckForAppUpdateAsync();
        Closing += (_, _) =>
        {
            Agents.StopAll();
            Tower.Stop();
        };
    }

    /// <summary>A newer Flight Lab found on GitHub, if any.</summary>
    internal AppUpdate? AvailableAppUpdate { get; private set; }

    /// <summary>Why the last update check couldn't tell (offline, private repository...), or null.</summary>
    internal string? AppUpdateProblem { get; private set; }

    /// <summary>The exe that updates replace. Tests point it at a throwaway file.</summary>
    internal static string? CurrentExe { get; set; } = Environment.ProcessPath;

    /// <summary>Starts the updated exe with the same options. Tests replace it so nothing is started.</summary>
    internal static Func<string, IReadOnlyList<string>, bool> StartNewVersion { get; set; } = (exe, arguments) =>
    {
        var info = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        return System.Diagnostics.Process.Start(info) != null;
    };

    /// <summary>Asks GitHub for a newer Flight Lab (at startup when the settings say so, or Settings → Check now).</summary>
    internal async Task CheckForAppUpdateAsync()
    {
        AppUpdateProblem = null;
        try
        {
            AvailableAppUpdate = await new AppUpdater(LabHttp.Client).CheckAsync(Version.Parse(AppVersion));
        }
        catch (AppUpdateUnavailableException e)
        {
            AppUpdateProblem = e.Message;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            AppUpdateProblem = "Couldn't reach GitHub to check for updates.";
        }
        Changed();
        RefreshCurrent();
    }

    /// <summary>
    /// Downloads and installs the newer Flight Lab, then restarts into it with the same options. Running agents are
    /// asked to quit first (closing does that). Returns a message if it didn't restart.
    /// </summary>
    internal async Task<string> UpdateAppAsync(IProgress<double>? progress = null)
    {
        if (AvailableAppUpdate is not { } update)
            return "There's no update to install.";
        if (!AppUpdater.CanUpdateItself(CurrentExe))
            return $"This copy of Flight Lab can't replace itself (it's a development build). Download {update.Release.Tag} from {AppUpdater.ReleasesPage}.";
        try
        {
            var downloaded = await new AppUpdater(LabHttp.Client).DownloadAsync(update, progress);
            await Task.Run(() => AppUpdater.Install(downloaded, CurrentExe!, update));
        }
        catch (UnauthorizedAccessException)
        {
            return $"Flight Lab can't write to {Path.GetDirectoryName(CurrentExe)}. Move it to a folder you can write to, or download {update.Release.Tag} from {AppUpdater.ReleasesPage}.";
        }
        catch (Exception e) when (e is DownloadVerificationException or HttpRequestException or IOException or TaskCanceledException)
        {
            return $"Update failed: {e.Message} Flight Lab {AppVersion} is still installed.";
        }
        if (!StartNewVersion(CurrentExe!, Environment.GetCommandLineArgs().Skip(1).ToList()))
            return $"Updated to {update.Release.Tag}, but it didn't start. Start Flight Lab again.";
        Close();
        return $"Updated to {update.Release.Tag}. Restarting...";
    }

    internal string SettingsPath { get; }

    internal LabSettings Settings { get; private set; }

    /// <summary>Where the tower's channel and each agent's session are recorded (next to the settings).</summary>
    internal string LogFolder { get; }

    internal string KnowledgePath => LabSettingsStore.KnowledgePath(Settings, SettingsPath);

    private (string Path, DateTime Written, Core.Tower.Knowledge Knowledge)? _fileKnowledge;

    /// <summary>
    /// Reads what the agents know: the running tower's (live), or, with the tower off, its file (so the Knowledge pages
    /// work any time; Mission Control's needed the tower on).
    /// </summary>
    internal T ReadKnowledge<T>(Func<Core.Tower.Knowledge, T> read)
    {
        if (Tower.Server is { } server)
            lock (server.Knowledge)
                return read(server.Knowledge);
        var path = KnowledgePath;
        var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_fileKnowledge is not { } cached || cached.Path != path || cached.Written != written)
            _fileKnowledge = (path, written, Core.Tower.Knowledge.Load(path));
        return read(_fileKnowledge.Value.Knowledge);
    }

    /// <summary>Deletes a lesson: through the running tower, or straight in its file with the tower off.</summary>
    internal void DeleteLesson(long id)
    {
        if (Tower.Server is { } server)
            server.DeleteLesson(id);
        else
        {
            var k = Core.Tower.Knowledge.Load(KnowledgePath);
            if (k.Delete(id))
            {
                try
                {
                    k.Save(KnowledgePath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    ErrorLog.Write("Knowledge", e);
                }
            }
            _fileKnowledge = null;
        }
        Changed();
        RefreshCurrent();
    }

    internal TowerPage Tower { get; }
    internal AgentsPage Agents { get; }
    internal SkillsPage Skills { get; }
    internal PlanesPage Planes { get; }
    internal MapPage Map { get; }
    internal LessonsPage Lessons { get; }
    internal SessionsPage Sessions { get; }
    internal SetupPage Setup { get; }
    internal SettingsPage SettingsPage { get; }

    /// <summary>The current page, by its rail key.</summary>
    internal string? Current => _current;

    /// <summary>Saves new settings (Settings page, Install page).</summary>
    internal void Save(LabSettings settings)
    {
        Settings = settings;
        try
        {
            LabSettingsStore.Save(SettingsPath, settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Settings", e);
        }
        Changed();
    }

    /// <summary>Something the pages show changed (an agent, the tower, the knowledge): refreshed within 2 s.</summary>
    internal void Changed() => _changed = true;

    /// <summary>The 2 s timer's work (tests call it: headless timers don't tick).</summary>
    internal void RefreshIfChanged()
    {
        if (!_changed)
            return;
        _changed = false;
        RefreshCurrent();
    }

    internal void Navigate(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
            return;
        if (_current != null && _pages.TryGetValue(_current, out var previous))
            previous.Nav.Classes.Remove("selected");
        page.Nav.Classes.Add("selected");
        _current = key;
        _page.Content = page.Page;
        RefreshCurrent();
    }

    internal void RefreshCurrent()
    {
        RefreshFooter();
        if (_current != null && _pages.TryGetValue(_current, out var page))
            page.Refresh?.Invoke();
    }

    private void RefreshFooter()
    {
        var on = Tower.Server != null;
        _towerDot.Classes.Set("running", on);
        _towerText.Text = on ? $"Tower on · localhost:{Tower.Server!.Port}" : "Tower off";
        var install = WingmanTools.Installed();
        var server = Settings.ServerFolder is { Length: > 0 } s ? Path.GetFileName(s.TrimEnd(Path.DirectorySeparatorChar)) : "no server chosen";
        _versionsText.Text = $"Flight Lab {AppVersion} · Wingman {install?.Version ?? "not installed"} · {Agents.Host.Agents.Count(a => a.Running)} agent(s) · server: {server}" +
            (AvailableAppUpdate is { } update ? $" · Flight Lab {update.Version} is available (Settings)" : "");
    }

    /// <summary>Flight Lab's version (from its assembly, set in the csproj).</summary>
    internal static string AppVersion =>
        typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

    private void AddHeading(string title)
    {
        var heading = Ui.Text(title, "groupTitle");
        heading.Margin = new Thickness(16, _nav.Children.Count == 0 ? 0 : 14, 0, 4);
        _nav.Children.Add(heading);
    }

    private void AddNav(string key, string label, Control page, Action? refresh)
    {
        var nav = new Button { Content = new TextBlock { Text = label } }.WithId("nav/" + key);
        nav.Classes.Add("nav");
        nav.Click += (_, _) => Navigate(key);
        _nav.Children.Add(nav);
        _pages[key] = (nav, page, refresh);
    }
}
