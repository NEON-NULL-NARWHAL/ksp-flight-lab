// KSP Flight Lab · tests/KspFlightLab.Tests/UiHarness.cs
// [Test] Opens the real window headlessly over throwaway folders and drives it like a user (find by AutomationId,
//        click, wait). No network: GitHub and Ollama go to a fake. From Mission Control's UiHarness.
// Used by: All UI tests
// Guide:   Meat and Potatoes/06-App-UI.md

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KspFlightLab.Core;
using KspFlightLab.Core.Tests;
using KspFlightLab.Core.Wingman;

[assembly: AvaloniaTestApplication(typeof(KspFlightLab.Tests.TestApp))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace KspFlightLab.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

internal sealed class UiHarness : IDisposable
{
    /// <param name="server">Give the lab a (throwaway) server folder.</param>
    /// <param name="ksp">Give the lab a fake KSP.</param>
    public UiHarness(bool server = false, bool ksp = false, Func<LabSettings, LabSettings>? settings = null)
    {
        Folder = TestFolders.Temp("lab");
        SettingsPath = Path.Combine(Folder, "settings", "settings.json");
        ServerDirectory = server ? TestFolders.Temp("server") : null;
        if (ServerDirectory != null)
            File.WriteAllText(Path.Combine(ServerDirectory, "Server.exe"), "");
        KspDirectory = ksp ? TestFolders.FakeKsp() : null;
        // The tower stays off unless a test starts it (8920 may be the real one's, Mission Control's or Flight Lab's).
        var start = new LabSettings(ServerFolder: ServerDirectory, KspFolder: KspDirectory, TowerOnStart: false);
        LabSettingsStore.Save(SettingsPath, settings?.Invoke(start) ?? start);
        Web = new FakeHttp();
        LabHttp.Client = Web.Client;
        ErrorLog.Path = Path.Combine(Folder, "errors.log");
        // The test host must never be "updated" or restarted (an update test points CurrentExe at a throwaway file).
        MainWindow.CurrentExe = null;
        MainWindow.StartNewVersion = (_, _) => throw new InvalidOperationException("A test tried to start Flight Lab without faking it.");
        Window = new MainWindow(new StartupOptions(SettingsPath: SettingsPath));
        Window.Show();
        Pump();
    }

    public MainWindow Window { get; }
    public FakeHttp Web { get; }
    public string Folder { get; }
    public string SettingsPath { get; }
    public string? ServerDirectory { get; }
    public string? KspDirectory { get; }

    public void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Window.RefreshIfChanged();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    public void WaitFor(Func<bool> condition, string what, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Environment.GetEnvironmentVariable("CI") == "true" ? seconds * 3 : seconds);
        while (true)
        {
            Pump();
            if (condition())
                return;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Timed out waiting for: {what}");
            Thread.Sleep(30);
        }
    }

    /// <summary>Waits for an async UI method while pumping the dispatcher (never block on .GetResult() in a UI test).</summary>
    public void Await(Task task, string what = "task", int seconds = 30)
    {
        WaitFor(() => task.IsCompleted, what, seconds);
        task.GetAwaiter().GetResult();
    }

    public void Open(string key)
    {
        Click(Find<Button>("nav/" + key));
        Pump();
    }

    public T Find<T>(string automationId) where T : Control =>
        TryFind<T>(automationId) ?? throw new InvalidOperationException($"No {typeof(T).Name} with id '{automationId}' on the current page.");

    public T? TryFind<T>(string automationId) where T : Control =>
        Window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == automationId);

    /// <summary>A row's description (Ui.Row gives it the sublabel class).</summary>
    public string Description(string id)
    {
        Pump();
        return Find<Control>(id).GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("sublabel")).Text ?? "";
    }

    /// <summary>Every text in a control (a group, a list).</summary>
    public List<string> Texts(string id)
    {
        Pump();
        return Find<Control>(id).GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
    }

    public void Click(Button button)
    {
        Assert.True(button.IsEffectivelyEnabled, $"Button '{AutomationProperties.GetAutomationId(button)}' is disabled.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
    }

    /// <summary>Types into a box and presses Enter (how the Settings page's boxes commit).</summary>
    public void Type(string id, string text)
    {
        var box = Find<TextBox>(id);
        box.Text = text;
        box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
        Pump();
    }

    public void Dispose()
    {
        Window.Close();
        Pump();
        TestFolders.Delete(Folder);
        TestFolders.Delete(ServerDirectory);
        TestFolders.Delete(KspDirectory);
    }
}

/// <summary>An agent's console that's only pretend: it records orders and says what the test tells it to.</summary>
internal sealed class FakeAgent : IAgentProcess
{
    public List<string> Sent { get; } = [];
    public bool HasExited { get; private set; }
    public event Action<string?>? Output;
    public event Action? Exited;
    public void Say(string line) => Output?.Invoke(line);

    public void Send(string line)
    {
        Sent.Add(line);
        if (line.Contains("\"quit\""))
            Kill();
    }

    public void Kill()
    {
        HasExited = true;
        Exited?.Invoke();
    }

    public void Dispose()
    {
    }
}
