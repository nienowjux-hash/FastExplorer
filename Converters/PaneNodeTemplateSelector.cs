using FastExplorer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastExplorer.Converters;

// The pane tree is recursive and polymorphic (SplitPaneNode vs. leaf
// PaneViewModel), so a ContentControl bound to a PaneNode needs to pick its
// visual per concrete type at runtime - WinUI has no WPF-style implicit
// (keyless, DataType-matched) template lookup for ContentControl.Content, so
// this selector is the supported mechanism instead.
public sealed class PaneNodeTemplateSelector : DataTemplateSelector
{
    public DataTemplate SplitTemplate { get; set; } = null!;
    public DataTemplate PaneTemplate { get; set; } = null!;

    protected override DataTemplate SelectTemplateCore(object item) => item switch
    {
        SplitPaneNode => SplitTemplate,
        PaneViewModel => PaneTemplate,
        _ => base.SelectTemplateCore(item),
    };

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
