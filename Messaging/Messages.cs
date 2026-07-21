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
