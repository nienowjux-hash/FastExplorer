using FastExplorer.Models;
using Microsoft.UI.Xaml.Data;

namespace FastExplorer.Converters;

// Translates AppTheme/AccentColor enum values to Portuguese for display, without
// renaming the enum members themselves - those are persisted by name in settings.json
// (SettingsService) and used as C# identifiers throughout the codebase, so renaming
// them would be a much larger, riskier change than just translating how they're shown.
public sealed class EnumDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        AppTheme.System => "Sistema",
        AppTheme.Light => "Claro",
        AppTheme.Dark => "Escuro",
        AccentColor.Blue => "Azul",
        AccentColor.Purple => "Roxo",
        AccentColor.Green => "Verde",
        AccentColor.Red => "Vermelho",
        AccentColor.Orange => "Laranja",
        AccentColor.Teal => "Turquesa",
        AccentColor.Pink => "Rosa",
        AccentColor.Yellow => "Amarelo",
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
