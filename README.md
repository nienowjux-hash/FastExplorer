# FastExplorer

A lightweight, tabbed file explorer for Windows, built with WinUI 3 and .NET 8.

FastExplorer avoids expensive Windows Shell calls (like `SHGetFileInfo` icon
extraction) in favor of a static icon map, and streams search results instead
of blocking on a full directory walk, aiming to stay responsive on large
folders and slow drives.

## Features

- Multi-tab browsing with back/forward/up navigation and breadcrumbs
- Favorites sidebar, persisted to `%LocalAppData%\FastExplorer\favorites.json`
- Preview pane for images, text files, and generic file info
- Search by file name or file content
- Multi-select file operations: cut, copy, paste, delete (Recycle Bin), rename

## Requirements

- Windows 10 (10.0.19041.0) or later
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows App SDK tooling (restored automatically via NuGet)

## Building

```powershell
dotnet build FastExplorer.csproj
```

## Running tests

```powershell
dotnet test
```

## Project layout

```
FastExplorer/
├── Models/       # FileSystemItem, icon glyph mapping
├── Services/     # File system, search, favorites, recycle bin (static, testable)
├── ViewModels/   # MVVM view models (CommunityToolkit.Mvvm)
├── Views/        # Per-tab folder browser UI
├── Controls/     # Preview pane control
└── Messaging/    # Cross-component messages (WeakReferenceMessenger)
```

## License

[MIT](LICENSE)
