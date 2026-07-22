using FastExplorer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace FastExplorer.Controls;

// One leaf of the split-pane tree: an independent tab strip. DataContext flows
// in as a PaneViewModel from the implicit PaneViewModelTemplate (App.xaml),
// the same mechanism FolderView already relies on for its own TabViewModel.
public sealed partial class PaneGroupView : UserControl
{
    // Edge band as a fraction of this pane's width/height - a drop within this
    // distance of an edge splits the pane in that direction; anything closer to
    // the middle just moves the tab into this pane's own strip.
    private const double EdgeZoneFraction = 0.25;

    private const string TabDragFormat = "FastExplorer.TabDrag";

    private PaneViewModel? Pane => DataContext as PaneViewModel;

    public PaneGroupView()
    {
        InitializeComponent();

        // Plain XAML PointerPressed= would miss most clicks here: ListView/TabView
        // mark pointer-pressed handled internally once they've dealt with selection,
        // which stops it from bubbling any further - registering with
        // handledEventsToo:true is what still lets this fire regardless, so clicking
        // anywhere in a pane (including on a file/tab) reliably marks it active.
        AddHandler(PointerPressedEvent, new PointerEventHandler(RootGrid_PointerPressed), true);

        TabDragState.DragStarted += OnDragStarted;
        TabDragState.DragEnded += OnDragEnded;
        Unloaded += (_, _) =>
        {
            TabDragState.DragStarted -= OnDragStarted;
            TabDragState.DragEnded -= OnDragEnded;
        };
    }

    private void OnDragStarted() => DragOverlay.Visibility = Visibility.Visible;

    private void OnDragEnded()
    {
        DragOverlay.Visibility = Visibility.Collapsed;
        ZoneHighlight.Visibility = Visibility.Collapsed;
    }

