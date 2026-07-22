namespace FastExplorer.Controls;

// Where a dragged tab was released relative to the target pane: Center moves
// the tab into that pane's own tab strip; the four edges split the pane in
// that direction, with the dragged tab landing in the newly created half.
public enum DockZone
{
    Center,
    Top,
    Bottom,
    Left,
    Right,
}
