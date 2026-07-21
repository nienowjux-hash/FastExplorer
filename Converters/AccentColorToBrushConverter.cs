using FastExplorer.Models;
using FastExplorer.Services;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace FastExplorer.Converters;

public sealed class AccentColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is AccentColor accent
            ? new SolidColorBrush(AccentColorPalette.GetBaseColor(accent))
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
