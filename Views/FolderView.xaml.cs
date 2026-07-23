using CommunityToolkit.Mvvm.Messaging;
using FastExplorer.Controls;
using FastExplorer.Messaging;
using FastExplorer.Models;
using FastExplorer.Services;
using FastExplorer.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace FastExplorer.Views;

public sealed partial class FolderView : UserControl
{
    private TabViewModel? ViewModel => DataContext as TabViewModel;

    // Type-ahead ("press a letter, jump to the first item starting with it") state -
    // see FileList_CharacterReceived.
    private string _typeAheadPrefix = string.Empty;
    private DateTime _typeAheadLastKeystroke;
    private static readonly TimeSpan TypeAheadResetInterval = TimeSpan.FromSeconds(1);

    public FolderView()
    {
        InitializeComponent();

        // FileListArea's context menu and file-drop handling are wired here with
        // handledEventsToo:true, not as plain XAML attributes or a declarative
        // Grid.ContextFlyout - ListView's own built-in gesture handling (tied to
        // CanDragItems/item selection) can mark RightTapped/DragOver/Drop Handled
        // before they'd otherwise bubble from FileList up to this wrapper Grid,
        // especially over an empty or blank list area, which silently ate both the
        // context menu and file drops there.
        FileListArea.AddHandler(RightTappedEvent, new RightTappedEventHandler(FileListArea_RightTapped), true);
        FileListArea.AddHandler(DragOverEvent, new DragEventHandler(FileList_DragOver), true);
        FileListArea.AddHandler(DropEvent, new DragEventHandler(FileList_Drop), true);

        // FileListArea.Background can't just be {ThemeResource ApplicationPageBackgroundThemeBrush}
        // in XAML - confirmed empirically that brush isn't fully opaque (alpha < 255),
        // and anything less than fully opaque silently fails to hit-test for pointer
        // input here (Transparent, alpha=1/255, and alpha=16/255 were all tried and all
        // failed; only a literal, guaranteed-alpha=255 brush - first proven with a
        // throwaway solid Magenta - actually works). ThemeChangedMessage (not
        // ActualThemeChanged - see ApplyOpaqueFileListAreaBackground for why) keeps it
        // in sync when the user switches theme.
        ApplyOpaqueFileListAreaBackground(SettingsService.LoadTheme());
        WeakReferenceMessenger.Default.Register<FolderView, ThemeChangedMessage>(
            this, (recipient, message) => recipient.ApplyOpaqueFileListAreaBackground(message.Theme));
    }

