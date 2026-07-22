using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastExplorer.ViewModels;

// A node in the split-pane tree: either a SplitPaneNode (two children, laid out
// side by side or stacked) or a leaf PaneViewModel (an actual tab strip). Parent
// back-references let RemoveTabFromPane splice out an emptied leaf in O(1) without
// rebuilding the tree from the root - rebuilding would force WinUI to re-realize
// the whole visual subtree (losing scroll position, tearing down FileSystemWatchers)
// on every close. Node identity must stay stable for the same reason.
public abstract partial class PaneNode : ObservableObject
{
    public PaneNode? Parent { get; set; }
}

public partial class SplitPaneNode : PaneNode
{
    [ObservableProperty]
    private Orientation orientation;

    [ObservableProperty]
    private GridLength firstLength = new(1, GridUnitType.Star);

    [ObservableProperty]
    private GridLength secondLength = new(1, GridUnitType.Star);

    // [ObservableProperty], not a plain property with a hand-written setter: the
    // recursive tree is rendered by SplitContainerView binding a ContentControl's
    // Content to First/Second via a live property-changed subscription (not a
    // one-shot imperative read), specifically so that splitting an *already
    // nested* pane - Replace() below, mutating an existing SplitPaneNode's child
    // in place rather than reassigning MainViewModel.RootPane itself - actually
    // repaints. Before this was observable, that mutation was silent: nothing
    // was listening, so a second-level split only ever appeared to work when
    // some unrelated side effect (e.g. the source pane's own collapse) happened
    // to also reassign RootPane and force a full rebuild as a side effect.
    [ObservableProperty]
    private PaneNode first = null!;

    [ObservableProperty]
    private PaneNode second = null!;

    partial void OnFirstChanged(PaneNode value) => value.Parent = this;

    partial void OnSecondChanged(PaneNode value) => value.Parent = this;

    public SplitPaneNode(Orientation orientation, PaneNode first, PaneNode second)
    {
        this.orientation = orientation;
        First = first;
        Second = second;
    }

    // Returns the sibling of `child` (the node occupying the other slot), or null
    // if `child` isn't actually one of this split's two children.
    public PaneNode? SiblingOf(PaneNode child)
    {
        if (ReferenceEquals(child, First)) return Second;
        if (ReferenceEquals(child, Second)) return First;
        return null;
    }

    // Replaces whichever slot currently holds `oldChild` with `newChild`,
    // reparenting `newChild` in the process. No-op if `oldChild` isn't a child.
    public void Replace(PaneNode oldChild, PaneNode newChild)
    {
        if (ReferenceEquals(oldChild, First)) First = newChild;
        else if (ReferenceEquals(oldChild, Second)) Second = newChild;
    }
}

public partial class PaneViewModel : PaneNode
{
    // Leaf panes are only ever created by MainViewModel's tree-mutation methods
    // (NewTab/SplitPane/initial construction), so each one carries a back-reference
    // to its owner - the same object-graph pattern as Parent above. This is how
    // PaneGroupView (bound to just this PaneViewModel via DataContext) reaches
    // MainViewModel's CloseTab/SplitPane/SetActivePane without an ambient singleton;
    // ElementName bindings back to MainWindow's RootGrid don't resolve from inside a
    // separate UserControl's own XAML namescope, so this is the mechanism instead.
    public required MainViewModel Owner { get; init; }

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private TabViewModel? selectedTab;
}
