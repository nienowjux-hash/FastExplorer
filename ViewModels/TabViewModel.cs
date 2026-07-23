using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FastExplorer.Messaging;
using FastExplorer.Models;
using FastExplorer.Services;
using Microsoft.UI.Xaml.Media;

namespace FastExplorer.ViewModels;

public partial class TabViewModel : ObservableObject, IDisposable
{
    // Shared across every tab in the app, mirroring how a single system clipboard
    // works in Explorer - copy in one tab, paste in another.
    private static List<string> s_clipboard = new();
    private static bool s_clipboardIsCut;

    private readonly List<string> _backStack = new();
    private readonly List<string> _forwardStack = new();
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _operationCts;
    private FileSystemWatcher? _watcher;
    private Timer? _refreshDebounceTimer;
    private Timer? _searchDebounceTimer;
    private bool _showHiddenFiles;

    [ObservableProperty]
    private string currentPath = string.Empty;

    [ObservableProperty]
    private string header = "Este Computador";

    [ObservableProperty]
    private FileSystemItem? selectedItem;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private bool isSearchingContent;

    [ObservableProperty]
    private bool isBusy;

    // Distinct from IsBusy (which also covers search): only true while a
    // TransferAsync/DeleteSelectedAsync bulk operation is running, so the status
    // bar's determinate ProgressBar doesn't show a stuck-at-zero bar during search.
    [ObservableProperty]
    private bool isTransferring;

    [ObservableProperty]
    private double transferProgress;

    [ObservableProperty]
    private SortField sortField = SortField.Name;

    [ObservableProperty]
    private bool sortDescending;

    // "Todos" means no filtering - every FileSystemItemKind passes through OrderItems
    // unchanged. Applied both to ordinary folder loads (LoadFolder) and streaming
    // search results (RunSearchAsync's OnResult), so switching categories has the
    // same effect regardless of which populated the current Items.
    [ObservableProperty]
    private FileTypeCategory filterCategory = FileTypeCategory.All;

    [ObservableProperty]
    private ViewMode viewMode = ViewMode.List;

    [ObservableProperty]
    private string selectionSummary = string.Empty;

    public bool HasSelectionSummary => SelectionSummary.Length > 0;

    partial void OnSelectionSummaryChanged(string value) => OnPropertyChanged(nameof(HasSelectionSummary));

    [ObservableProperty]
    private SolidColorBrush accentBrush = new(AccentColorPalette.GetBaseColor(SettingsService.LoadAccentColor()));

    public ObservableCollection<FileSystemItem> Items { get; } = new();

    public ObservableCollection<FileSystemItem> SelectedItems { get; } = new();

    public ObservableCollection<string> Breadcrumbs { get; } = new();

    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;
    public bool CanGoUp => !string.IsNullOrEmpty(CurrentPath) && Path.GetPathRoot(CurrentPath) != CurrentPath;
    public bool IsAtDrivesRoot => string.IsNullOrEmpty(CurrentPath);

    public TabViewModel(string? initialPath = null)
    {
        _showHiddenFiles = SettingsService.LoadShowHiddenFiles();
        SelectedItems.CollectionChanged += OnSelectedItemsChanged;
        WeakReferenceMessenger.Default.Register<TabViewModel, ShowHiddenFilesChangedMessage>(
            this, (recipient, message) =>
            {
                recipient._showHiddenFiles = message.ShowHiddenFiles;
                recipient.Refresh();
            });
        WeakReferenceMessenger.Default.Register<TabViewModel, AccentColorChangedMessage>(
            this, (recipient, message) =>
                recipient.AccentBrush = new SolidColorBrush(AccentColorPalette.GetBaseColor(message.AccentColor)));
        WeakReferenceMessenger.Default.Register<TabViewModel, ClipboardChangedMessage>(
            this, (recipient, message) => recipient.UpdateCutMarkers());

        if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
        {
            NavigateTo(initialPath, recordHistory: false);
        }
        else
        {
            NavigateHome();
        }
    }