    // Pointer-down anywhere in this pane (not just its TabView) marks it as the
    // target for global keyboard shortcuts and sidebar actions - WinUI doesn't
    // auto-focus a pane just because it's visible, mirroring the FileList
    // focus-on-load gap already documented for per-tab shortcuts.
    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Pane is { } pane) pane.Owner.SetActivePane(pane);

        // Safety net: an ordinary click can only reach here if no OS drag-and-drop
        // operation is genuinely in progress, so TabDragState still claiming one is
        // active means some earlier drag ended without going through TabDragState.End()
        // - clear it defensively rather than leaving every pane's DragOverlay stuck
        // Visible (and silently intercepting all input) for the rest of the session.
        if (TabDragState.Tab is not null) TabDragState.End();
    }

    private void PaneTabView_AddTabButtonClick(TabView sender, object args)
    {
        if (Pane is { } pane) pane.Owner.NewTab(pane);
    }

    private void PaneTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (Pane is { } pane && args.Item is TabViewModel tab)
        {
            pane.Owner.CloseTab(pane, tab);
        }
    }

    private void PaneTabView_TabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args)
    {
        if (Pane is not { } pane || args.Item is not TabViewModel tab) return;

        // A pure marker, checked by DragOver handlers to distinguish this from an
        // ordinary OS file drag (e.g. FolderView's own StorageItems drop target) -
        // the actual payload travels through TabDragState, not the DataPackage.
        args.Data.SetData(TabDragFormat, true);
        args.Data.RequestedOperation = DataPackageOperation.Move;
        TabDragState.Start(tab, pane);
    }

    // Fires when a tab (ours or another pane's) is dropped directly onto this
    // pane's own tab strip - always a "move into this pane's tabs" regardless of
    // where in the strip it landed, so this is the Center-zone equivalent for
    // drops that land on the strip itself rather than the overlay below it.
    private void PaneTabView_TabStripDrop(object sender, DragEventArgs args)
    {
        if (TabDragState.Tab is not { } tab || TabDragState.SourcePane is not { } sourcePane) return;
        if (!args.DataView.Contains(TabDragFormat)) return;
        if (Pane is not { } targetPane) return;

        // Dropping back onto the strip it came from is an ordinary same-pane
        // reorder (WinUI's own CanReorderTabs already handles repositioning it) -
        // nothing to move, but TabDragState.End() must still run unconditionally
        // here. Previously this whole branch returned early for that case, which
        // left every pane's DragOverlay permanently Visible (and hit-testing on
        // top of the TabView/FolderView beneath it) after the very next ordinary
        // tab reorder - silently swallowing clicks, right-clicks, and drops.
        if (!ReferenceEquals(sourcePane, targetPane))
        {
            targetPane.Owner.MoveTabToPane(tab, sourcePane, targetPane);
        }
        TabDragState.End();
    }

    // Safety net: fires on the source TabView if the drag ends without being
    // accepted by any TabStripDrop/overlay Drop (e.g. released over the sidebar
    // or outside the window entirely) - just clears state so overlays hide again.
    private void PaneTabView_TabDroppedOutside(TabView sender, TabViewTabDroppedOutsideEventArgs args)
    {
        TabDragState.End();
    }

    private void DragOverlay_DragOver(object sender, DragEventArgs e)
    {
        if (TabDragState.SourcePane is not { } sourcePane || Pane is not { } targetPane
            || !e.DataView.Contains(TabDragFormat))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        ShowZoneHighlight(ComputeZone(e.GetPosition(DragOverlay), DragOverlay.ActualWidth, DragOverlay.ActualHeight));
    }

    private void DragOverlay_DragLeave(object sender, DragEventArgs e) => ZoneHighlight.Visibility = Visibility.Collapsed;

    private void DragOverlay_Drop(object sender, DragEventArgs e)
    {
        if (TabDragState.Tab is not { } tab || TabDragState.SourcePane is not { } sourcePane || Pane is not { } targetPane) return;

        var zone = ComputeZone(e.GetPosition(DragOverlay), DragOverlay.ActualWidth, DragOverlay.ActualHeight);
        if (zone == DockZone.Center)
        {
            if (!ReferenceEquals(sourcePane, targetPane)) targetPane.Owner.MoveTabToPane(tab, sourcePane, targetPane);
        }
        else
        {
            var orientation = zone is DockZone.Left or DockZone.Right
                ? Microsoft.UI.Xaml.Controls.Orientation.Horizontal
                : Microsoft.UI.Xaml.Controls.Orientation.Vertical;
            var newPaneIsSecond = zone is DockZone.Right or DockZone.Bottom;

            targetPane.Owner.SplitPaneWithTab(targetPane, orientation, newPaneIsSecond, tab, sourcePane);
        }

        TabDragState.End();
    }

    private static DockZone ComputeZone(Point position, double width, double height)
    {
        if (width <= 0 || height <= 0) return DockZone.Center;

        var fx = position.X / width;
        var fy = position.Y / height;

        var distances = new (DockZone Zone, double Distance)[]
        {
            (DockZone.Left, fx),
            (DockZone.Right, 1 - fx),
            (DockZone.Top, fy),
            (DockZone.Bottom, 1 - fy),
        };

        var closest = distances.OrderBy(d => d.Distance).First();
        return closest.Distance < EdgeZoneFraction ? closest.Zone : DockZone.Center;
    }

    // The trigger band (EdgeZoneFraction, 25%) is deliberately smaller than what
    // gets previewed here: showing only that thin 25% strip made it unclear the
    // drop would actually produce an even 50/50 split (matching SplitPaneNode's
    // default equal FirstLength/SecondLength) - previewing the real resulting
    // half, with a label naming the action, is what actually communicates it.
    private const double PreviewSizeFraction = 0.5;

    private void ShowZoneHighlight(DockZone zone)
    {
        ZoneHighlight.Visibility = Visibility.Visible;
        ZoneHighlight.HorizontalAlignment = HorizontalAlignment.Stretch;
        ZoneHighlight.VerticalAlignment = VerticalAlignment.Stretch;
        ZoneHighlight.Width = double.NaN;
        ZoneHighlight.Height = double.NaN;

        switch (zone)
        {
            case DockZone.Left:
                ZoneHighlight.HorizontalAlignment = HorizontalAlignment.Left;
                ZoneHighlight.Width = DragOverlay.ActualWidth * PreviewSizeFraction;
                ZoneHighlightLabel.Text = "Dividir à esquerda";
                break;
            case DockZone.Right:
                ZoneHighlight.HorizontalAlignment = HorizontalAlignment.Right;
                ZoneHighlight.Width = DragOverlay.ActualWidth * PreviewSizeFraction;
                ZoneHighlightLabel.Text = "Dividir à direita";
                break;
            case DockZone.Top:
                ZoneHighlight.VerticalAlignment = VerticalAlignment.Top;
                ZoneHighlight.Height = DragOverlay.ActualHeight * PreviewSizeFraction;
                ZoneHighlightLabel.Text = "Dividir acima";
                break;
            case DockZone.Bottom:
                ZoneHighlight.VerticalAlignment = VerticalAlignment.Bottom;
                ZoneHighlight.Height = DragOverlay.ActualHeight * PreviewSizeFraction;
                ZoneHighlightLabel.Text = "Dividir abaixo";
                break;
            case DockZone.Center:
            default:
                ZoneHighlightLabel.Text = "Mover para esta aba";
                break;
        }
    }
}
