// KSP Flight Lab · src/KspFlightLab/App.axaml.cs
// [UI] Avalonia application: loads App.axaml (theme and styles) and opens the main window.
// Used by: Program
// Guide:   Meat and Potatoes/06-App-UI.md

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace KspFlightLab;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(Program.Options);
        base.OnFrameworkInitializationCompleted();
    }
}
