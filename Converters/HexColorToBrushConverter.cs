using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FastExplorer.Converters;

// Turns a "#RRGGBB" accent color (from IconGlyphMap) into a Brush for FontIcon's
// Foreground. Falls back to the theme's normal text color when there's no
// per-type override, so unrecognized file types still look correct in both themes.
public sealed class HexColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex) && TryParseHex(hex, out var color))
        {
            return new SolidColorBrush(color);
        }

        return Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var fallback)
            ? fallback
            : new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static bool TryParseHex(string hex, out Color color)
    {
        color = default;
        var span = hex.AsSpan().TrimStart('#');
        if (span.Length != 6) return false;

        if (!byte.TryParse(span[..2], System.Globalization.NumberStyles.HexNumber, null, out var r)) return false;
        if (!byte.TryParse(span[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g)) return false;
        if (!byte.TryParse(span[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b)) return false;

        color = Color.FromArgb(255, r, g, b);
        return true;
    }
}