    private void OnSelectedItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SelectedItems.Count <= 1)
        {
            SelectionSummary = string.Empty;
            return;
        }

        var totalBytes = SelectedItems.Where(i => !i.IsDirectory).Sum(i => i.SizeBytes);
        SelectionSummary = totalBytes > 0
            ? $"{SelectedItems.Count} itens selecionados, {FileSystemItem.FormatSize(totalBytes)}"
            : $"{SelectedItems.Count} itens selecionados";
    }

    public void NavigateHome()
    {
        CurrentPath = string.Empty;
        Header = "Este Computador";
        Items.Clear();
        Breadcrumbs.Clear();
        Breadcrumbs.Add("Este Computador");
        _ = LoadDrivesAsync();
    }

    // Every drive is probed in parallel with its own timeout (see FileSystemService),
    // and each is added to the list as soon as it resolves - so one unreachable
    // network share or empty card reader can't hold up every other drive.
    private async Task LoadDrivesAsync()
    {
        IsBusy = true;
        StatusText = "Carregando unidades...";

        void OnDriveReady(FileSystemItem drive)
        {
            App.EnqueueOnUiThread(() =>
            {
                if (!string.IsNullOrEmpty(CurrentPath)) return; // navigated away meanwhile
                Items.Add(drive);
                StatusText = $"{Items.Count} unidade{(Items.Count == 1 ? string.Empty : "s")}";
            });
        }

        try
        {
            await FileSystemService.GetDrivesAsync(OnDriveReady);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void NavigateTo(string path, bool recordHistory = true)
    {
        if (recordHistory && !string.IsNullOrEmpty(CurrentPath))
        {
            _backStack.Add(CurrentPath);
            _forwardStack.Clear();
        }
        else if (recordHistory && string.IsNullOrEmpty(CurrentPath))
        {
            _backStack.Add(string.Empty); // came from "Este Computador"
            _forwardStack.Clear();
        }

        LoadFolder(path);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

    private void LoadFolder(string path)
    {
        // Reconciling (vs. a plain Clear()+re-Add) only pays off when refreshing the
        // *same* folder - most items are unchanged, so the diff is small and there's
        // no flicker or lost selection. Navigating to a genuinely different folder
        // means practically everything is "new" anyway, so reconciling there just
        // makes WinUI play a remove+insert transition per item instead of one bulk
        // reset - slower and jankier than the plain Clear()+Add() it replaced.
        var isSameFolder = string.Equals(CurrentPath, path, StringComparison.OrdinalIgnoreCase);

        CurrentPath = path;
        Header = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(Header)) Header = path;

        try
        {
            var entries = OrderItems(ApplyFilter(FileSystemService.EnumerateDirectory(path, _showHiddenFiles))).ToList();
            if (isSameFolder)
            {
                ReconcileItems(entries);
            }
            else
            {
                Items.Clear();
                foreach (var entry in entries) Items.Add(entry);
            }
            StatusText = $"{Items.Count} {(Items.Count == 1 ? "item" : "itens")}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Items.Clear();
            StatusText = "Acesso negado";
        }

        RebuildBreadcrumbs(path);
        UpdateCutMarkers();
    }

    // Updates Items to match `freshItems` via Insert/Remove/Move/targeted Replace
    // instead of Clear()-then-re-Add-everything - the previous approach blanked the
    // whole ListView for a frame on every refresh (every paste/copy/delete, and every
    // debounced FileSystemWatcher tick), even when only one file actually changed,
    // and silently dropped the current selection since Clear() invalidates every
    // bound SelectedItem/SelectedItems reference. Items that are unaffected keep
    // their exact object instance (preserving already-loaded Thumbnail/IsCut and
    // selection); only genuinely new/removed/changed paths touch the collection.
    private void ReconcileItems(List<FileSystemItem> freshItems)
    {
        var freshPaths = freshItems.Select(i => i.FullPath).ToHashSet();
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!freshPaths.Contains(Items[i].FullPath))
            {
                Items.RemoveAt(i);
            }
        }

        for (var i = 0; i < freshItems.Count; i++)
        {
            var fresh = freshItems[i];
            var existingIndex = -1;
            for (var j = i; j < Items.Count; j++)
            {
                if (Items[j].FullPath == fresh.FullPath)
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                Items.Insert(i, fresh);
                continue;
            }

            if (existingIndex != i) Items.Move(existingIndex, i);

            var existing = Items[i];
            if (existing.SizeBytes != fresh.SizeBytes || existing.DateModified != fresh.DateModified
                || existing.Kind != fresh.Kind || existing.IsCloudPlaceholder != fresh.IsCloudPlaceholder
                || existing.IsCloudPinned != fresh.IsCloudPinned)
            {
                fresh.IsCut = existing.IsCut;
                Items[i] = fresh;
            }
        }
    }

    // Mirrors Explorer's dimmed look for items marked with Ctrl+X. Cheap - just a
    // property flip per already-loaded item, no disk access - and kept in sync
    // across every open tab via ClipboardChangedMessage rather than a per-tab flag.
    private void UpdateCutMarkers()
    {
        var cutSet = s_clipboardIsCut ? s_clipboard : null;
        foreach (var item in Items)
        {
            item.IsCut = cutSet is not null && cutSet.Contains(item.FullPath);
        }
    }

    // "Todos" (FileTypeCategory.All) is the common case, so this is a passthrough
    // rather than a .Where() with an always-true predicate - avoids an extra
    // enumerator wrapping every folder load when no filter is active.
    private IEnumerable<FileSystemItem> ApplyFilter(IEnumerable<FileSystemItem> items) =>
        FilterCategory == FileTypeCategory.All ? items : items.Where(i => i.Category == FilterCategory);

    private IEnumerable<FileSystemItem> OrderItems(IEnumerable<FileSystemItem> items)
    {
        var byKind = items.OrderByDescending(i => i.IsDirectory);
        return SortField switch
        {
            SortField.Size => SortDescending ? byKind.ThenByDescending(i => i.SizeBytes) : byKind.ThenBy(i => i.SizeBytes),
            SortField.Modified => SortDescending ? byKind.ThenByDescending(i => i.DateModified) : byKind.ThenBy(i => i.DateModified),
            SortField.Created => SortDescending ? byKind.ThenByDescending(i => i.DateCreated) : byKind.ThenBy(i => i.DateCreated),
            _ => SortDescending
                ? byKind.ThenByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase)
                : byKind.ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
        };
    }

    // Column-header click: re-clicking the already-active column flips direction
    // instead of leaving it unchanged, matching Explorer's header-click convention.
    [RelayCommand]
    private void SortBy(SortField field)
    {
        if (SortField == field) SortDescending = !SortDescending;
        else
        {
            SortField = field;
            SortDescending = false;
        }
        Refresh();
    }

    // Used by the "Ordenar por" flyout's field/direction RadioMenuFlyoutItems, which
    // set each independently rather than toggling on re-selection like SortBy does
    // for column headers - picking "Nome" again there shouldn't flip direction.
    [RelayCommand]
    private void SetSortField(SortField field)
    {
        SortField = field;
        Refresh();
    }

    [RelayCommand]
    private void SetSortDescending(bool value)
    {
        SortDescending = value;
        Refresh();
    }

    [RelayCommand]
    private void SetFilterCategory(FileTypeCategory category)
    {
        FilterCategory = category;
        Refresh();
    }

    partial void OnCurrentPathChanged(string value)
    {
        SetupWatcher(value);
        OnPropertyChanged(nameof(IsAtDrivesRoot));
    }

    private void SetupWatcher(string path)
    {
        _watcher?.Dispose();
        _watcher = null;

        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            watcher.Created += OnWatchedFolderChanged;
            watcher.Deleted += OnWatchedFolderChanged;
            watcher.Renamed += OnWatchedFolderChanged;
            watcher.Changed += OnWatchedFolderChanged;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _watcher = null;
        }
    }

    private void OnWatchedFolderChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: a single copy/delete can raise dozens of raw watcher events.
        _refreshDebounceTimer?.Dispose();
        _refreshDebounceTimer = new Timer(
            _ => App.EnqueueOnUiThread(Refresh), null, dueTime: 400, period: Timeout.Infinite);
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        SelectedItems.CollectionChanged -= OnSelectedItemsChanged;
        _watcher?.Dispose();
        _refreshDebounceTimer?.Dispose();
        _searchDebounceTimer?.Dispose();
        _searchCts?.Cancel();
        _operationCts?.Cancel();
    }

    private void RebuildBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        var root = Path.GetPathRoot(path) ?? path;
        Breadcrumbs.Add(root.TrimEnd(Path.DirectorySeparatorChar));

        var relative = path.Substring(root.Length);
        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            Breadcrumbs.Add(part);
        }
    }

    public string BreadcrumbPathAt(int index)
    {
        var combined = Breadcrumbs.Take(index + 1).ToList();
        var root = combined[0];
        if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;
        return combined.Count == 1 ? root : Path.Combine(root, Path.Combine(combined.Skip(1).ToArray()));
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (_backStack.Count == 0) return;
        var target = _backStack[^1];
        _backStack.RemoveAt(_backStack.Count - 1);
        _forwardStack.Add(CurrentPath);

        if (string.IsNullOrEmpty(target)) NavigateHome();
        else LoadFolder(target);

        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        if (_forwardStack.Count == 0) return;
        var target = _forwardStack[^1];
        _forwardStack.RemoveAt(_forwardStack.Count - 1);
        _backStack.Add(CurrentPath);

        if (string.IsNullOrEmpty(target)) NavigateHome();
        else LoadFolder(target);

        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void GoUp()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
        var parent = Path.GetDirectoryName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar));
        if (parent == null)
        {
            NavigateTo(string.Empty);
        }
        else
        {
            NavigateTo(parent);
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        if (string.IsNullOrEmpty(CurrentPath))
        {
            Items.Clear();
            _ = LoadDrivesAsync();
        }
        else
        {
            LoadFolder(CurrentPath);
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        // Debounce so a full-subtree filesystem walk doesn't restart on every keystroke.
        _searchDebounceTimer?.Dispose();
        _searchDebounceTimer = new Timer(
            _ => App.EnqueueOnUiThread(() => _ = RunSearchAsync()), null, dueTime: 300, period: Timeout.Infinite);
    }

    public async Task RunSearchAsync()
    {
        _searchCts?.Cancel();
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            Refresh();
            return;
        }
        if (string.IsNullOrEmpty(CurrentPath)) return;

        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        IsBusy = true;
        Items.Clear();
        StatusText = "Pesquisando...";

        var dispatcherItems = new List<FileSystemItem>();
        void OnResult(FileSystemItem item)
        {
            if (FilterCategory != FileTypeCategory.All && item.Category != FilterCategory) return;
            App.EnqueueOnUiThread(() =>
            {
                if (token.IsCancellationRequested) return;
                Items.Add(item);
                StatusText = $"{Items.Count} {(Items.Count == 1 ? "resultado" : "resultados")}";
            });
        }

        try
        {
            if (IsSearchingContent)
                await SearchService.SearchByContentAsync(CurrentPath, SearchQuery, OnResult, token);
            else
                await SearchService.SearchByNameAsync(CurrentPath, SearchQuery, OnResult, token);

            if (!token.IsCancellationRequested) StatusText = $"{Items.Count} {(Items.Count == 1 ? "resultado" : "resultados")}";
        }
        catch (OperationCanceledException)
        {
            if (token == _searchCts?.Token) StatusText = "Pesquisa cancelada";
        }
        finally
        {
            // Only the still-current search may clear IsBusy - an older search
            // superseded by a newer one (different token) must not stomp on it.
            if (token == _searchCts?.Token) IsBusy = false;
        }
    }

    public void OpenItem(FileSystemItem item)
    {
        if (item.IsDirectory)
        {
            NavigateTo(item.FullPath);
            return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(item.FullPath) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No associated application registered; nothing sensible to do without a dialog.
        }
    }

    // Multi-select operates on SelectedItems; SelectedItem (the ListView's primary
    // selection) is used as a fallback for callers - like right-click - that only
    // ever touch a single item.
    private IReadOnlyList<FileSystemItem> GetOperationTargets()
    {
        if (SelectedItems.Count > 0) return SelectedItems.ToList();
        if (SelectedItem is { } item) return new[] { item };
        return Array.Empty<FileSystemItem>();
    }

    [RelayCommand]
    private void NewFolder()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        var name = "Nova pasta";
        var candidate = name;
        int i = 2;
        while (Directory.Exists(Path.Combine(CurrentPath, candidate)))
        {
            candidate = $"{name} ({i++})";
        }

        try
        {
            var path = Path.Combine(CurrentPath, candidate);
            FileSystemService.CreateFolder(CurrentPath, candidate);
            Refresh();
            UndoService.Push(new CreateItemUndoAction(path, isDirectory: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Não foi possível criar a pasta: {ex.Message}";
        }
    }

    [RelayCommand]
    private void NewFile()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        const string name = "Novo Documento de Texto";
        const string ext = ".txt";
        var candidate = name + ext;
        var i = 2;
        while (File.Exists(Path.Combine(CurrentPath, candidate)))
        {
            candidate = $"{name} ({i++}){ext}";
        }

        try
        {
            var path = Path.Combine(CurrentPath, candidate);
            File.WriteAllText(path, string.Empty);
            Refresh();
            UndoService.Push(new CreateItemUndoAction(path, isDirectory: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Não foi possível criar o arquivo: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _operationCts?.Cancel();
        _searchCts?.Cancel();
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var targets = GetOperationTargets();
        if (targets.Count == 0) return;

        IsBusy = true;
        IsTransferring = true;
        TransferProgress = 0;
        _operationCts = new CancellationTokenSource();
        var token = _operationCts.Token;
        var failures = new List<string>();
        var deletedPaths = new List<string>();
        var total = targets.Count;
        var completed = 0;
        try
        {
            await Task.Run(() =>
            {
                foreach (var item in targets)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        FileSystemService.Delete(item);
                        deletedPaths.Add(item.FullPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failures.Add(item.Name);
                    }

                    completed++;
                    var progress = (double)completed / total;
                    App.EnqueueOnUiThread(() =>
                    {
                        TransferProgress = progress;
                        StatusText = $"Excluindo {completed} de {total}...";
                    });
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            Refresh();
            StatusText = "Exclusão cancelada";
            return;
        }
        finally
        {
            IsBusy = false;
            IsTransferring = false;
            _operationCts = null;
        }

        Refresh();
        if (deletedPaths.Count > 0)
        {
            UndoService.Push(new DeleteUndoAction(deletedPaths));
        }
        if (failures.Count > 0)
        {
            StatusText = $"Não foi possível excluir: {string.Join(", ", failures)}";
        }
    }

    [RelayCommand]
    private async Task OrganizeSelectedAsync()
    {
        var targets = GetOperationTargets();
        if (targets.Count == 0) return;

        IsBusy = true;
        try
        {
            // A single selected folder organizes its own (direct) contents; anything else -
            // loose files, or a multi-selection - organizes those specific files into
            // subfolders of the current folder instead.
            var moves = await Task.Run(() =>
            {
                if (targets.Count == 1 && targets[0].IsDirectory)
                {
                    var folder = targets[0].FullPath;
                    return OrganizeService.OrganizeByType(Directory.EnumerateFiles(folder), folder);
                }

                var files = targets.Where(t => !t.IsDirectory).Select(t => t.FullPath);
                return OrganizeService.OrganizeByType(files, CurrentPath);
            });

            Refresh();
            if (moves.Count > 0)
            {
                UndoService.Push(new MoveUndoAction(moves));
                StatusText = moves.Count == 1 ? "1 arquivo organizado" : $"{moves.Count} arquivos organizados";
            }
            else
            {
                StatusText = "Nada para organizar";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Não foi possível organizar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RenameAsync(FileSystemItem item, string newName)
    {
        var oldName = item.Name;
        try
        {
            await Task.Run(() => FileSystemService.Rename(item, newName));
            Refresh();
            var parent = Path.GetDirectoryName(item.FullPath)!;
            var newPath = Path.Combine(parent, newName);
            UndoService.Push(new RenameUndoAction(newPath, oldName, item.IsDirectory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Não foi possível renomear '{item.Name}': {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopySelected()
    {
        var targets = GetOperationTargets();
        if (targets.Count == 0) return;
        s_clipboard = targets.Select(t => t.FullPath).ToList();
        s_clipboardIsCut = false;
        StatusText = targets.Count == 1 ? $"Copiado '{targets[0].Name}'" : $"Copiados {targets.Count} itens";
        WeakReferenceMessenger.Default.Send(new ClipboardChangedMessage());
    }

    [RelayCommand]
    private void CutSelected()
    {
        var targets = GetOperationTargets();
        if (targets.Count == 0) return;
        s_clipboard = targets.Select(t => t.FullPath).ToList();
        s_clipboardIsCut = true;
        StatusText = targets.Count == 1 ? $"Recortado '{targets[0].Name}'" : $"Recortados {targets.Count} itens";
        WeakReferenceMessenger.Default.Send(new ClipboardChangedMessage());
    }

    // Only the file name conflicts against the destination folder - used by the view
    // to decide whether to prompt for Replace/Skip/Keep both before starting a transfer.
    public IReadOnlyList<string> PeekPasteConflicts() =>
        string.IsNullOrEmpty(CurrentPath) ? Array.Empty<string>() : PeekConflicts(s_clipboard, CurrentPath);

    // destinationFolder is the specific subfolder a file was dropped directly onto
    // (FolderView.GetDropTargetFolder), when there was one - otherwise CurrentPath,
    // matching a drop anywhere else in the list.
    public IReadOnlyList<string> PeekDropConflicts(IReadOnlyList<string> sourcePaths, string? destinationFolder = null)
    {
        var destination = destinationFolder ?? CurrentPath;
        return string.IsNullOrEmpty(destination) ? Array.Empty<string>() : PeekConflicts(sourcePaths, destination);
    }

    private static List<string> PeekConflicts(IReadOnlyList<string> sourcePaths, string destination)
    {
        var conflicts = new List<string>();
        foreach (var sourcePath in sourcePaths)
        {
            var name = Path.GetFileName(sourcePath);
            if (!string.IsNullOrEmpty(name) && FileSystemService.Exists(destination, name))
            {
                conflicts.Add(name);
            }
        }
        return conflicts;
    }

    public async Task PasteAsync(ConflictResolution resolution)
    {
        if (string.IsNullOrEmpty(CurrentPath) || s_clipboard.Count == 0) return;

        var destination = CurrentPath;
        var sources = s_clipboard.ToList();
        var isCut = s_clipboardIsCut;

        var outcome = await TransferAsync(sources, destination, isCut, resolution);
        if (outcome is null) return; // cancelled

        if (isCut)
        {
            s_clipboard = new List<string>();
            WeakReferenceMessenger.Default.Send(new ClipboardChangedMessage());
        }
        Refresh();

        if (outcome.Succeeded.Count > 0)
        {
            UndoService.Push(isCut
                ? new MoveUndoAction(outcome.Succeeded.Select(s => (s.DestPath, s.SourcePath)).ToList())
                : new CopyUndoAction(outcome.Succeeded.Select(s => s.DestPath).ToList()));
        }
        if (outcome.Failures.Count > 0)
        {
            StatusText = $"Não foi possível colar: {string.Join(", ", outcome.Failures)}";
        }
        else
        {
            StatusText = outcome.Succeeded.Count == 1
                ? $"Colado '{Path.GetFileName(outcome.Succeeded[0].DestPath)}'"
                : $"Colados {outcome.Succeeded.Count} itens";
        }
    }

    public async Task CopyIntoAsync(IReadOnlyList<string> sourcePaths, ConflictResolution resolution, string? destinationFolder = null)
    {
        var destination = destinationFolder ?? CurrentPath;
        if (string.IsNullOrEmpty(destination) || sourcePaths.Count == 0) return;

        var outcome = await TransferAsync(sourcePaths, destination, isCut: false, resolution);
        if (outcome is null) return; // cancelled

        Refresh();

        if (outcome.Succeeded.Count > 0)
        {
            UndoService.Push(new CopyUndoAction(outcome.Succeeded.Select(s => s.DestPath).ToList()));
        }
        if (outcome.Failures.Count > 0)
        {
            StatusText = $"Não foi possível copiar: {string.Join(", ", outcome.Failures)}";
        }
        else
        {
            StatusText = outcome.Succeeded.Count == 1
                ? $"Copiado '{Path.GetFileName(outcome.Succeeded[0].DestPath)}'"
                : $"Copiados {outcome.Succeeded.Count} itens";
        }
    }

    private sealed class TransferOutcome
    {
        public List<string> Failures { get; } = new();
        public List<(string DestPath, string SourcePath)> Succeeded { get; } = new();
    }

    // Returns null if the transfer was cancelled partway through.
    private async Task<TransferOutcome?> TransferAsync(
        IReadOnlyList<string> sourcePaths, string destination, bool isCut, ConflictResolution resolution)
    {
        IsBusy = true;
        IsTransferring = true;
        TransferProgress = 0;
        _operationCts = new CancellationTokenSource();
        var token = _operationCts.Token;
        var outcome = new TransferOutcome();
        var total = sourcePaths.Count;
        var completed = 0;
        var verb = isCut ? "Movendo" : "Copiando";
        try
        {
            await Task.Run(() =>
            {
                foreach (var sourcePath in sourcePaths)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var info = Directory.Exists(sourcePath)
                            ? FileSystemService.ToItem(new DirectoryInfo(sourcePath))
                            : FileSystemService.ToItem(new FileInfo(sourcePath));

                        var conflict = FileSystemService.Exists(destination, info.Name);
                        if (conflict && resolution == ConflictResolution.Skip) continue;

                        string? destName = null;
                        var overwrite = false;
                        if (conflict && resolution == ConflictResolution.KeepBoth)
                            destName = FileSystemService.MakeUniqueName(destination, info.Name);
                        else if (conflict && resolution == ConflictResolution.Replace)
                            overwrite = true;

                        var finalDestPath = Path.Combine(destination, destName ?? info.Name);

                        if (isCut)
                            FileSystemService.Move(info, destination, destName, overwrite);
                        else
                            FileSystemService.Copy(info, destination, destName, overwrite);

                        outcome.Succeeded.Add((finalDestPath, sourcePath));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        outcome.Failures.Add(Path.GetFileName(sourcePath));
                    }

                    completed++;
                    var progress = (double)completed / total;
                    App.EnqueueOnUiThread(() =>
                    {
                        TransferProgress = progress;
                        StatusText = $"{verb} {completed} de {total}...";
                    });
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            Refresh();
            StatusText = "Operação cancelada";
            return null;
        }
        finally
        {
            IsBusy = false;
            IsTransferring = false;
            _operationCts = null;
        }

        return outcome;
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (!UndoService.CanUndo)
        {
            StatusText = "Nada para desfazer";
            return;
        }

        IsBusy = true;
        string? description = null;
        try
        {
            await Task.Run(() => UndoService.TryUndo(out description));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Não foi possível desfazer: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        Refresh();
        StatusText = description ?? "Desfeito";
    }
}
