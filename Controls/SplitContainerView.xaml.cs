using CommunityToolkit.WinUI.Controls;
using FastExplorer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FastExplorer.Controls;

// Renders one SplitPaneNode: two recursively-templated children (each itself
// either another split or a leaf PaneGroupView) laid out side by side or
// stacked, with a draggable GridSplitter between them. The grid is built in
// code-behind rather than static XAML because which dimension (rows vs.
// columns) is used depends on Orientation, which isn't known until the node
// is bound - SplitPaneNode never changes orientation after construction, so
// this only needs to run once per bound node, not on every property change.
public sealed partial class SplitContainerView : UserControl
{
    private const double SplitterThickness = 6;

    public SplitContainerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildLayout();
    }

    private void BuildLayout()
    {
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();
        RootGrid.Children.Clear();

        if (DataContext is not SplitPaneNode node) return;

        var selector = (DataTemplateSelector)Application.Current.Resources["PaneNodeTemplateSelector"];
        var firstHost = new ContentControl
        {
            Content = node.First,
            ContentTemplateSelector = selector,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        var secondHost = new ContentControl
        {
            Content = node.Second,
            ContentTemplateSelector = selector,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        var splitter = new GridSplitter
        {
            ResizeBehavior = GridSplitter.GridResizeBehavior.PreviousAndNext,
            Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
        };

        if (node.Orientation == Orientation.Horizontal)
        {
            // One-time seed from the model, not a live binding: GridSplitter drags
            // resize the Grid's own ColumnDefinitions directly (that's how it works
            // structurally), and split ratios aren't persisted in this phase, so
            // there's nothing further for the model's Length properties to drive.
            var first = new ColumnDefinition { Width = node.FirstLength };
            var gutter = new ColumnDefinition { Width = new GridLength(SplitterThickness) };
            var second = new ColumnDefinition { Width = node.SecondLength };
            RootGrid.ColumnDefinitions.Add(first);
            RootGrid.ColumnDefinitions.Add(gutter);
            RootGrid.ColumnDefinitions.Add(second);

            Grid.SetColumn(firstHost, 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(secondHost, 2);

            splitter.ResizeDirection = GridSplitter.GridResizeDirection.Columns;
            splitter.Width = SplitterThickness;
            splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            splitter.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            var first = new RowDefinition { Height = node.FirstLength };
            var gutter = new RowDefinition { Height = new GridLength(SplitterThickness) };
            var second = new RowDefinition { Height = node.SecondLength };
            RootGrid.RowDefinitions.Add(first);
            RootGrid.RowDefinitions.Add(gutter);
            RootGrid.RowDefinitions.Add(second);

            Grid.SetRow(firstHost, 0);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(secondHost, 2);

            splitter.ResizeDirection = GridSplitter.GridResizeDirection.Rows;
            splitter.Height = SplitterThickness;
            splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            splitter.VerticalAlignment = VerticalAlignment.Stretch;
        }

        RootGrid.Children.Add(firstHost);
        RootGrid.Children.Add(splitter);
        RootGrid.Children.Add(secondHost);
    }
}