    // Hardcoded to WinUI's own known default colors (Microsoft.WindowsAppSDK.WinUI's
    // Themes/generic.xaml: ApplicationPageBackgroundThemeBrush -> SolidBackgroundFillColorBase,
    // #202020 Dark / #F3F3F3 Light) and driven by AppTheme - carried directly as data
    // in ThemeChangedMessage's payload, or read once from SettingsService at
    // construction - never derived from any resource/brush/ActualTheme lookup on this
    // element's own visual tree at runtime, after several earlier attempts at that
    // all proved unreliable in different ways:
    //   1. Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] - not
    //      scoped to this element's actual per-subtree theme (set via MainWindow's
    //      RootGrid.RequestedTheme, a per-subtree override, not an app-global setting),
    //      so it never updated after the very first theme it happened to resolve to.
    //   2. Application.Current.Resources.ThemeDictionaries[themeKey][...] - wrong
    //      assumption about where that resource is actually merged from; always fell
    //      through to a black fallback.
    //   3. Copying FolderViewRoot.Background (a live, correctly-updating {ThemeResource}
    //      XAML binding one element over), re-read on ActualThemeChanged - correct in
    //      principle, but observably stale by exactly one toggle when read inline.
    //   4. Same idea, but deferred one dispatcher tick (DispatcherQueue.TryEnqueue) to
    //      let sibling {ThemeResource} bindings finish re-resolving first - still
    //      intermittently stale.
    //   5. Matching a literal color directly off this element's own ActualTheme inside
    //      its ActualThemeChanged handler (no resource lookup at all, seemingly the
    //      most bulletproof option) - *still* observed stale on a subsequent toggle.
    //      FolderView is nested inside dynamically-composed content (TabView's
    //      TabItemTemplate, itself hosted by PaneGroupView's TabView, reached via a
    //      DataTemplateSelector-driven ContentControl - see PaneNode.cs/
    //      PaneNodeTemplateSelector) rather than a plain static XAML tree, and
    //      ActualTheme/ActualThemeChanged apparently doesn't reliably propagate
    //      through that composition the way a declarative {ThemeResource} XAML
    //      binding does (which is why MainWindow's own RootGrid background - a plain
    //      XAML binding, not a C# event handler - has always updated correctly).
    // ThemeChangedMessage sidesteps all of that: it's sent explicitly and
    // deterministically from MainViewModel.OnSelectedThemeChanged exactly once per
    // theme selection, independent of whatever WinUI's internal theme-cascade
    // machinery does or doesn't do through this app's specific visual composition.
    private static readonly Windows.UI.Color DarkPageBackground = Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20);
    private static readonly Windows.UI.Color LightPageBackground = Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3);

    private void ApplyOpaqueFileListAreaBackground(AppTheme theme)
    {
        // AppTheme.System has no fixed answer here - it means "whatever Windows is
        // currently set to" - so this is the one remaining case that still asks
        // ActualTheme, but only as a last resort for that one case, not as the
        // primary signal for the explicit Light/Dark selections this bug was actually
        // about.
        var isLight = theme switch
        {
            AppTheme.Light => true,
            AppTheme.Dark => false,
            _ => ActualTheme == ElementTheme.Light,
        };
        FileListArea.Background = new SolidColorBrush(isLight ? LightPageBackground : DarkPageBackground);
    }

    private void FileListArea_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        FileContextMenu.ShowAt(FileListArea, new FlyoutShowOptions { Position = e.GetPosition(FileListArea) });
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => ViewModel?.GoBackCommand.Execute(null);
    private void ForwardButton_Click(object sender, RoutedEventArgs e) => ViewModel?.GoForwardCommand.Execute(null);
    private void UpButton_Click(object sender, RoutedEventArgs e) => ViewModel?.GoUpCommand.Execute(null);
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => ViewModel?.RefreshCommand.Execute(null);
    private void NewFolderMenuItem_Click(object sender, RoutedEventArgs e) => ViewModel?.NewFolderCommand.Execute(null);
    private void NewTextFileMenuItem_Click(object sender, RoutedEventArgs e) => ViewModel?.NewFileCommand.Execute(null);
    private void CancelOperationButton_Click(object sender, RoutedEventArgs e) => ViewModel?.CancelOperationCommand.Execute(null);

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint((UIElement)sender).Properties;
        if (props.IsXButton1Pressed && ViewModel?.CanGoBack == true) ViewModel.GoBackCommand.Execute(null);
        else if (props.IsXButton2Pressed && ViewModel?.CanGoForward == true) ViewModel.GoForwardCommand.Execute(null);
    }

    private void Breadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var button = (Button)sender;
        var index = ViewModel.Breadcrumbs.IndexOf((string)button.Tag);
        if (index < 0) return;
        var path = ViewModel.BreadcrumbPathAt(index);
        ViewModel.NavigateTo(path);
    }

    private void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || ViewModel is not { } vm) return;

        var path = AddressBox.Text.Trim();
        if (Directory.Exists(path))
        {
            vm.NavigateTo(path);
        }
        else
        {
            vm.StatusText = $"Caminho não encontrado: {path}";
        }
    }

    private const int MaxAddressSuggestions = 15;
    private int _addressSuggestionVersion;

    // Suggests sibling subfolders of whatever's already typed, e.g. "C:\Prog" ->
    // every top-level folder under C:\ starting with "Prog". Enter still commits
    // navigation via AddressBox_KeyDown above, unchanged - picking a suggestion
    // here only fills the text box, it doesn't navigate by itself.
    private async void AddressBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        var text = AddressBox.Text;
        var version = ++_addressSuggestionVersion;
        var suggestions = await Task.Run(() => GetPathSuggestions(text));
        if (version != _addressSuggestionVersion) return;

        AddressBox.ItemsSource = suggestions;
    }

    private static List<string> GetPathSuggestions(string text)
    {
        try
        {
            string parent;
            string partial;
            if (text.EndsWith(Path.DirectorySeparatorChar) || text.EndsWith(Path.AltDirectorySeparatorChar))
            {
                parent = text;
                partial = string.Empty;
            }
            else
            {
                parent = Path.GetDirectoryName(text) ?? string.Empty;
                partial = Path.GetFileName(text);
            }

            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return new List<string>();

            return Directory.EnumerateDirectories(parent)
                .Where(d => Path.GetFileName(d).StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .Take(MaxAddressSuggestions)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new List<string>();
        }
    }

    private void AddressBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string path) AddressBox.Text = path;
    }

    private void SortHeader_Click(object sender, RoutedEventArgs e)
    {
        var tag = (string)((Button)sender).Tag;
        var field = tag switch
        {
            "Size" => SortField.Size,
            "Modified" => SortField.Modified,
            _ => SortField.Name,
        };
        ViewModel?.SortByCommand.Execute(field);
    }

    // Syncs the flyout's RadioMenuFlyoutItems to the ViewModel's current sort state
    // every time it opens - the items themselves only update IsChecked in response to
    // being clicked (RadioMenuFlyoutItem's own built-in group behavior), so without
    // this they'd drift out of sync with sorting done via the column headers instead
    // (SortHeader_Click/SortByCommand), which don't touch these controls at all.
    private void SortFlyout_Opening(object sender, object e)
    {
        if (ViewModel is not { } vm) return;
        var fieldItem = vm.SortField switch
        {
            SortField.Size => SortSizeItem,
            SortField.Modified => SortModifiedItem,
            SortField.Created => SortCreatedItem,
            _ => SortNameItem,
        };
        fieldItem.IsChecked = true;
        (vm.SortDescending ? SortDescItem : SortAscItem).IsChecked = true;
    }

    private void SortFieldItem_Click(object sender, RoutedEventArgs e)
    {
        var tag = (string)((RadioMenuFlyoutItem)sender).Tag;
        var field = tag switch
        {
            "Size" => SortField.Size,
            "Modified" => SortField.Modified,
            "Created" => SortField.Created,
            _ => SortField.Name,
        };
        ViewModel?.SetSortFieldCommand.Execute(field);
    }

    private void SortDirectionItem_Click(object sender, RoutedEventArgs e)
    {
        var tag = (string)((RadioMenuFlyoutItem)sender).Tag;
        ViewModel?.SetSortDescendingCommand.Execute(tag == "Desc");
    }

    private void FilterFlyout_Opening(object sender, object e)
    {
        if (ViewModel is not { } vm) return;
        var item = vm.FilterCategory switch
        {
            FileTypeCategory.Folder => FilterFolderItem,
            FileTypeCategory.Document => FilterDocumentItem,
            FileTypeCategory.Image => FilterImageItem,
            FileTypeCategory.Audio => FilterAudioItem,
            FileTypeCategory.Video => FilterVideoItem,
            FileTypeCategory.Archive => FilterArchiveItem,
            FileTypeCategory.Executable => FilterExecutableItem,
            FileTypeCategory.Code => FilterCodeItem,
            FileTypeCategory.Font => FilterFontItem,
            FileTypeCategory.Other => FilterOtherItem,
            _ => FilterAllItem,
        };
        item.IsChecked = true;
    }

    private void FilterCategoryItem_Click(object sender, RoutedEventArgs e)
    {
        var tag = (string)((RadioMenuFlyoutItem)sender).Tag;
        var category = Enum.Parse<FileTypeCategory>(tag);
        ViewModel?.SetFilterCategoryCommand.Execute(category);
    }

    private void ViewToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        vm.ViewMode = vm.ViewMode == ViewMode.List ? ViewMode.LargeIcons : ViewMode.List;
        ApplyViewMode(vm.ViewMode);
    }

    private void ApplyViewMode(ViewMode mode)
    {
        if (mode == ViewMode.LargeIcons)
        {
            FileList.ItemsPanel = (ItemsPanelTemplate)Resources["GridItemsPanel"];
            FileList.ItemTemplate = (DataTemplate)Resources["LargeIconItemTemplate"];
            ListHeader.Visibility = Visibility.Collapsed;
        }
        else
        {
            FileList.ItemsPanel = (ItemsPanelTemplate)Resources["ListItemsPanel"];
            FileList.ItemTemplate = (DataTemplate)Resources["ListItemTemplate"];
            ListHeader.Visibility = Visibility.Visible;
        }
    }

    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel is not null)
        {
            await ViewModel.RunSearchAsync();
        }
    }

    private void FileList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is { } item)
        {
            ViewModel.OpenItem(item);
        }
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.SelectedItems.Clear();
            foreach (var selected in FileList.SelectedItems)
            {
                if (selected is FileSystemItem item) vm.SelectedItems.Add(item);
            }
        }

        var previewItem = ViewModel?.SelectedItems.Count == 1 ? ViewModel.SelectedItem : null;
        WeakReferenceMessenger.Default.Send(new PreviewRequestedMessage(previewItem));
    }

    private void FileList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: FileSystemItem item })
        {
            if (ViewModel is not null) ViewModel.SelectedItem = item;
        }
    }

    // Loads a thumbnail only for the item actually being realized by the
    // (virtualizing) ListView, and only once per item - the cheapest possible
    // version of "show real image previews" for a folder with thousands of files.
    private void FileList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not FileSystemItem item) return;
        if (item.Thumbnail is not null || !ThumbnailService.IsImage(item.Extension)) return;

        args.RegisterUpdateCallback(async (_, a) =>
        {
            if (a.Item is FileSystemItem target && target.Thumbnail is null)
            {
                target.Thumbnail = await ThumbnailService.LoadThumbnailAsync(target.FullPath);
            }
        });
    }

    private async void FileList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;

        var ctrlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        var altDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
            .HasFlag(CoreVirtualKeyStates.Down);

        switch (e.Key)
        {
            case VirtualKey.Delete:
                vm.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.F2:
                await BeginRenameAsync();
                e.Handled = true;
                break;
            case VirtualKey.Enter when altDown:
                await ShowPropertiesAsync();
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                if (vm.SelectedItem is { } item) vm.OpenItem(item);
                e.Handled = true;
                break;
            case VirtualKey.C when ctrlDown:
                vm.CopySelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.X when ctrlDown:
                vm.CutSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.V when ctrlDown:
                await PasteFromClipboardAsync();
                e.Handled = true;
                break;
            case VirtualKey.Z when ctrlDown:
                vm.UndoCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.A when ctrlDown:
                FileList.SelectAll();
                e.Handled = true;
                break;
        }
    }

    // Mirrors Explorer's "type a letter, jump to the first matching item" behavior.
    // Uses CharacterReceived (not FileList_KeyDown's VirtualKey switch above) since it
    // hands over the actual typed Unicode character - correctly handling accented
    // names (ç, ã, é...) and Shift/keyboard-layout differences without hand-mapping
    // VirtualKey.A..Z, and it doesn't fire for Ctrl-chord shortcuts (Ctrl+C etc.),
    // so there's no overlap with FileList_KeyDown's own handling of those.
    private void FileList_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        if (ViewModel is not { } vm || vm.Items.Count == 0) return;

        var typed = args.Character;
        if (char.IsControl(typed)) return;

        var now = DateTime.UtcNow;
        var withinBurst = now - _typeAheadLastKeystroke <= TypeAheadResetInterval;
        _typeAheadLastKeystroke = now;

        // "Confirmed cycling": the burst so far is two or more presses of the exact
        // same letter (e.g. "d","d") - matching Explorer/WPF ListBox convention that
        // this means "next match of that one letter", not a literal search for "dd".
        // A single keystroke is always ambiguous (could be the start of a word like
        // "do"), so this only applies from the second repeat onward - and critically,
        // a genuinely different letter arriving while already in this state RESETS
        // the prefix to just that new letter instead of appending to the old one
        // (which previously produced dead strings like "dp" that matched nothing
        // until the burst window timed out - the reported "delay when switching key").
        var confirmedCycling = withinBurst && _typeAheadPrefix.Length >= 2
            && _typeAheadPrefix.All(c => c == _typeAheadPrefix[0]);

        bool cycleToNext;
        if (!withinBurst)
        {
            _typeAheadPrefix = typed.ToString();
            cycleToNext = false;
        }
        else if (confirmedCycling)
        {
            if (typed == _typeAheadPrefix[0])
            {
                cycleToNext = true;
            }
            else
            {
                _typeAheadPrefix = typed.ToString();
                cycleToNext = false;
            }
        }
        else
        {
            _typeAheadPrefix += typed;
            cycleToNext = _typeAheadPrefix.Length >= 2 && _typeAheadPrefix.All(c => c == typed);
        }

        var currentIndex = vm.SelectedItem is { } selected ? vm.Items.IndexOf(selected) : -1;
        var startIndex = cycleToNext ? (currentIndex + 1) % vm.Items.Count : 0;
        var searchText = cycleToNext ? typed.ToString() : _typeAheadPrefix;

        for (var offset = 0; offset < vm.Items.Count; offset++)
        {
            var index = (startIndex + offset) % vm.Items.Count;
            var candidate = vm.Items[index];
            if (!candidate.Name.StartsWith(searchText, StringComparison.CurrentCultureIgnoreCase)) continue;

            FileList.SelectedItem = candidate;
            FileList.ScrollIntoView(candidate);
            args.Handled = true;
            return;
        }
    }

    // Ctrl+C/X/V etc. are handled by FileList_KeyDown above, which only fires once
    // the ListView itself has keyboard focus. Opening a folder (new tab, double-click,
    // address bar) doesn't automatically move focus there, so give it focus as soon as
    // it's loaded - a one-time, essentially free call, not something that runs per item.
    private void FileList_Loaded(object sender, RoutedEventArgs e) => FileList.Focus(FocusState.Programmatic);

    // Lets a file be dragged out of this list - onto another pane's file list (to
    // copy it there, via that pane's own FileList_Drop) or, in principle, onto any
    // other app that accepts StorageItems. Resolving paths to real IStorageItems is
    // inherently async, and DragItemsStartingEventArgs offers no way to await
    // inline, so the actual work happens in a SetDataProvider callback (deferred
    // until a drop target asks for it) rather than here.
    private void FileList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var paths = e.Items.OfType<FileSystemItem>()
            .Where(i => i.Kind != FileSystemItemKind.Drive)
            .Select(i => i.FullPath)
            .ToList();
        if (paths.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var items = new List<IStorageItem>();
                foreach (var path in paths)
                {
                    if (Directory.Exists(path)) items.Add(await StorageFolder.GetFolderFromPathAsync(path));
                    else if (File.Exists(path)) items.Add(await StorageFile.GetFileFromPathAsync(path));
                }
                request.SetData(items);
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    // AllowDrop is only set on FileListArea (the whole list's wrapping container, not
    // each row - see CLAUDE.md), so e.OriginalSource on a drag/drop event is always
    // FileListArea itself, never the specific row under the pointer; a DataContext
    // check on it can't find a target folder (this is why the first version of this
    // silently never worked). Position-based hit-testing against FileList's own visual
    // tree is what actually finds the row: walk every element at the drop point,
    // outermost-first, until one whose DataContext is a directory item turns up.
    private FileSystemItem? GetDropTargetFolder(DragEventArgs e)
    {
        // FindElementsInHostCoordinates wants the point in the app's host/window
        // coordinate space, not relative to FileList - GetPosition(null) is what
        // returns that (same convention as PointerRoutedEventArgs.GetCurrentPoint(null)).
        // Passing GetPosition(FileList) here (element-relative) was the actual reason
        // this still didn't work after the OriginalSource fix: the point was in the
        // wrong coordinate space entirely, so hit-testing never landed on anything real.
        var point = e.GetPosition(null);
        var elements = VisualTreeHelper.FindElementsInHostCoordinates(point, FileList, includeAllElements: true);
        foreach (var element in elements)
        {
            if (element is FrameworkElement { DataContext: FileSystemItem { IsDirectory: true } item }) return item;
        }
        return null;
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        var targetFolder = GetDropTargetFolder(e);
        e.DragUIOverride.Caption = targetFolder is { } folder ? $"Copiar para \"{folder.Name}\"" : "Copiar aqui";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void FileList_Drop(object sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm || string.IsNullOrEmpty(vm.CurrentPath)) return;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        // Captured now, before the first await - the pointer position this reads is
        // only meaningful synchronously during the event, not after awaiting.
        var targetFolder = GetDropTargetFolder(e);

        var deferral = e.GetDeferral();
        try
        {
            var storageItems = await e.DataView.GetStorageItemsAsync();
            var paths = storageItems.Select(i => i.Path).ToList();
            if (paths.Count == 0) return;

            // Dropping a file back onto the folder it's already directly in isn't a
            // real conflict to prompt about - just a no-op, same as dropping it on
            // empty space in that same folder would already have been treated (this
            // wasn't reachable before drops could target a specific row).
            var destination = targetFolder?.FullPath ?? vm.CurrentPath;
            paths = paths.Where(p => !string.Equals(Path.GetDirectoryName(p), destination, StringComparison.OrdinalIgnoreCase)).ToList();
            if (paths.Count == 0) return;

            var conflicts = vm.PeekDropConflicts(paths, targetFolder?.FullPath);
            var resolution = conflicts.Count == 0
                ? ConflictResolution.FailOnConflict
                : await ResolveConflictsAsync(conflicts);
            if (resolution is null) return;

            await vm.CopyIntoAsync(paths, resolution.Value, targetFolder?.FullPath);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ContextOpen_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is { } item) ViewModel.OpenItem(item);
    }

    // The active pane (which this click already made active, via PaneGroupView's
    // handledEventsToo pointer hook) is resolved on the receiving end - FolderView
    // itself has no reference to its owning pane, only WeakReferenceMessenger does.
    private void ContextOpenInNewTab_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is { IsDirectory: true } item)
        {
            WeakReferenceMessenger.Default.Send(new OpenInNewTabRequestedMessage(item.FullPath));
        }
    }

    private async void ContextAnalyzeDiskUsage_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is { IsDirectory: true } item) await ShowDiskUsageAsync(item.FullPath);
    }

    private async void DiskUsageButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && !string.IsNullOrEmpty(vm.CurrentPath)) await ShowDiskUsageAsync(vm.CurrentPath);
    }

    private async Task ShowDiskUsageAsync(string path)
    {
        var dialog = new ContentDialog
        {
            Title = "Analisar espaço em disco",
            Content = new DiskUsageView(path),
            CloseButtonText = "Fechar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async void ContextFindDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is { IsDirectory: true } item) await ShowDuplicatesAsync(item.FullPath);
    }

    private async void DuplicatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && !string.IsNullOrEmpty(vm.CurrentPath)) await ShowDuplicatesAsync(vm.CurrentPath);
    }

    private async Task ShowDuplicatesAsync(string path)
    {
        var dialog = new ContentDialog
        {
            Title = "Buscar arquivos duplicados",
            Content = new DuplicatesView(path),
            CloseButtonText = "Fechar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void ContextCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is not { } item) return;
        var package = new DataPackage();
        package.SetText(item.FullPath);
        Clipboard.SetContent(package);
    }

    private void ContextCut_Click(object sender, RoutedEventArgs e) => ViewModel?.CutSelectedCommand.Execute(null);
    private void ContextCopy_Click(object sender, RoutedEventArgs e) => ViewModel?.CopySelectedCommand.Execute(null);
    private async void ContextPaste_Click(object sender, RoutedEventArgs e) => await PasteFromClipboardAsync();
    private void ContextDelete_Click(object sender, RoutedEventArgs e) => ViewModel?.DeleteSelectedCommand.Execute(null);
    private async void ContextRename_Click(object sender, RoutedEventArgs e) => await BeginRenameAsync();
    private void ContextOrganize_Click(object sender, RoutedEventArgs e) => ViewModel?.OrganizeSelectedCommand.Execute(null);

    private async Task PasteFromClipboardAsync()
    {
        if (ViewModel is not { } vm) return;

        var conflicts = vm.PeekPasteConflicts();
        var resolution = conflicts.Count == 0
            ? ConflictResolution.FailOnConflict
            : await ResolveConflictsAsync(conflicts);
        if (resolution is null) return;

        await vm.PasteAsync(resolution.Value);
    }

    private async Task<ConflictResolution?> ResolveConflictsAsync(IReadOnlyList<string> conflictingNames)
    {
        var dialog = new ContentDialog
        {
            Title = "Item já existe",
            Content = $"{conflictingNames.Count} item(ns) já existem nesta pasta:\n{string.Join(", ", conflictingNames)}",
            PrimaryButtonText = "Substituir",
            SecondaryButtonText = "Manter os dois",
            CloseButtonText = "Ignorar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => ConflictResolution.Replace,
            ContentDialogResult.Secondary => ConflictResolution.KeepBoth,
            ContentDialogResult.None => null, // dialog dismissed (e.g. Esc)
            _ => ConflictResolution.Skip,
        };
    }

    private async Task BeginRenameAsync()
    {
        if (ViewModel?.SelectedItem is not { } item) return;

        var textBox = new TextBox { Text = item.Name, SelectionStart = 0 };
        var dialog = new ContentDialog
        {
            Title = "Renomear",
            Content = textBox,
            PrimaryButtonText = "Renomear",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            await ViewModel!.RenameAsync(item, textBox.Text.Trim());
        }
    }

    private void ContextAddFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is { } item)
        {
            WeakReferenceMessenger.Default.Send(new AddFavoriteRequestedMessage(item));
        }
    }

    // Mirrors TabViewModel.GetOperationTargets' single-vs-multi fallback (that one's
    // private to the ViewModel) for the context-menu actions that live here in
    // code-behind instead: tags and the image batch dialog both need "the selection, or
    // the single right-clicked item if nothing's multi-selected."
    private List<FileSystemItem> GetContextTargets()
    {
        if (ViewModel is not { } vm) return new List<FileSystemItem>();
        if (vm.SelectedItems.Count > 0) return vm.SelectedItems.ToList();
        return vm.SelectedItem is { } item ? new List<FileSystemItem> { item } : new List<FileSystemItem>();
    }

    // Preset Tag is "colorHex;label" - both applied together.
    private void TagPresetItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string tag }) return;
        var parts = tag.Split(';', 2);
        if (parts.Length != 2) return;
        ApplyTag(parts[0], parts[1]);
    }

    private async void TagCustomItem_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetContextTargets();
        if (targets.Count == 0) return;

        // Prefill from the first selected item's current tag, if any, so editing an
        // already-tagged item starts from what it already has instead of a blank form.
        var first = targets[0];
        var editView = new TagEditView(first.TagLabel, first.TagColorHex);
        var dialog = new ContentDialog
        {
            Title = "Personalizar etiqueta",
            Content = editView,
            PrimaryButtonText = "Salvar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        ApplyTag(editView.ColorHex, editView.Label);
    }

    private void RemoveTagItem_Click(object sender, RoutedEventArgs e) => ApplyTag(null, null);

    // Writes through FileTagService and updates each already-loaded FileSystemItem in
    // place so the colored dot (and its tooltip) in the list update immediately - no
    // need to reload the folder just to pick up a tag change.
    private void ApplyTag(string? colorHex, string? label)
    {
        foreach (var target in GetContextTargets())
        {
            FileTagService.SetTag(target.FullPath, colorHex, label);
            target.TagColorHex = colorHex;
            target.TagLabel = colorHex is null ? null : label;
        }
    }

    private async void ContextResizeImages_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetContextTargets();
        if (targets.Count == 0) return;

        var parentFolder = Path.GetDirectoryName(targets[0].FullPath);
        if (string.IsNullOrEmpty(parentFolder)) return;

        var dialog = new ContentDialog
        {
            Title = "Redimensionar/converter imagens",
            Content = new ImageBatchView(targets.Select(t => t.FullPath).ToList(), parentFolder),
            CloseButtonText = "Fechar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private ShellContextMenu? _shellContextMenu;
    private readonly List<MenuFlyoutItemBase> _dynamicShellItems = new();

    private void ContextFlyout_Opening(object sender, object e)
    {
        DisconnectDriveItem.Visibility = ViewModel?.SelectedItem?.IsNetworkDrive == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        OpenInNewTabItem.Visibility = ViewModel?.SelectedItem?.IsDirectory == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        AnalyzeDiskUsageItem.Visibility = ViewModel?.SelectedItem?.IsDirectory == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        FindDuplicatesItem.Visibility = ViewModel?.SelectedItem?.IsDirectory == true
            ? Visibility.Visible
            : Visibility.Collapsed;

        var contextTargets = GetContextTargets();
        ResizeImagesItem.Visibility = contextTargets.Count > 0 && contextTargets.All(t => !t.IsDirectory && t.Category == FileTypeCategory.Image)
            ? Visibility.Visible
            : Visibility.Collapsed;

        AddShellExtensionItems();
    }

    private void ContextFlyout_Closed(object sender, object e)
    {
        foreach (var item in _dynamicShellItems)
        {
            FileContextMenu.Items.Remove(item);
        }
        _dynamicShellItems.Clear();

        _shellContextMenu?.Dispose();
        _shellContextMenu = null;
    }

    // Augments our own menu with whatever the Shell's native IContextMenu reports
    // for the selected item (third-party extensions like archivers, antivirus
    // scanners, "Open in ..." tools). Scoped to a single non-drive selection; any
    // failure just means no extra items, never a broken menu.
    private void AddShellExtensionItems()
    {
        if (ViewModel is not { } vm || vm.SelectedItems.Count > 1) return;
        if (vm.SelectedItem is not { Kind: not FileSystemItemKind.Drive } item) return;

        var folder = Path.GetDirectoryName(item.FullPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(folder)) return;

        var menu = ShellContextMenu.TryCreate(folder, Path.GetFileName(item.FullPath), IntPtr.Zero);
        if (menu is null) return;

        _shellContextMenu = menu;

        var insertAt = FileContextMenu.Items.IndexOf(ShellExtensionsAnchor);
        if (insertAt < 0) return;

        var separator = new MenuFlyoutSeparator();
        FileContextMenu.Items.Insert(insertAt, separator);
        _dynamicShellItems.Add(separator);
        insertAt++;

        foreach (var entry in menu.Entries)
        {
            var menuItem = BuildShellMenuItem(entry);
            FileContextMenu.Items.Insert(insertAt, menuItem);
            _dynamicShellItems.Add(menuItem);
            insertAt++;
        }
    }

    private MenuFlyoutItemBase BuildShellMenuItem(ShellMenuEntry entry)
    {
        if (entry.SubItems is { Count: > 0 } subItems)
        {
            var subItem = new MenuFlyoutSubItem { Text = entry.Label };
            foreach (var child in subItems)
            {
                subItem.Items.Add(BuildShellMenuItem(child));
            }
            return subItem;
        }

        var menuItem = new MenuFlyoutItem { Text = entry.Label, IsEnabled = entry.IsEnabled };
        menuItem.Click += (_, _) => _shellContextMenu?.Invoke(entry, IntPtr.Zero);
        return menuItem;
    }

    private void ContextDisconnectDrive_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is not { IsNetworkDrive: true } item) return;

        try
        {
            NetworkDriveService.DisconnectDrive(item.FullPath.TrimEnd('\\'));
            ViewModel.RefreshCommand.Execute(null);
        }
        catch (IOException ex)
        {
            ViewModel.StatusText = ex.Message;
        }
    }

    private async void ContextProperties_Click(object sender, RoutedEventArgs e) => await ShowPropertiesAsync();

    private async Task ShowPropertiesAsync()
    {
        if (ViewModel?.SelectedItem is not { } item) return;

        var propertiesView = new PropertiesView(item);
        var dialog = new ContentDialog
        {
            Title = "Propriedades",
            Content = propertiesView,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && item.Kind != FileSystemItemKind.Drive)
        {
            try
            {
                PropertiesService.SetAttributes(item.FullPath, propertiesView.ReadOnlyChecked, propertiesView.HiddenChecked);
                ViewModel.RefreshCommand.Execute(null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.StatusText = $"Não foi possível atualizar os atributos: {ex.Message}";
            }
        }
    }
}
