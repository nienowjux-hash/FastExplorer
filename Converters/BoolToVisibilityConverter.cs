using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FastExplorer.Converters;

// ConverterParameter="Invert" flips the mapping - used to hide elements that only
// make sense inside a real folder (address bar, search, new folder) while browsing
// the drives list ("This PC"), where CurrentPath is empty.
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = value is bool b && b;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            boolValue = !boolValue;
        }
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
