# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

The `.csproj` sets `Platforms=x64;ARM64` and enables MSIX tooling, so **`Configuration`/`Platform` must always be specified together** — building with the bare "Any CPU" solution platform (or omitting `-p:Platform`) fails with either `MSB4126` or an MSIX packaging error (`NETSDK1XXX ... AnyCPU`).

```powershell
# Build the app only
dotnet build FastExplorer.csproj -c Debug -p:Platform=x64

# Build app + test project together (the .sln already maps its "Any CPU" solution
# platform to x64 per-project, so no -p:Platform flag is needed here)
dotnet build FastExplorer.sln -c Debug

# Run all tests
dotnet test FastExplorer.Tests\FastExplorer.Tests.csproj -p:Platform=x64

# Run a single test
dotnet test FastExplorer.Tests\FastExplorer.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~ClassName.MethodName"

# Run the built app directly (dotnet run resolves the wrong output path for this
# project's platform-qualified bin folder — launch the exe instead)
.\bin\x64\Debug\net8.0-windows10.0.26100.0\FastExplorer.exe
```

If `dotnet` isn't resolving on PATH in a given shell, invoke it via its full path (`C:\Program Files\dotnet\dotnet.exe`) instead of troubleshooting PATH.

`FastExplorer.csproj` explicitly excludes `FastExplorer.Tests\` from its own `Compile`/`None` globs (SDK-style projects otherwise auto-glob the sibling test folder's `.cs` files into the app). If a new top-level folder is ever added alongside the test project, apply the same exclusion pattern.

## Architecture

**MVVM via CommunityToolkit.Mvvm source generators** (`[ObservableProperty]`, `[RelayCommand]`). `TabViewModel` is the workhorse — one instance per tab, owning navigation history, search, sort state, and all file operations. `MainViewModel` owns the tab collection and the favorites/theme state shown in the sidebar.

**View-to-view communication goes through `WeakReferenceMessenger`, never direct references.** `MainWindow` used to expose a `public static MainWindow.Instance` that `FolderView` reached into directly; that was removed in favor of messages defined in `Messaging/Messages.cs` (`PreviewRequestedMessage`, `AddFavoriteRequestedMessage`, `ThemeChangedMessage`). `MainWindow` registers handlers in its constructor and unregisters on `Closed`. Any new cross-view interaction should follow this pattern rather than reintroducing a singleton.

**File operations (delete/paste/drop) share one pipeline in `TabViewModel`:** `TransferAsync` runs on a background thread via `Task.Run`, accepts a `CancellationToken` (wired to the status bar's Cancel button, shared with `_operationCts`), and takes a `ConflictResolution` (`FailOnConflict` / `Replace` / `Skip` / `KeepBoth`). Conflict detection (`PeekPasteConflicts`/`PeekDropConflicts`) happens synchronously before the transfer starts; `FolderView` code-behind is what actually prompts the user via `ResolveConflictsAsync` (a `ContentDialog`) — the ViewModel never shows UI itself. `Paste`, `CopyIntoAsync` (drag-drop), and `DeleteSelectedAsync` all route through this same helper; add new bulk file operations here rather than duplicating the cancellation/conflict logic.

**"This PC" (drives list) is probed in parallel, not sequentially.** `FileSystemService.GetDrivesAsync` fires one `Task.Run` per `DriveInfo` with an independent 2-second timeout each (`DriveProbeTimeout`), streaming each resolved drive back via an `Action<FileSystemItem>` callback as soon as it's ready — a single unreachable network share or empty optical drive no longer stalls (or serially delays) the rest of the list. `TabViewModel.LoadDrivesAsync` is the caller; it guards against a stale drive arriving after the user has already navigated into a folder by checking `CurrentPath` is still empty before appending.

**`TabViewModel.IsAtDrivesRoot` (`CurrentPath` is empty) drives contextual UI in `FolderView.xaml`** — the address bar, search bar, and "New folder" button are only visible when actually inside a folder, via `Converters/BoolToVisibilityConverter` with `ConverterParameter=Invert`.

**`Services/*` are stateless static classes** (`FileSystemService`, `SearchService`, `FavoritesService`, `SettingsService`, `PropertiesService`, `NetworkDriveService`, `RecycleBinHelper`) and are the layer to unit test — `FastExplorer.Tests` covers them directly rather than the ViewModels/Views. `RecycleBinHelper` wraps `IFileOperation` via raw COM interop (not the legacy `SHFileOperationW`); its test (`FileSystemServiceTests.Delete_ToRecycleBin_ItemAppearsInRecycleBin`) independently verifies the deleted file actually lands in the real Recycle Bin using `Shell.Application` COM automation, separate from the interop under test — don't remove that verification if touching `RecycleBinHelper`, since a COM vtable/HRESULT mistake there fails silently otherwise.

**Cloud-sync status (OneDrive "Files On-Demand") is read from raw `FileAttributes` bits**, not any cloud API: `FileSystemService.ToItem` checks `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS` (`0x400000`, placeholder/not downloaded) and `FILE_ATTRIBUTE_PINNED` (`0x80000`, always-available) directly on the raw attribute int, since `System.IO.FileAttributes` doesn't expose named members for them. `FileSystemItem.CloudGlyph`/`HasCloudStatus` drive the small overlay icon in `FolderView`'s item template.

**Segoe Fluent Icons glyphs are always built from raw codepoints** (`((char)0xE753).ToString()`), never typed as literal PUA characters in source — see the comments in `Models/IconGlyphMap.cs` and `Models/FileSystemItem.cs`. Editing a glyph as a literal character in a source file risks silent encoding corruption; add new glyphs the same way.

**Theme (`AppTheme`: System/Light/Dark)** is persisted via `SettingsService` (JSON in `%LocalAppData%\FastExplorer\settings.json`, same convention as `FavoritesService`) and applied by setting `RootGrid.RequestedTheme` in `MainWindow`. `RootGrid` has an explicit `Background="{ThemeResource ApplicationPageBackgroundThemeBrush}"` — without it the window's canvas stays black regardless of theme (only controls with their own opaque fill render; plain `TextBlock`s become invisible dark-on-black in Light mode). Don't remove that background.

**The live window icon and the `.exe`'s own icon are set independently.** `ApplicationIcon` in `FastExplorer.csproj` only covers the compiled `.exe` resource (File Explorer, shortcuts); the actually-running window's taskbar icon requires `AppWindow.SetIcon(...)` called explicitly in `MainWindow`'s constructor (`ApplyIcon()`), since this is an unpackaged WinUI 3 app with no packaging manifest to source it from otherwise.

**Properties dialog (`Controls/PropertiesView`) permissions are read-only by design** — it displays owner + ACL entries via `PropertiesService.GetAccessInfo` but never modifies them. The only mutation it performs is toggling Read-only/Hidden `FileAttributes` (`PropertiesService.SetAttributes`), applied from `FolderView.ShowPropertiesAsync` only when the dialog's primary button is pressed and the item isn't a drive.

**`Services/ShellContextMenu.cs` is the riskiest interop in this codebase** — it queries the real Windows Shell's `IContextMenu` (via `IShellFolder.GetUIObjectOf`) so third-party shell extensions (7-Zip, antivirus scanners, "Open in ..." tools) appear in our own context menu instead of us reimplementing each one. It's deliberately scoped down from what a full native integration would do:
- Single-item selection only (`FolderView.AddShellExtensionItems` bails if more than one item is selected) — no PIDL-array marshaling for multi-select.
- Text-only recreation — menu items are read via `GetMenuItemInfoW`/walked recursively for submenus and rebuilt as our own `MenuFlyoutItem`/`MenuFlyoutSubItem` tree (inserted into `FolderView.xaml`'s `MenuFlyout` at the `ShellExtensionsAnchor` separator, cleaned up on `Closed`), rather than displaying the shell's native `HMENU` directly via `TrackPopupMenuEx` — no owner-drawn icons/bitmaps, no `IContextMenu2`/`IContextMenu3` message forwarding.
- Standard verbs we already implement ourselves (open/cut/copy/paste/delete/rename/properties) are filtered out via `GetCommandString(GCS_VERBW)` so the shell's version never shadows our own async/cancellable/conflict-resolved file-op pipeline — anything without a resolvable verb string (i.e. almost all third-party extension entries) passes through.
- Every COM/P-Invoke call is wrapped defensively (`ShellContextMenu.TryCreate` returns `null` on any `COMException`/etc.) so a misbehaving extension degrades to "no extra items," never a crash. `ShellContextMenu.Dispose()` is idempotent (guarded by `_disposed`) since both `Opening` and `Closed` handlers can trigger cleanup.
- The `IShellFolder`/`IContextMenu` interface declarations must match the native vtable order *exactly*, including methods this code never calls (e.g. `GetDisplayNameOf`) — those are declared with dummy-but-syntactically-valid signatures purely to occupy the correct vtable slot. Getting the order or a struct's field layout wrong causes memory corruption (`AccessViolationException`, uncatchable), not a clean .NET exception, so verify any change here against a real `shobjidl_core.h`/MSDN reference and re-run `ShellContextMenuTests` before trusting it. That test suite deliberately exercises this against real files/folders on the host machine.

**Per-app settings (theme + show-hidden-files) live in one `settings.json`** via `SettingsService`'s private `SettingsData`/`LoadData`/`SaveData` read-modify-write helpers — adding a new persisted setting means adding a field there plus typed `LoadX`/`SaveX` wrappers, never a separate file, so existing settings aren't clobbered.

**Cross-tab settings changes broadcast via messages, not a shared static.** `MainViewModel.ShowHiddenFiles` sends `ShowHiddenFilesChangedMessage` on change; each `TabViewModel` registers for it in its constructor (and unregisters in `Dispose`) and calls `Refresh()` to re-enumerate with the new flag. `FileSystemService.EnumerateDirectory`'s `includeHidden` parameter controls this via `EnumerationOptions.AttributesToSkip`. This mirrors the existing `ThemeChangedMessage` pattern rather than introducing a new communication style.

**Accent color recolors WinUI's built-in `SystemAccentColor*` resources, not our own controls.** `AccentColorPalette` holds the 8 base colors and computes the Light1-3/Dark1-3 tints WinUI's default control templates expect (ListView selection highlight, CheckBox/ComboBox focus states, etc. all key off these). `MainWindow.ApplyAccentColor` sets all seven resources on `Application.Current.Resources` and then briefly flips `RootGrid.RequestedTheme` to its opposite and back — existing controls don't repaint just because a resource *value* changed, and this forces WinUI to re-evaluate `ThemeResource` lookups. Persisted/broadcast the same way as theme (`SettingsService.LoadAccentColor`/`SaveAccentColor`, `AccentColorChangedMessage`).

**`x:Bind` with an inline `Converter=` inside a `DataTemplate` fails to compile when the containing XAML file's code-behind is a `Window`** (not a `Page`/`UserControl`/other `FrameworkElement`) — the compiler generates `bindings.SetConverterLookupRoot(this)`, and `this` (the `Window`) doesn't satisfy the `FrameworkElement` parameter, so it's a hard compile error (`CS1503`), not a runtime issue. Hit this in `MainWindow.xaml`'s accent-color `ComboBox.ItemTemplate`; the fix was switching that one template from `x:Bind`/`x:DataType` to classic `{Binding}` (which has no such restriction). `FolderView.xaml` and other `UserControl`-rooted views don't have this problem and should keep using `x:Bind` as usual - this only bites templates declared directly inside `MainWindow.xaml`.

**Image thumbnails load lazily per realized row, never per folder.** `FileSystemItem` inherits `ObservableObject` specifically so its `Thumbnail` property (nullable `BitmapImage`, absent everywhere else in the model) can notify the UI once decoded. The actual load is triggered by `FolderView.FileList_ContainerContentChanging` — WinUI's own "container about to show a new/recycled item" virtualization callback — via `args.RegisterUpdateCallback`, and `ThumbnailService.LoadThumbnailAsync` decodes at `DecodePixelWidth = 32` so even a multi-megapixel photo is cheap. This only ever runs for currently-visible image rows; it must not be moved into `TabViewModel.LoadFolder` or any other eager, whole-folder path.

**List vs. large-icons view is a runtime template/panel swap on a single `ListView`, not two separate controls.** `FolderView.xaml` defines `ListItemTemplate`/`LargeIconItemTemplate` and `ListItemsPanel`/`GridItemsPanel` as named `UserControl.Resources`; `ApplyViewMode` swaps `FileList.ItemTemplate`/`ItemsPanel` and toggles the `ListHeader` (sort-header row) visibility. `TabViewModel.ViewMode` is per-tab and in-memory only (no persistence, resets to `List` on a new tab) — if that's ever meant to persist, thread it through `SettingsService` like `ShowHiddenFiles`.

**Undo is a single app-wide stack (`Services/UndoService.cs`), not per-tab**, mirroring how Explorer's undo history is shared across the whole process. Every mutating `TabViewModel` operation (`NewFolder`, `NewFile`, `RenameAsync`, `DeleteSelectedAsync`, and `TransferAsync`'s callers `PasteAsync`/`CopyIntoAsync`) pushes a matching `UndoAction` (`CreateItemUndoAction`, `RenameUndoAction`, `DeleteUndoAction`, `MoveUndoAction`, `CopyUndoAction`) after it succeeds, and `TabViewModel.UndoCommand` (Ctrl+Z) pops and reverses the most recent one on a background thread. `DeleteUndoAction.Undo()` restores from the Recycle Bin via `RecycleBinService.TryRestore`, which uses **late-bound** `Shell.Application` COM automation (`Type.GetTypeFromProgID` + `InvokeMember`, matching `ShellContextMenuTests`'s verification style) rather than the strictly-typed vtable interop in `ShellContextMenu.cs` — a wrong property/verb name here just fails to find/restore the item, it can't corrupt memory the way a vtable mistake could. `RecycleBinServiceTests.TryRestore_AfterDelete_BringsFileBack` is the regression test for this; it was empirically verified to work before being wired into the undo stack, not assumed.
