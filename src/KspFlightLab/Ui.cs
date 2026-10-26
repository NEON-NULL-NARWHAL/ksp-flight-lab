// KSP Flight Lab · src/KspFlightLab/Ui.cs
// [UI] Little helpers for building styled controls in code (text, buttons, groups, rows, icons).
// Used by: All UI files
// Guide:   Meat and Potatoes/06-App-UI.md

using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace KspFlightLab;

/// <summary>Small helpers for building the NOMM-style controls in code. Styling lives in App.axaml.</summary>
internal static partial class Ui
{
    /// <summary>"DedicatedServer" → "Dedicated Server", "LMPModControl" → "LMP Mod Control".</summary>
    public static string Humanize(string name) =>
        // LMP misspells HearbeatMsInterval; the file keeps LMP's name, people see the right word.
        WordBoundary().Replace(name, " ").Replace("Hearbeat", "Heartbeat", StringComparison.Ordinal);

    /// <summary>A TextBlock with style classes from App.axaml, e.g. "label", "sublabel", "headline".</summary>
    public static TextBlock Text(string text, params string[] classes)
    {
        var block = new TextBlock { Text = text };
        block.Classes.AddRange(classes);
        return block;
    }

    /// <summary>Sets the AutomationId, which the UI tests (and screen readers) use to find the control.</summary>
    public static T WithId<T>(this T control, string automationId) where T : Control
    {
        AutomationProperties.SetAutomationId(control, automationId);
        return control;
    }

    /// <summary>
    /// A summary card (label over a value). With <paramref name="click"/> it's a button (Button.card: hover, hand cursor,
    /// keyboard); <paramref name="selected"/> outlines it, for a card that's filtering a list.
    /// </summary>
    public static Control Card(string id, string label, string value, Action? click = null, bool selected = false, string? tip = null)
    {
        var content = new StackPanel { Spacing = 2, Children = { Text(label, "sublabel"), Text(value, "value") } };
        Control card;
        if (click == null)
            card = new Border { Child = content };
        else
        {
            var button = new Button { Content = content };
            button.Click += (_, _) => click();
            ToolTip.SetTip(button, tip);
            card = button;
        }
        card.Classes.Add("card");
        card.Classes.Set("selected", selected);
        return card.WithId(id);
    }

    /// <summary>A button with a style class ("fab", "text", "chip"...) and an optional icon resource key.</summary>
    public static Button Button(string text, string cssClass, string? icon = null)
    {
        var button = new Button { Content = IconAndText(text, icon) };
        button.Classes.Add(cssClass);
        return button;
    }

    /// <summary>Button content: an optional icon followed by text.</summary>
    public static Control IconAndText(string text, string? icon)
    {
        if (icon == null)
            return new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(Icon(icon));
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    /// <summary>An icon from a StreamGeometry in App.axaml (e.g. "IconSave"), bound so it follows theme changes.</summary>
    public static PathIcon Icon(string resourceKey)
    {
        var icon = new PathIcon();
        icon[!PathIcon.DataProperty] = icon.GetResourceObservable(resourceKey).ToBinding();
        return icon;
    }

    /// <summary>A NOMM settings group: a small primary-coloured title over a rounded panel.</summary>
    public static (Control View, StackPanel Body, TextBlock Title) Group(string title)
    {
        var heading = Text(title, "groupTitle");
        var body = new StackPanel { Spacing = 2 };
        var panel = new Border { Child = body };
        panel.Classes.Add("group");
        var view = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 20), Children = { heading, panel } };
        return (view, body, heading);
    }

    /// <summary>Only web links and existing folders may be opened.</summary>
    internal static bool CanOpen(string target) =>
        (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") ||
        (Path.IsPathFullyQualified(target) && Directory.Exists(target));

    /// <summary>A settings row in the same layout as the generated ones: label and description left, control right.</summary>
    public static Border Row(string label, string? description, Control? editor)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { Text(label, "label") } };
        if (!string.IsNullOrWhiteSpace(description))
            text.Children.Add(Text(description, "sublabel"));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(text);
        if (editor != null)
        {
            editor.VerticalAlignment = VerticalAlignment.Center;
            editor.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(editor, 1);
            grid.Children.Add(editor);
        }

        var row = new Border { Child = grid };
        row.Classes.Add("row");
        return row;
    }

    /// <summary>
    /// Opens a folder in Explorer or a web link in the browser, and nothing else: with UseShellExecute, any other
    /// string could launch a program or document, so it's refused.
    /// </summary>
    public static void Open(string target)
    {
        if (!CanOpen(target))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing registered to open it; not worth a dialog.
        }
    }

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex WordBoundary();
}
