using System.Collections.ObjectModel;
using FastExplorer.Models;
using FastExplorer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastExplorer.Controls;

// Hosted directly in a ContentDialog (FolderView's "Buscar arquivos duplicados" nav-bar
// button and per-folder context menu item), mirroring DiskUsageView/RecycleBinView.
// DuplicateFinderService.FindDuplicatesAsync returns groups; this view flattens them into
// one selectable list (DuplicateFileRow per file) since WinUI's grouped-ListView setup
// (CollectionViewSource + GroupStyle) buys little here for one extra "group label" column.
public sealed partial class DuplicatesView : UserControl
{
    private readonly ObservableCollection<DuplicateFileRow> _rows = new();
    private readonly string _rootPath;
    private CancellationTokenSource? _scanCts;

    public DuplicatesView(string rootPath)
    {
        InitializeComponent();
        _rootPath = rootPath;
        ItemsList.ItemsSource = _rows;
        Loaded += async (_, _) => await ScanAsync();
    }

    private async Task ScanAsync()
    {
        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        var token = cts.Token;

        LoadingRing.IsActive = true;
        CancelScanButton.IsEnabled = true;
        RefreshButton.IsEnabled = false;
        SummaryText.Text = "Buscando duplicados...";
        _rows.Clear();
        EmptyStateText.Visibility = Visibility.Collapsed;

        IReadOnlyList<DuplicateFileGroup> groups;
        try
        {
            groups = await DuplicateFinderService.FindDuplicatesAsync(_rootPath, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (token == _scanCts?.Token)
            {
                LoadingRing.IsActive = false;
                CancelScanButton.IsEnabled = false;
                RefreshButton.IsEnabled = true;
            }
        }

        if (token.IsCancellationRequested) return; // superseded by a newer scan meanwhile

        var wastedBytes = 0L;
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            var groupLabel = $"Grupo {g + 1} ({group.Paths.Count} cópias)";
            wastedBytes += group.SizeBytes * (group.Paths.Count - 1);

            for (var i = 0; i < group.Paths.Count; i++)
            {
                var path = group.Paths[i];
                _rows.Add(new DuplicateFileRow
                {
                    FullPath = path,
                    Name = Path.GetFileName(path),
                    FolderPath = Path.GetDirectoryName(path) ?? string.Empty,
                    SizeDisplay = FileSystemItem.FormatSize(group.SizeBytes),
                    GroupLabel = groupLabel,
                    GroupIndex = g,
                    IsFirstInGroup = i == 0,
                });
            }
        }

        EmptyStateText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SummaryText.Text = groups.Count == 0
            ? "Nenhum grupo de duplicados."
            : $"{groups.Count} grupo(s), {_rows.Count} arquivo(s), {FileSystemItem.FormatSize(wastedBytes)} desperdiçados";
    }

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DeleteButton.IsEnabled = ItemsList.SelectedItems.Count > 0;
    }

    // Selects every row except the first in each group - the common "keep one, remove
    // the rest" duplicate-cleanup default. Users can still adjust the selection by hand
    // before deleting.
    private void SmartSelectButton_Click(object sender, RoutedEventArgs e)
    {
        ItemsList.SelectedItems.Clear();
        foreach (var row in _rows)
        {
            if (!row.IsFirstInGroup) ItemsList.SelectedItems.Add(row);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ItemsList.SelectedItems.Cast<DuplicateFileRow>().ToList();
        if (selected.Count == 0) return;

        ConfirmText.Text = selected.Count == 1
            ? $"\"{selected[0].Name}\" será movido para a Lixeira. Continuar?"
            : $"{selected.Count} arquivo(s) serão movidos para a Lixeira. Continuar?";
        ConfirmPanel.Visibility = Visibility.Visible;
        _pendingDeletion = selected;
    }

    private List<DuplicateFileRow>? _pendingDeletion;

    private async void ConfirmYesButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmPanel.Visibility = Visibility.Collapsed;
        var selected = _pendingDeletion;
        _pendingDeletion = null;
        if (selected is null || selected.Count == 0) return;

        LoadingRing.IsActive = true;
        var deleted = 0;
        await Task.Run(() =>
        {
            foreach (var row in selected)
            {
                try
                {
                    RecycleBinHelper.SendToRecycleBin(row.FullPath);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Skip and keep going - report the shortfall below.
                }
            }
        });
        LoadingRing.IsActive = false;

        SummaryText.Text = deleted == selected.Count
            ? $"{deleted} arquivo(s) excluído(s)."
            : $"{deleted} de {selected.Count} arquivo(s) excluído(s).";
        await ScanAsync();
    }

    private void ConfirmNoButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmPanel.Visibility = Visibility.Collapsed;
        _pendingDeletion = null;
    }

    private void CancelScanButton_Click(object sender, RoutedEventArgs e) => _scanCts?.Cancel();
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ScanAsync();
}
