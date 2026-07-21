# FastExplorer

A lightweight, tabbed file explorer for Windows, built with WinUI 3 and .NET 8.

FastExplorer avoids expensive Windows Shell calls (like `SHGetFileInfo` icon
extraction) in favor of a static, colored icon map, probes drives in parallel
instead of one at a time, and streams search results instead of blocking on a
full directory walk — aiming to stay fast and responsive even on large folders,
slow drives, or unreachable network shares.

## Features

- Multi-tab browsing with back/forward/up navigation (including mouse
  back/forward buttons), breadcrumbs, and an editable address bar
- List view and large-icons/grid view, with sortable columns and real image
  thumbnails (loaded lazily, only for visible rows)
- Favorites sidebar (persisted) with automatic OneDrive detection, and cloud
  sync status icons (placeholder/pinned) read from local file attributes
- "This PC" drive list with used/free space per drive, probed in parallel so
  one unreachable network drive can't stall the rest
- Map/disconnect network drives
- Search by file name or file content, with a "show hidden files" toggle
- Multi-select file operations: cut, copy, paste, delete (Recycle Bin),
  rename, new folder/file — all async, cancellable, and with a
  Replace/Skip/Keep-both conflict resolution dialog
- Undo (Ctrl+Z) for create/rename/move/copy/delete, including restoring
  deleted files from the Recycle Bin
- Properties dialog: size (async for folders), dates, read-only/hidden
  attributes, and a read-only view of owner + permissions
- Right-click menu augmented with real Windows Shell context menu entries
  (7-Zip, antivirus scanners, etc. show up automatically)
- Preview pane for images, text files, and generic file info
- Light/Dark/System theme plus a preset accent color picker
- Drag-and-drop from Explorer (or another tab) into the current folder

## Requirements

- Windows 10 (10.0.19041.0) or later, x64 or ARM64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows App SDK runtime/tooling (restored automatically via NuGet on build)

There's no packaged installer yet — FastExplorer is unpackaged (no MSIX) and
run from a local build.

## Installing / building from source

```powershell
git clone https://github.com/nienowjux-hash/FastExplorer.git
cd FastExplorer
dotnet build FastExplorer.csproj -c Release -p:Platform=x64
```

> The project targets `x64` and `ARM64` only (no "Any CPU") — always pass
> `-p:Platform=x64` (or `ARM64` on an ARM machine) when building or running.

Then run the built executable directly:

```powershell
.\bin\x64\Release\net8.0-windows10.0.26100.0\FastExplorer.exe
```

(Use `Debug` instead of `Release` above if you built without `-c Release`.)

## Running tests

```powershell
dotnet test FastExplorer.Tests\FastExplorer.Tests.csproj -p:Platform=x64
```

## Project layout

```
FastExplorer/
├── Models/       # FileSystemItem, icon glyph mapping, small value types
├── Services/     # File system, search, favorites, settings, recycle bin,
│                 # network drives, shell context menu, undo (static, testable)
├── ViewModels/   # MVVM view models (CommunityToolkit.Mvvm)
├── Views/        # Per-tab folder browser UI
├── Controls/     # Preview pane, Properties dialog, map-network-drive dialog
├── Converters/   # XAML value converters
├── Messaging/    # Cross-component messages (WeakReferenceMessenger)
└── FastExplorer.Tests/  # xUnit test project
```

See [CLAUDE.md](CLAUDE.md) for architecture notes and non-obvious design
decisions made throughout the project.

## License

[MIT](LICENSE)
