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
        // throwaway solid Magenta - actually works). This forces alpha=255 on top of
        // whatever color the current theme's page background resolves to, so it's
        // reliably opaque while still matching the visible theme. WeakReferenceMessenger
        // registration (no explicit unregister - FolderView has no Dispose, and the
        // messenger holds this only weakly) keeps it in sync if the user switches theme.
        ApplyOpaqueFileListAreaBackground();
        WeakReferenceMessenger.Default.Register<FolderView, ThemeChangedMessage>(
            this, (recipient, _) => recipient.ApplyOpaqueFileListAreaBackground());
    }

    private void ApplyOpaqueFileListAreaBackground()
    {
        var themeColor = (Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as SolidColorBrush)?.Color
            ?? Microsoft.UI.Colors.Black;
        FileListArea.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, themeColor.R, themeColor.G, themeColor.B));
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

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Copiar aqui";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private async void FileList_Drop(object sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm || string.IsNullOrEmpty(vm.CurrentPath)) return;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var deferral = e.GetDeferral();
        try
        {
            var storageItems = await e.DataView.GetStorageItemsAsync();
            var paths = storageItems.Select(i => i.Path).ToList();
            if (paths.Count == 0) return;

            var conflicts = vm.PeekDropConflicts(paths);
            var resolution = conflicts.Count == 0
                ? ConflictResolution.FailOnConflict
                : await ResolveConflictsAsync(conflicts);
            if (resolution is null) return;

            await vm.CopyIntoAsync(paths, resolution.Value);
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
