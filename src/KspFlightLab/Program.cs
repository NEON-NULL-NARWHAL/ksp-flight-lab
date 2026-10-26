// KSP Flight Lab · src/KspFlightLab/Program.cs
// [UI] Entry point: parses the command line (the same flags LUNA EVO Mission Control's AI pilots took, so the rig's
//      scripts carry over), hooks the error log, starts Avalonia.
// Used by: Windows and Linux, when you launch it
// Guide:   Meat and Potatoes/06-App-UI.md

using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace KspFlightLab;

internal static class Program
{
    public const string FontFamily = "avares://KspFlightLab/Assets/Fonts#JetBrains Mono";

    public static StartupOptions Options { get; private set; } = new();

    [STAThread]
    public static int Main(string[] args)
    {
        Options = StartupOptions.Parse(args);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception x) Core.ErrorLog.Write("Unhandled", x); };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Core.ErrorLog.Write("Background task", e.Exception);
            e.SetObserved();
        };
        // A bug in a button handler is logged and the app carries on. Only here: headless tests rely on such errors failing.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Core.ErrorLog.Write("UI", e.Exception);
            e.Handled = true;
        };
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia setup. Also used by the Avalonia designer, so keep the name.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions { DefaultFamilyName = FontFamily })
            .LogToTrace();
}

/// <param name="Server">--server: the LMP server folder (its LunaEvo folder keeps the tower's knowledge and radio).</param>
/// <param name="Ksp">--ksp: the agents' KSP folder.</param>
/// <param name="SettingsPath">--settings: Flight Lab's settings file (portable mode, or a test's own).</param>
/// <param name="TowerPort">--tower [port]: start the KSC tower (it also starts by itself when the settings say so).</param>
/// <param name="Agents">--agent CallSign:port[:model] (or --wingman-agent), any number: start those agents.</param>
/// <param name="Train">--train fast,planes (or --wingman-train): each agent, in order, trains that.</param>
internal sealed record StartupOptions(string? Server = null, string? Ksp = null, string? SettingsPath = null, int? TowerPort = null,
    bool TowerChat = false, IReadOnlyList<string>? AgentList = null, string? Planner = null, double? Free = null, IReadOnlyList<string>? Train = null)
{
    public IReadOnlyList<string> Agents => AgentList ?? [];

    public static StartupOptions Parse(string[] args)
    {
        string? server = null, ksp = null, settings = null, planner = null;
        var agents = new List<string>();
        double? free = null;
        string[]? train = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--agent" or "--wingman-agent":
                    agents.Add(args[++i]);
                    break;
                case "--planner" or "--wingman-planner":
                    planner = args[++i];
                    break;
                case "--train" or "--wingman-train":
                    train = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                case "--free" or "--wingman-free":
                    free = double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : null;
                    break;
                case "--server":
                    server = Path.GetFullPath(args[++i]);
                    break;
                case "--ksp":
                    ksp = Path.GetFullPath(args[++i]);
                    break;
                case "--settings":
                    settings = Path.GetFullPath(args[++i]);
                    break;
            }
        }
        // --tower, with or without a port after it.
        int? tower = null;
        var t = Array.IndexOf(args, "--tower");
        if (t >= 0)
            tower = t + 1 < args.Length && int.TryParse(args[t + 1], out var port) && port is > 0 and < 65536 ? port : Core.Tower.TowerServer.DefaultPort;
        return new StartupOptions(server, ksp, settings, tower, args.Contains("--tower-chat"), agents, planner, free, train);
    }
}
