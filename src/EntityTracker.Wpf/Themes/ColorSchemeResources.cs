using System.Windows;
using System.Windows.Media;

using EntityTracker.Theme;

namespace EntityTracker.Wpf.Themes;

/// <summary>
/// The app's colour resources, made from <see cref="ColorScheme"/>: a Color and a SolidColorBrush for every
/// entry, under the keys the views use. Merged first in EntityTrackerTheme.xaml, so the other dictionaries
/// can refer to them.
/// </summary>
/// <remarks>
/// Keys: base colours are Color.Brand.X / Brush.Brand.X; statuses are Brush.Status.X and
/// Brush.Status.X.Text (Brush.WorkStatus.* for work statuses); app entries are Brush.&lt;path&gt;, where a
/// final "Default" is left out (App.Overlay.Default is Brush.Overlay).
/// </remarks>
public sealed class ColorSchemeResources : ResourceDictionary
{
    public ColorSchemeResources()
    {
        foreach (ColorSchemeCatalog.Entry entry in ColorSchemeCatalog.All)
        {
            switch (entry.Section)
            {
                case nameof(ColorScheme.Base):
                    Add($"Brand.{entry.Name}", entry.Value);
                    break;
                case nameof(ColorScheme.Status):
                case nameof(ColorScheme.WorkStatus):
                    Add($"{entry.Section}.{entry.Name}", entry.Value);
                    break;
                case nameof(ColorScheme.App):
                    Add(entry.Name.EndsWith(".Default", StringComparison.Ordinal)
                        ? entry.Name[..^".Default".Length]
                        : entry.Name, entry.Value);
                    break;
            }
        }
    }

    private void Add(string key, object value)
    {
        switch (value)
        {
            case string color:
                AddColor(key, color, 1);
                break;
            case StatusColor status:
                AddColor(key, status.Fill, 1);
                AddColor($"{key}.Text", status.Text, 1);
                if (status.Edge is { } edge) AddColor($"{key}.Edge", edge, 1);
                break;
            case Tint tint:
                AddColor(key, tint.Color, tint.Opacity);
                break;
        }
    }

    private void AddColor(string key, string hex, double opacity)
    {
        Color color = (Color)ColorConverter.ConvertFromString(hex);
        SolidColorBrush brush = new(color) { Opacity = opacity };
        brush.Freeze();
        this[$"Color.{key}"] = color;
        this[$"Brush.{key}"] = brush;
    }
}
