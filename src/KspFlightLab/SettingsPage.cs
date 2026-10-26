// KSP Flight Lab · src/KspFlightLab/SettingsPage.cs
// [UI] Flight Lab's own settings: the server and KSP folders, the models the agents use, the tower, the look, and
//      where everything is kept.
// Used by: MainWindow (rail: Setup → Settings)
// Guide:   Meat and Potatoes/05-Install-And-Settings.md

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using KspFlightLab.Core;
using KspFlightLab.Core.Install;
using KspFlightLab.Core.Wingman;

namespace KspFlightLab;

internal sealed class SettingsPage
{
    private readonly MainWindow _host;
    private readonly TextBox _server = new TextBox { Width = 420 }.WithId("settings/server");
    private readonly TextBox _ksp = new TextBox { Width = 420 }.WithId("settings/ksp");
    private readonly TextBox _pilot = new TextBox { Width = 240 }.WithId("settings/pilot");
    private readonly TextBox _planner = new TextBox { Width = 240 }.WithId("settings/planner");
    private readonly TextBox _llm = new TextBox { Width = 320 }.WithId("settings/llm");
    private readonly NumericUpDown _port = new NumericUpDown { Width = 160, Minimum = 1024, Maximum = 65535, Increment = 1, FormatString = "0" }.WithId("settings/towerPort");
    private readonly ToggleSwitch _towerOnStart = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0 }.WithId("settings/towerOnStart");
    private readonly ToggleSwitch _towerChat = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0 }.WithId("settings/towerChat");
    private readonly TextBox _hex = new TextBox { Width = 120 }.WithId("settings/accentHex");
    private readonly ComboBox _theme = new ComboBox { ItemsSource = new[] { "Dark", "Light", "System" }, Width = 160 }.WithId("settings/theme");
    private readonly TextBlock _result = Ui.Text("", "sublabel").WithId("settings/result");
    private readonly StackPanel _where = new() { Spacing = 2 };
    private readonly StackPanel _updates = new() { Spacing = 2 };
    private readonly ToggleSwitch _checkUpdates = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0 }.WithId("settings/checkUpdates");
    private string? _updateMessage;
    private bool _updating, _busy;

    public SettingsPage(MainWindow host)
    {
        _host = host;
        _result.TextWrapping = TextWrapping.Wrap;
        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Text("Settings", "headline"),
                Ui.Text($"Flight Lab's own settings, saved to {host.SettingsPath}. The command line (--server, --ksp, --tower) wins for that run.", "sublabel"),
            },
        };

        var (folders, foldersBody, _) = Ui.Group("Folders");
        foldersBody.Children.Add(Ui.Row("LMP server", "The server the agents fly on: the tower keeps their knowledge in its LunaEvo folder (the same file LUNA EVO " +
            "Mission Control used), and talks on its chat through the LUNA EVO server plugin.", Folder(_server, "settings/serverBrowse", "Choose the LMP server's folder (with Server.exe)", SetServer)));
        foldersBody.Children.Add(Ui.Row("KSP", "The agents' KSP: Wingman's mod and kRPC go in its GameData. A copy for the agents, not the one you play.",
            Folder(_ksp, "settings/kspBrowse", "Choose the agents' KSP folder", SetKsp)));

        var (models, modelsBody, _) = Ui.Group("Models");
        Commit(_pilot, text => text.Length > 0 ? _host.Settings with { PilotModel = text } : null);
        Commit(_planner, text => text.Length > 0 ? _host.Settings with { PlannerModel = text } : null);
        Commit(_llm, text => Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? _host.Settings with { LlmUrl = text } : null);
        modelsBody.Children.Add(Ui.Row("Pilot", $"Flies the plane (a call every few seconds: small and fast). {WingmanTools.DefaultModel} is the tested one.", _pilot));
        modelsBody.Children.Add(Ui.Row("Planner", "Plans flights, debriefs them and writes the lessons. The same model is fine.", _planner));
        modelsBody.Children.Add(Ui.Row("Model server", "Ollama's (the default) or any OpenAI-compatible server's address, ending in /v1/.", _llm));

        var (tower, towerBody, _) = Ui.Group("KSC tower");
        _port.ValueChanged += (_, e) =>
        {
            if (!_updating && e.NewValue is { } value)
                _host.Save(_host.Settings with { TowerPort = (int)value });
        };
        _towerOnStart.IsCheckedChanged += (_, _) =>
        {
            if (!_updating)
                _host.Save(_host.Settings with { TowerOnStart = _towerOnStart.IsChecked == true });
        };
        _towerChat.IsCheckedChanged += (_, _) =>
        {
            if (!_updating)
                _host.Tower.SetChat(_towerChat.IsChecked == true);
        };
        towerBody.Children.Add(Ui.Row("Port", "Where the agents reach the tower (localhost only). Used the next time the tower starts.", _port));
        towerBody.Children.Add(Ui.Row("Start with Flight Lab", "The tower is on whenever Flight Lab is.", _towerOnStart));
        towerBody.Children.Add(Ui.Row("Talk on LMP chat", "The tower's calls also go on the server's chat, for the players (needs the LUNA EVO server plugin).", _towerChat));

        var (look, lookBody, _) = Ui.Group("Appearance");
        _hex.LostFocus += (_, _) => CommitHex();
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
                CommitHex();
        };
        var swatches = new WrapPanel();
        foreach (var (name, hex) in ThemeManager.Presets)
        {
            var color = Color.Parse(hex);
            var swatch = new Button { Background = new SolidColorBrush(color) }.WithId("settings/preset/" + name);
            swatch.Classes.Add("swatch");
            ToolTip.SetTip(swatch, $"{name} ({hex})");
            swatch.Click += (_, _) => SetLook(ThemeManager.ToHex(color), _host.Settings.Theme);
            swatches.Children.Add(swatch);
        }
        _theme.SelectionChanged += (_, _) =>
        {
            if (!_updating && _theme.SelectedItem is string theme)
                SetLook(_host.Settings.AccentColor, theme);
        };
        var reset = Ui.Button("Reset to LUNA green", "text").WithId("settings/resetAppearance");
        reset.Click += (_, _) => SetLook(LabSettings.DefaultAccent, "Dark");
        lookBody.Children.Add(Ui.Row("Accent color", "A hex code, or a preset: the whole theme is made from it (as in Mission Control).", _hex));
        lookBody.Children.Add(new StackPanel { Spacing = 8, Margin = new Thickness(12, 4, 12, 12), Children = { swatches, reset } });
        lookBody.Children.Add(Ui.Row("Theme", "Dark, light, or follow the system.", _theme));

        var (updates, updatesBody, _) = Ui.Group("Updates");
        _checkUpdates.IsCheckedChanged += (_, _) =>
        {
            if (!_updating)
                _host.Save(_host.Settings with { CheckForUpdates = _checkUpdates.IsChecked == true });
        };
        updatesBody.Children.Add(Ui.Row("Check when Flight Lab starts", "Asks GitHub for a newer Flight Lab (its releases, checked against GitHub's checksum).", _checkUpdates));
        updatesBody.Children.Add(_updates);

        var (where, whereBody, _) = Ui.Group("Where things are kept");
        whereBody.Children.Add(_where);

        View = new ScrollViewer
        {
            Content = new StackPanel { Margin = new Thickness(28, 20, 28, 28), Spacing = 16, Children = { header, _result, folders, models, tower, look, updates, where } },
        };
    }

    public Control View { get; }

    public void Refresh()
    {
        _updating = true;
        var s = _host.Settings;
        _server.Text = s.ServerFolder ?? "";
        _ksp.Text = s.KspFolder ?? "";
        _pilot.Text = s.PilotModel;
        _planner.Text = s.PlannerModel;
        _llm.Text = s.LlmUrl;
        _port.Value = s.TowerPort;
        _towerOnStart.IsChecked = s.TowerOnStart;
        _towerChat.IsChecked = s.TowerChat;
        _hex.Text = s.AccentColor;
        _theme.SelectedItem = s.Theme;
        _checkUpdates.IsChecked = s.CheckForUpdates;
        _updating = false;

        _updates.Children.Clear();
        var check = Ui.Button("Check now", "text").WithId("settings/checkNow");
        check.IsEnabled = !_busy;
        check.Click += async (_, _) => await CheckNowAsync();
        var state = _host.AvailableAppUpdate is { } update
            ? $"Flight Lab {update.Version} is available (you have {MainWindow.AppVersion})."
            : _host.AppUpdateProblem ?? $"Flight Lab {MainWindow.AppVersion}.";
        _updates.Children.Add(Ui.Row("This version", state, check).WithId("settings/updateStatus"));
        if (_host.AvailableAppUpdate is { } available)
        {
            var install = Ui.Button("Update and restart", "fab").WithId("settings/updateNow");
            install.IsEnabled = !_busy;
            install.Click += async (_, _) => await UpdateNowAsync();
            _updates.Children.Add(Ui.Row($"What's new in {available.Version}", available.Release.Notes.Length > 0 ? Shorten(available.Release.Notes) : available.Release.PageUrl,
                install).WithId("settings/updateNotes"));
        }
        if (_updateMessage != null)
            _updates.Children.Add(Ui.Text(_updateMessage, "sublabel").WithId("settings/updateResult"));
        _updates.Children.Add(Ui.Row("Every version", AppUpdater.ReleasesPage, OpenButton("settings/releases", AppUpdater.ReleasesPage)));

        _where.Children.Clear();
        _where.Children.Add(Ui.Row("Agents' knowledge", _host.KnowledgePath, null).WithId("settings/knowledgePath"));
        _where.Children.Add(Ui.Row("Logs (tower, sessions, errors)", _host.LogFolder, OpenButton("settings/openLogs", _host.LogFolder)));
        _where.Children.Add(Ui.Row("KSP Wingman", WingmanTools.Root, OpenButton("settings/openTools", WingmanTools.Root)));
        _where.Children.Add(Ui.Row("Flight Lab", $"{MainWindow.AppVersion} · GPL-3.0 · with KSP Wingman and LUNA EVO Mission Control", null).WithId("settings/about"));
    }

    /// <summary>A folder box (type a path and press Enter, or browse).</summary>
    private Control Folder(TextBox box, string browseId, string title, Func<string?, string?> set)
    {
        box.LostFocus += (_, _) => CommitFolder(box, set);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
                CommitFolder(box, set);
        };
        var browse = Ui.Button("Browse", "text").WithId(browseId);
        browse.Click += async (_, _) =>
        {
            var picked = await _host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
            if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
            {
                box.Text = path;
                CommitFolder(box, set);
            }
        };
        return new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { box, browse } };
    }

    private void CommitFolder(TextBox box, Func<string?, string?> set)
    {
        if (_updating)
            return;
        var text = box.Text?.Trim().Trim('"') ?? "";
        _result.Text = set(text.Length == 0 ? null : text) ?? "";
        Refresh();
    }

    /// <summary>Saves the server folder (empty: none). The tower moves to its knowledge when no agent is flying.</summary>
    internal string? SetServer(string? folder)
    {
        if (folder == _host.Settings.ServerFolder)
            return null;
        if (folder != null && (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder)))
            return $"{folder} isn't a folder.";
        _host.Save(_host.Settings with { ServerFolder = folder });
        var note = folder != null && !File.Exists(Path.Combine(folder, "Server.exe")) && !File.Exists(Path.Combine(folder, "Server"))
            ? "There's no LMP Server in that folder; saved anyway. " : "";
        if (_host.Tower.Server is { } tower)
        {
            if (_host.Agents.Host.Agents.Any(a => a.Running))
                return note + "Saved. The tower keeps the old server's knowledge until it restarts (agents are flying).";
            _host.Tower.Start(tower.Port);
        }
        return note + "Saved.";
    }

    /// <summary>Saves the KSP folder (empty: none). It must be a KSP (KSP_x64.exe or KSP.x86_64, and GameData).</summary>
    internal string? SetKsp(string? folder)
    {
        if (folder == _host.Settings.KspFolder)
            return null;
        if (folder != null && !KspFolder.IsKspFolder(folder))
            return $"{folder} isn't a KSP folder (no KSP_x64.exe or KSP.x86_64 with GameData).";
        _host.Save(_host.Settings with { KspFolder = folder });
        return "Saved. Install Wingman's mod there on Setup → Install.";
    }

    private void Commit(TextBox box, Func<string, LabSettings?> make)
    {
        void Apply()
        {
            if (_updating)
                return;
            if (make(box.Text?.Trim() ?? "") is { } settings)
            {
                if (settings != _host.Settings)
                    _host.Save(settings);
                _result.Text = "";
            }
            else
                _result.Text = "That value isn't valid; the old one is kept.";
            Refresh();
        }
        box.LostFocus += (_, _) => Apply();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
                Apply();
        };
    }

    private void CommitHex()
    {
        if (_updating)
            return;
        var text = _hex.Text?.Trim() ?? "";
        if (!text.StartsWith('#'))
            text = "#" + text;
        if (Color.TryParse(text, out var color))
            SetLook(ThemeManager.ToHex(Color.FromRgb(color.R, color.G, color.B)), _host.Settings.Theme);
        else
            Refresh();
    }

    private void SetLook(string accent, string theme)
    {
        _host.Save(_host.Settings with { AccentColor = accent, Theme = theme });
        ThemeManager.Apply(ThemeManager.Parse(accent, Color.Parse(LabSettings.DefaultAccent)), theme);
        Refresh();
    }

    private static Button OpenButton(string id, string target)
    {
        var button = Ui.Button("Open", "text").WithId(id);
        button.Click += (_, _) =>
        {
            if (!target.StartsWith("https://", StringComparison.Ordinal))
                Directory.CreateDirectory(target);
            Ui.Open(target);
        };
        return button;
    }

    internal async Task CheckNowAsync()
    {
        _busy = true;
        _updateMessage = null;
        Refresh();
        await _host.CheckForAppUpdateAsync();
        _busy = false;
        _updateMessage = _host.AvailableAppUpdate == null && _host.AppUpdateProblem == null ? "This is the newest Flight Lab." : null;
        Refresh();
    }

    internal async Task UpdateNowAsync()
    {
        _busy = true;
        Refresh();
        var progress = new Progress<double>(p =>
        {
            _updateMessage = $"Downloading: {p:P0}";
            Refresh();
        });
        _updateMessage = await _host.UpdateAppAsync(progress);
        _busy = false;
        Refresh();
    }

    /// <summary>The release notes' first lines, for the row (the whole notes are on GitHub).</summary>
    private static string Shorten(string notes)
    {
        var text = string.Join(" ", notes.Split('\n').Select(l => l.Trim().TrimStart('#', '-', '*', ' ')).Where(l => l.Length > 0));
        return text.Length > 300 ? text[..300] + "…" : text;
    }
}
