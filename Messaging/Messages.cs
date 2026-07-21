using FastExplorer.Models;

namespace FastExplorer.Messaging;

public sealed record PreviewRequestedMessage(FileSystemItem? Item);

public sealed record AddFavoriteRequestedMessage(FileSystemItem Item);

public sealed record ThemeChangedMessage(AppTheme Theme);

public sealed record ShowHiddenFilesChangedMessage(bool ShowHiddenFiles);

public sealed record AccentColorChangedMessage(AccentColor AccentColor);
