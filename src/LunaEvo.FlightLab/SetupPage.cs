// Flight Lab · src/LunaEvo.FlightLab/SetupPage.cs
// [UI] Install: KSP Wingman (from its GitHub releases, checked against GitHub's checksum), its mod in your KSP, kRPC,
//      the local model (Ollama), and the server's side (the LUNA EVO server plugin, for the tower's radio). From LUNA
//      EVO Mission Control's "Tools (for AI pilots)".
// Used by: MainWindow (rail: Setup → Install)
// Guide:   Meat and Potatoes/05-Install-And-Settings.md

using Avalonia;
using Avalonia.Controls;
using LunaEvo.FlightLab.Core;
using LunaEvo.FlightLab.Core.Install;
using LunaEvo.FlightLab.Core.Wingman;

namespace LunaEvo.FlightLab;

internal sealed class SetupPage
{
    private readonly MainWindow _host;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly TextBlock _result = Ui.Text("", "sublabel").WithId("setup/result");
    private (string Version, IReadOnlyList<string> Models)? _ollama;
    private DateTime _ollamaChecked = DateTime.MinValue;
    private bool _busy;

    public SetupPage(MainWindow host)
    {
        _host = host;
        _result.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Text("Install", "headline"),
                Ui.Text("Everything the agents need, in order: KSP Wingman, its mod and kRPC in your KSP, a local model, and the " +
                        "server's side for the tower's radio.", "sublabel"),
            },
        };
        var (group, body, _) = Ui.Group("Ready to fly?");
        body.Children.Add(_rows);
        View = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(28, 20, 28, 28), Spacing = 16, Children = { header, _result, group } } };
    }

    public Control View { get; }

    public void Refresh()
    {
        _rows.Children.Clear();
        var install = WingmanTools.Installed();
        var get = Ui.Button(install == null ? "Install" : "Check for updates", install == null ? "fab" : "text").WithId("setup/wingman");
        get.IsEnabled = !_busy;
        get.Click += async (_, _) => await InstallWingmanAsync();
        _rows.Children.Add(Ui.Row("1 · KSP Wingman", (install == null ? "Not installed. " : $"{install.Version} installed. ") +
            "The AI pilot: a local model flies a plane through kRPC, takes orders in plain words and talks to the KSC tower. " +
            "From its GitHub releases, checked against GitHub's checksum.", get).WithId("setup/wingmanStatus"));

        var ksp = _host.Settings.KspFolder;
        if (!KspFolder.IsKspFolder(ksp))
        {
            var choose = Ui.Button("Settings", "text").WithId("setup/chooseKsp");
            choose.Click += (_, _) => _host.Navigate(MainWindow.SettingsKey);
            _rows.Children.Add(Ui.Row("2 · Your KSP", "Choose the agents' KSP folder in Settings (the one with KSP_x64.exe or KSP.x86_64).", choose).WithId("setup/bridgeStatus"));
        }
        else if (install != null)
        {
            var bridge = WingmanTools.Bridge(ksp!, install);
            var krpc = WingmanTools.HasKrpc(ksp!);
            var state = $"{ksp} · " + bridge switch
            {
                BridgeState.Installed => "Wingman's mod is installed",
                BridgeState.Different => "another version of Wingman's mod is installed",
                _ => "Wingman's mod isn't installed",
            } + (krpc ? " · kRPC is installed." : " · kRPC isn't: Wingman flies through it (install it with CKAN; LUNA EVO Mission Control's CKAN page does it safely).");
            Control? action = null;
            if (bridge != BridgeState.Installed)
            {
                var button = Ui.Button(bridge == BridgeState.Different ? "Update the mod" : "Install the mod", "fab").WithId("setup/bridge");
                button.Click += (_, _) => InstallBridge(ksp!, install);
                action = button;
            }
            _rows.Children.Add(Ui.Row("2 · Wingman in your KSP", state, action).WithId("setup/bridgeStatus"));
        }
        else
            _rows.Children.Add(Ui.Row("2 · Wingman in your KSP", $"{ksp} · install KSP Wingman first (1).", null).WithId("setup/bridgeStatus"));

        // Again when it's older than 15 s (Ollama started after Flight Lab), not on every refresh.
        if (DateTime.UtcNow - _ollamaChecked > TimeSpan.FromSeconds(15))
        {
            _ollamaChecked = DateTime.UtcNow;
            _ = CheckOllamaAsync();
        }
        var wanted = new[] { _host.Settings.PilotModel, _host.Settings.PlannerModel }.Where(m => m.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string modelState;
        Control modelAction;
        if (_ollama is not { } ollama)
        {
            modelState = "Ollama isn't running on this PC (ollama.com). Wingman can also use any OpenAI-compatible model server (Settings).";
            var again = Ui.Button("Check again", "text").WithId("setup/ollamaCheck");
            again.Click += async (_, _) => await CheckOllamaAsync();
            modelAction = again;
        }
        else
        {
            var missing = wanted.Where(m => !ollama.Models.Any(x => x.Equals(m, StringComparison.OrdinalIgnoreCase))).ToList();
            modelState = missing.Count == 0
                ? $"Ollama {ollama.Version} · {string.Join(" and ", wanted)} ready ({ollama.Models.Count} model(s))."
                : $"Ollama {ollama.Version} · {string.Join(", ", missing)} not downloaded ({WingmanTools.DefaultModel} is the tested pilot and planner, a few GB).";
            if (missing.Count > 0)
            {
                var pull = Ui.Button("Download", "fab").WithId("setup/pull");
                pull.IsEnabled = !_busy;
                pull.Click += async (_, _) => await PullAsync(missing);
                modelAction = pull;
            }
            else
            {
                var again = Ui.Button("Check again", "text").WithId("setup/ollamaCheck");
                again.Click += async (_, _) => await CheckOllamaAsync();
                modelAction = again;
            }
        }
        _rows.Children.Add(Ui.Row("3 · Local model (Ollama)", modelState, modelAction).WithId("setup/ollamaStatus"));

        var server = _host.Settings.ServerFolder;
        string serverState;
        if (server is not { Length: > 0 } || !Directory.Exists(server))
            serverState = "No server chosen (Settings). The tower then keeps the agents' knowledge next to Flight Lab's settings, and can't talk on LMP chat.";
        else
        {
            var plugin = File.Exists(Path.Combine(server, "Plugins", "LunaEvo.ServerPlugin.dll"));
            serverState = $"{server} · the tower keeps the agents' knowledge in its LunaEvo folder · " + (plugin
                ? "the LUNA EVO server plugin is installed (the radio works)."
                : "the LUNA EVO server plugin isn't installed: the tower's radio needs it (LUNA EVO Mission Control installs it: Map → server plugin).");
        }
        var settings = Ui.Button("Settings", "text").WithId("setup/chooseServer");
        settings.Click += (_, _) => _host.Navigate(MainWindow.SettingsKey);
        _rows.Children.Add(Ui.Row("4 · The server", serverState, settings).WithId("setup/server"));
    }

    private void InstallBridge(string ksp, WingmanInstall install)
    {
        try
        {
            WingmanTools.InstallBridge(ksp, install);
            _result.Text = $"Wingman's mod is in {ksp}. Start KSP, start kRPC's server and load a game, then start an agent (Fly → Agents).";
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _result.Text = "Couldn't install Wingman's mod: " + e.Message;
        }
        Refresh();
    }

    internal async Task CheckOllamaAsync()
    {
        _ollamaChecked = DateTime.UtcNow;
        _ollama = await WingmanTools.OllamaAsync(LabHttp.Client);
        Refresh();
    }

    internal async Task InstallWingmanAsync()
    {
        _busy = true;
        Refresh();
        try
        {
            var installed = WingmanTools.Installed();
            if (await WingmanTools.LatestAsync(LabHttp.Client) is not { } latest)
                _result.Text = "No KSP Wingman release with a checksum was found on GitHub.";
            else if (installed != null && Version.Parse(installed.Version) >= latest.Release.Version!)
                _result.Text = $"KSP Wingman {installed.Version} is the newest.";
            else
            {
                var progress = new Progress<double>(p => _result.Text = $"Downloading KSP Wingman {latest.Release.Version}: {p:P0}");
                var install = await WingmanTools.DownloadAndInstallAsync(LabHttp.Client, latest.Release, latest.Zip, progress);
                _result.Text = $"KSP Wingman {install.Version} installed. Next: its mod in your KSP (2).";
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or DownloadVerificationException or InvalidDataException
                                       or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _result.Text = "Couldn't install KSP Wingman: " + e.Message;
        }
        _busy = false;
        _host.Changed();
        Refresh();
    }

    private async Task PullAsync(IReadOnlyList<string> models)
    {
        _busy = true;
        Refresh();
        string? problem = null;
        foreach (var model in models)
        {
            _result.Text = $"Ollama is downloading {model}...";
            var progress = new Progress<double>(p => _result.Text = $"Ollama is downloading {model}: {p:P0}");
            problem ??= await WingmanTools.PullAsync(LabHttp.Client, model, progress);
        }
        _result.Text = problem ?? $"{string.Join(" and ", models)} ready.";
        _busy = false;
        await CheckOllamaAsync();
    }
}
