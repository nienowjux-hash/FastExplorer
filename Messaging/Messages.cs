using FastExplorer.Models;

namespace FastExplorer.Messaging;

public sealed record PreviewRequestedMessage(FileSystemItem? Item);

public sealed record AddFavoriteRequestedMessage(FileSystemItem Item);

public sealed record ThemeChangedMessage(AppTheme Theme);

public sealed record ShowHiddenFilesChangedMessage(bool ShowHiddenFiles);

public sealed record AccentColorChangedMessage(AccentColor AccentColor);

// Sent whenever the shared cut/copy clipboard changes, so every open tab (not just
// the one that triggered it) can re-mark which of its visible items are cut.
public sealed record ClipboardChangedMessage;

// FolderView has no direct reference to the PaneViewModel/MainViewModel that own
// its tab - it only knows its own TabViewModel via DataContext - so "open in new
// tab" from the context menu has to go through the messenger, like every other
// cross-view interaction in this app, rather than reaching upward directly.
public sealed record OpenInNewTabRequestedMessage(string Path);
