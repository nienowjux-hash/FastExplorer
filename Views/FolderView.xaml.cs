using CommunityToolkit.Mvvm.Messaging;
using FastExplorer.Controls;
using FastExplorer.Messaging;
using FastExplorer.Models;
using FastExplorer.Services;
using FastExplorer.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace FastExplorer.Views;

public sealed partial class FolderView : UserControl
{
    private TabViewModel? ViewModel => DataContext as TabViewModel;

    public FolderView()
    {
        InitializeComponent();
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
            vm.StatusText = $"Path not found: {path}";
        }
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
        }
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Copy here";
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
            Title = "Item already exists",
            Content = $"{conflictingNames.Count} item(s) already exist in this folder:\n{string.Join(", ", conflictingNames)}",
            PrimaryButtonText = "Replace",
            SecondaryButtonText = "Keep both",
            CloseButtonText = "Skip",
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
            Title = "Rename",
            Content = textBox,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
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
            Title = "Properties",
            Content = propertiesView,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
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
                ViewModel.StatusText = $"Couldn't update attributes: {ex.Message}";
            }
        }
    }
}
