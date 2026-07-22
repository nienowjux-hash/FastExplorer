using System.ComponentModel;
using CommunityToolkit.WinUI.Controls;
using FastExplorer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FastExplorer.Controls;

// Renders one SplitPaneNode: two recursively-templated children (each itself
// either another split or a leaf PaneGroupView) laid out side by side or
// stacked, with a draggable GridSplitter between them. The grid's row/column
// structure is built once in code-behind, since which dimension (rows vs.
// columns) is used depends on Orientation, which isn't known until the node
// is bound, and SplitPaneNode never changes orientation after construction.
// First/Second *do* change after construction, though - splitting an already-
// nested pane mutates an existing SplitPaneNode's child in place (see
// PaneNode.cs) - so _firstHost/_secondHost.Content are kept in sync via a live
// PropertyChanged subscription rather than only being set once at BuildLayout time.
public sealed partial class SplitContainerView : UserControl
{
    private const double SplitterThickness = 6;

    private ContentControl? _firstHost;
    private ContentControl? _secondHost;
    private SplitPaneNode? _subscribedNode;

    public SplitContainerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildLayout();
        Unloaded += (_, _) => Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (_subscribedNode is not null) _subscribedNode.PropertyChanged -= Node_PropertyChanged;
        _subscribedNode = null;
    }

    private void Node_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SplitPaneNode node) return;
        switch (e.PropertyName)
        {
            case nameof(SplitPaneNode.First) when _firstHost is not null:
                _firstHost.Content = node.First;
                break;
            case nameof(SplitPaneNode.Second) when _secondHost is not null:
                _secondHost.Content = node.Second;
                break;
        }
    }

    private void BuildLayout()
    {
        Unsubscribe();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();
        RootGrid.Children.Clear();

        if (DataContext is not SplitPaneNode node) return;

        _subscribedNode = node;
        node.PropertyChanged += Node_PropertyChanged;

        var selector = (DataTemplateSelector)Application.Current.Resources["PaneNodeTemplateSelector"];
        _firstHost = new ContentControl
        {
            Content = node.First,
            ContentTemplateSelector = selector,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        _secondHost = new ContentControl
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

            Grid.SetColumn(_firstHost, 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(_secondHost, 2);

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

            Grid.SetRow(_firstHost, 0);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(_secondHost, 2);

            splitter.ResizeDirection = GridSplitter.GridResizeDirection.Rows;
            splitter.Height = SplitterThickness;
            splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            splitter.VerticalAlignment = VerticalAlignment.Stretch;
        }

        RootGrid.Children.Add(_firstHost);
        RootGrid.Children.Add(splitter);
        RootGrid.Children.Add(_secondHost);
    }
}
