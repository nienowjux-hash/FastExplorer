using Microsoft.UI.Xaml.Controls;

namespace FastExplorer.Controls;

// Hosted directly in a ContentDialog from FolderView's "Etiqueta > Personalizar..."
// (not nested inside another dialog, so no inline-confirm-panel trick needed - see
// ImageBatchView). Exposes the chosen Label/ColorHex as plain properties for
// FolderView.xaml.cs to read after ContentDialog.ShowAsync() returns Primary.
public sealed partial class TagEditView : UserControl
{
    private static readonly string[] ColorHexes = ["#E81123", "#F7630C", "#FFB900", "#107C10", "#0078D4", "#881798"];

    public TagEditView(string? currentLabel, string? currentColorHex)
    {
        InitializeComponent();
        LabelBox.Text = currentLabel ?? string.Empty;

        var index = currentColorHex is not null ? Array.IndexOf(ColorHexes, currentColorHex) : -1;
        ColorCombo.SelectedIndex = index >= 0 ? index : 0;
    }

    public string Label => LabelBox.Text.Trim();

    public string ColorHex => (ColorCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? ColorHexes[0];
}
