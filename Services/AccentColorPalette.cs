using FastExplorer.Models;
using Microsoft.UI;
using Windows.UI;

namespace FastExplorer.Services;

// Maps each preset to a base color plus the Light1-3/Dark1-3 tints WinUI's built-in
// control templates expect (ListView selection highlight, CheckBox/ComboBox accents,
// etc. all key off these SystemAccentColor* resources). The tint percentages are an
// approximation of Fluent's own ramp, not a byte-for-byte match - good enough for a
// coherent set of shades without vendoring Microsoft's exact algorithm.
public static class AccentColorPalette
{
    private static readonly Dictionary<AccentColor, Color> BaseColors = new()
    {
        [AccentColor.Blue] = FromHex("#0078D4"),
        [AccentColor.Purple] = FromHex("#8764B8"),
        [AccentColor.Green] = FromHex("#107C10"),
        [AccentColor.Red] = FromHex("#E81123"),
        [AccentColor.Orange] = FromHex("#CA5010"),
        [AccentColor.Teal] = FromHex("#00B7C3"),
        [AccentColor.Pink] = FromHex("#E3008C"),
        [AccentColor.Yellow] = FromHex("#FFB900"),
    };

    public static Color GetBaseColor(AccentColor accent) => BaseColors[accent];

    public static Color Lighten(Color color, double amount) => Blend(color, Colors.White, amount);

    public static Color Darken(Color color, double amount) => Blend(color, Colors.Black, amount);

    private static Color Blend(Color color, Color toward, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            (byte)(color.R + (toward.R - color.R) * amount),
            (byte)(color.G + (toward.G - color.G) * amount),
            (byte)(color.B + (toward.B - color.B) * amount));
    }

    private static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        var r = System.Convert.ToByte(hex[..2], 16);
        var g = System.Convert.ToByte(hex[2..4], 16);
        var b = System.Convert.ToByte(hex[4..6], 16);
        return Color.FromArgb(255, r, g, b);
    }
}
