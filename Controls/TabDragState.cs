using FastExplorer.ViewModels;

namespace FastExplorer.Controls;

// In-proc state for a tab currently being dragged between panes. Simpler than
// round-tripping the TabViewModel/PaneViewModel references through the drag
// operation's DataPackage (which is meant for serializable formats); a small
// marker is still placed in the DataPackage itself (see PaneGroupView) so
// DragOver handlers elsewhere (e.g. FolderView's own OS file-drop handling)
// can cheaply tell "a FastExplorer tab is being dragged" apart from an
// ordinary file drag without reaching into this class.
//
// DragStarted/DragEnded let every live PaneGroupView toggle its own drop-zone
// overlay in sync, since only one tab can be mid-drag at a time app-wide.
internal static class TabDragState
{
    public static TabViewModel? Tab { get; private set; }
    public static PaneViewModel? SourcePane { get; private set; }

    public static event Action? DragStarted;
    public static event Action? DragEnded;

    public static void Start(TabViewModel tab, PaneViewModel sourcePane)
    {
        Tab = tab;
        SourcePane = sourcePane;
        DragStarted?.Invoke();
    }

    public static void End()
    {
        Tab = null;
        SourcePane = null;
        DragEnded?.Invoke();
    }
}
