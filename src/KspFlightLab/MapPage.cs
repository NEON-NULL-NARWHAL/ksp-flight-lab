// KSP Flight Lab · src/KspFlightLab/MapPage.cs
// [UI] The map the agents explored (KSP Wingman's explore): every place around KSC on a grid, colored by its biome,
//      bigger the more agents went there, and the list of places with the ground's height.
// Used by: MainWindow (rail: Knowledge → Explored map)
// Guide:   Meat and Potatoes/03-Knowledge.md

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using KspFlightLab.Core.Tower;

namespace KspFlightLab;

internal sealed class MapPage
{
    private readonly MainWindow _host;
    private readonly ExploredMap _map = new ExploredMap().WithId("map/canvas");
    private readonly StackPanel _summary = new() { Spacing = 2 };
    private readonly StackPanel _list = new() { Spacing = 2 };

    public MapPage(MainWindow host)
    {
        _host = host;
        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Text("Explored map", "headline"),
                Ui.Text("Where the agents went exploring around KSC (legs they chose themselves, checked for range and fuel) and what " +
                        "they found there: the biome and how high the ground is. Every agent reads it before it picks new ground.", "sublabel"),
            },
        };
        var (map, mapBody, _) = Ui.Group("Around KSC");
        mapBody.Children.Add(_summary);
        mapBody.Children.Add(new Border { Height = 520, Margin = new Thickness(8), Child = _map });
        var (list, listBody, _) = Ui.Group("Places");
        listBody.Children.Add(_list);
        View = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(28, 20, 28, 28), Spacing = 16, Children = { header, map, list } } };
    }

    public Control View { get; }

    public void Refresh()
    {
        _summary.Children.Clear();
        _list.Children.Clear();
        var places = _host.ReadKnowledge(k => k.Places.ToList());
        _map.Places = places;
        if (places.Count == 0)
        {
            _summary.Children.Add(Ui.Text("Nothing explored yet: train an agent on Explore (Agents → Train → Explore).", "sublabel").WithId("map/none"));
            return;
        }
        var biomes = places.GroupBy(p => p.Biome).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} ({g.Count()})");
        var highest = places.MaxBy(p => p.Ground)!;
        _summary.Children.Add(Ui.Row($"{places.Count} place(s)", string.Join(", ", biomes) + string.Create(CultureInfo.InvariantCulture,
            $" · highest ground {highest.Ground:0} m ({highest.Biome}, {ExploredMap.FromKsc(highest)}, found by {highest.CallSign})"), null).WithId("map/summary"));
        foreach (var p in places.OrderBy(p => ExploredMap.Offset(p).Km))
            _list.Children.Add(Ui.Row(p.Biome, string.Create(CultureInfo.InvariantCulture,
                $"{ExploredMap.FromKsc(p)} · ground {p.Ground:0} m · found by {p.CallSign}, {p.Time.ToLocalTime():yyyy-MM-dd}") +
                (p.Visits > 1 ? $" · {p.Visits} visits" : ""), null).WithId($"map/place/{p.Id}"));
    }
}

/// <summary>Draws the explored places around KSC: rings every 10 km, north up, each place a dot in its biome's color.</summary>
internal sealed class ExploredMap : Control
{
    private const double KerbinRadius = 600_000, KscLatitude = -0.0486, KscLongitude = -74.7244;
    private IReadOnlyList<Place> _places = [];

    public IReadOnlyList<Place> Places
    {
        get => _places;
        set
        {
            _places = value;
            InvalidateVisual();
        }
    }

    /// <summary>A place's offset from KSC's runway: km north and east, the distance and the bearing.</summary>
    public static (double North, double East, double Km, double Bearing) Offset(Place p)
    {
        var north = (p.Latitude - KscLatitude) * Math.PI / 180 * KerbinRadius / 1000;
        var east = (((p.Longitude - KscLongitude) % 360 + 540) % 360 - 180) * Math.PI / 180 * KerbinRadius * Math.Cos(p.Latitude * Math.PI / 180) / 1000;
        return (north, east, Math.Sqrt(north * north + east * east), (Math.Atan2(east, north) * 180 / Math.PI + 360) % 360);
    }

    /// <summary>"12 km north-east of KSC".</summary>
    public static string FromKsc(Place p)
    {
        var o = Offset(p);
        if (o.Km < 1)
            return "at KSC";
        string[] names = ["north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west"];
        return string.Create(CultureInfo.InvariantCulture, $"{o.Km:0} km {names[(int)Math.Round(o.Bearing / 45) % 8]} of KSC");
    }

    /// <summary>A steady color per biome name (the same biome is the same color everywhere).</summary>
    public static Color BiomeColor(string biome) => biome.ToLowerInvariant() switch
    {
        "water" => Color.Parse("#3F8FD8"),
        "shores" => Color.Parse("#E8C26A"),
        "grasslands" => Color.Parse("#6FC24A"),
        "highlands" => Color.Parse("#B08A5A"),
        "mountains" => Color.Parse("#9A9A9A"),
        "deserts" => Color.Parse("#E0A35A"),
        "tundra" => Color.Parse("#B8D8E0"),
        "ice caps" or "northern ice shelf" or "southern ice shelf" => Color.Parse("#E8F4FF"),
        _ => Color.FromRgb((byte)(80 + Math.Abs(biome.GetHashCode()) % 150), (byte)(80 + Math.Abs(biome.GetHashCode() / 7) % 150), 160),
    };

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width < 50 || size.Height < 50)
            return;
        var center = new Point(size.Width / 2, size.Height / 2);
        var farthest = _places.Count == 0 ? 0 : _places.Max(p => Offset(p).Km);
        var rangeKm = Math.Max(30, Math.Ceiling((farthest + 5) / 10) * 10);
        var scale = Math.Min(size.Width, size.Height) / 2 / rangeKm;
        var grid = Brush("OutlineVariant", Colors.Gray);
        var text = Brush("OnSurfaceVariant", Colors.LightGray);
        var pen = new Pen(grid, 1);
        for (var km = 10.0; km <= rangeKm; km += 10)
        {
            context.DrawEllipse(null, pen, center, km * scale, km * scale);
            context.DrawText(Label($"{km:0} km", text, 11), center + new Vector(km * scale + 3, -14));
        }
        context.DrawLine(pen, new Point(center.X, 0), new Point(center.X, size.Height));
        context.DrawLine(pen, new Point(0, center.Y), new Point(size.Width, center.Y));
        context.DrawText(Label("N", text, 12), new Point(center.X + 4, 2));
        // KSC's runway (09: a short line west-east), the center of it all.
        var accent = Brush("Primary", Colors.LightGreen);
        context.DrawLine(new Pen(accent, 4), center + new Vector(-1.2 * scale, 0), center + new Vector(1.2 * scale, 0));
        context.DrawText(Label("KSC", accent, 12), center + new Vector(6, 6));
        var highest = _places.Count == 0 ? null : _places.MaxBy(p => p.Ground);
        foreach (var p in _places)
        {
            var o = Offset(p);
            var at = center + new Vector(o.East * scale, -o.North * scale);
            var r = 4 + Math.Min(p.Visits, 6);
            context.DrawEllipse(new SolidColorBrush(BiomeColor(p.Biome)), new Pen(Brushes.Black, 1), at, r, r);
            if (p == highest)
                context.DrawText(Label(string.Create(CultureInfo.InvariantCulture, $"{p.Ground:0} m"), text, 11), at + new Vector(r + 3, -6));
        }
    }

    private IBrush Brush(string key, Color fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);

    private static FormattedText Label(string text, IBrush brush, double size) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Program.FontFamily), size, brush);
}
