// Flight Lab · src/LunaEvo.FlightLab/ThemeManager.cs
// [UI] Turns one accent color into the whole light/dark palette and applies it live.
// Used by: MainWindow, SettingsPage
// Guide:   Meat and Potatoes/06-App-UI.md

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace LunaEvo.FlightLab;

/// <summary>
/// Generates the whole palette from one accent color, like NOMM's Material "seed color": primaries keep the
/// accent's hue and saturation at fixed lightness steps, and the neutrals get a faint tint of the same hue.
/// </summary>
internal static class ThemeManager
{
    public static readonly (string Name, string Hex)[] Presets =
    [
        ("LUNA green", "#3FAE2A"),
        ("Kerbin blue", "#2F7FD8"),
        ("Kerbal orange", "#F28C28"),
        ("Duna red", "#C8452E"),
        ("Eve purple", "#8E4FD1"),
        ("Minmus mint", "#3FC7A4"),
        ("Jool lime", "#A6C93A"),
        ("Mun gray", "#8A9099"),
    ];

    /// <summary>Parses "#RRGGBB", or returns the fallback.</summary>
    public static Color Parse(string? hex, Color fallback) =>
        hex != null && Color.TryParse(hex, out var color) ? color : fallback;

    /// <summary>Formats as "#RRGGBB" (no alpha), the form stored in AppSettings.</summary>
    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>"Dark", "Light" or "System" to Avalonia's theme variant.</summary>
    public static ThemeVariant VariantFor(string theme) => theme switch
    {
        "Light" => ThemeVariant.Light,
        "System" => ThemeVariant.Default,
        _ => ThemeVariant.Dark,
    };

    /// <summary>Regenerates both palettes from the accent and swaps them into the app's resources; every DynamicResource updates live.</summary>
    public static void Apply(Color accent, string theme)
    {
        var app = Application.Current;
        if (app == null)
            return;

        app.RequestedThemeVariant = VariantFor(theme);
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var palette = Palette(accent, variant == ThemeVariant.Dark);
            if (app.Resources.ThemeDictionaries.TryGetValue(variant, out var provider) && provider is IResourceDictionary dictionary)
                foreach (var (key, color) in palette)
                    dictionary[key] = new SolidColorBrush(color);

            // Fluent's own accent drives toggles, focus rings, sliders and selection.
            if (app.Styles.OfType<FluentTheme>().FirstOrDefault()?.Palettes.TryGetValue(variant, out var fluent) == true)
            {
                fluent.Accent = palette["FluentAccent"];
                fluent.RegionColor = palette["Background"];
            }
        }
    }

    /// <summary>The brush colors for one theme, keyed by the resource names used in App.axaml.</summary>
    public static Dictionary<string, Color> Palette(Color accent, bool dark)
    {
        var hsl = accent.ToHsl();
        var h = hsl.H;
        var s = hsl.S;
        var n = Math.Min(s, 0.18); // neutrals: just a hint of the hue

        Color C(double saturation, double lightness) => new HslColor(1, h, Math.Clamp(saturation, 0, 1), lightness).ToRgb();

        return dark
            ? new()
            {
                ["Primary"] = C(Math.Min(s, 0.65), 0.66),
                ["OnPrimary"] = C(s, 0.12),
                ["PrimaryContainer"] = C(Math.Min(s, 0.6), 0.24),
                ["OnPrimaryContainer"] = C(Math.Min(s, 0.85), 0.80),
                ["Background"] = C(n, 0.07),
                ["SurfaceContainer"] = C(n * 0.85, 0.10),
                ["SurfaceVariant"] = C(n * 0.8, 0.14),
                ["RowHover"] = C(n * 0.8, 0.19),
                ["OnSurface"] = C(n * 0.6, 0.88),
                ["OnSurfaceVariant"] = C(n * 0.5, 0.63),
                ["Outline"] = C(n * 0.4, 0.55),
                ["OutlineVariant"] = C(n * 0.6, 0.24),
                ["FluentAccent"] = C(Math.Min(s, 0.6), 0.50),
            }
            : new()
            {
                ["Primary"] = C(Math.Min(s, 0.7), 0.30),
                ["OnPrimary"] = Colors.White,
                ["PrimaryContainer"] = C(Math.Min(s, 0.75), 0.84),
                ["OnPrimaryContainer"] = C(s, 0.10),
                ["Background"] = C(n, 0.97),
                ["SurfaceContainer"] = C(n, 0.93),
                ["SurfaceVariant"] = C(n, 0.90),
                ["RowHover"] = C(n, 0.85),
                ["OnSurface"] = C(n * 0.6, 0.10),
                ["OnSurfaceVariant"] = C(n * 0.5, 0.30),
                ["Outline"] = C(n * 0.4, 0.45),
                ["OutlineVariant"] = C(n * 0.6, 0.78),
                ["FluentAccent"] = C(Math.Min(s, 0.7), 0.32),
            };
    }
}
