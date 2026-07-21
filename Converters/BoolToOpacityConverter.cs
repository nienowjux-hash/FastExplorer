using Microsoft.UI.Xaml.Data;

namespace FastExplorer.Converters;

// Used to dim items marked with Ctrl+X (FileSystemItem.IsCut), mirroring Explorer's
// "this is about to move" look until it's pasted or the cut is replaced.
public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? 0.5 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
