using FastExplorer.Converters;
using FastExplorer.Models;
using FastExplorer.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FastExplorer.Controls;

// A WinDirStat-style treemap for "what's eating my disk space" - hosted directly in
// a ContentDialog (FolderView's "Analisar espaço em disco" nav-bar button and
// per-folder context menu item), one level at a time: DiskUsageService.ScanLevelAsync
// computes total size for every *direct* child of the folder being shown (recursive
// for subdirectories, but not pre-walking the whole subtree), and drilling into a
// directory cell just re-scans at that new level - so a single scan is always bounded
// to "one folder's immediate children", never "however much of the disk is under here".
public sealed partial class DiskUsageView : UserControl
{
    // Must match TreemapCanvas's declared Width/Height in the XAML - see the comment
    // there on why this isn't read from ActualWidth/Height instead.
    private const double CanvasWidth = 480;
    private const double CanvasHeight = 340;

    private readonly List<string> _pathStack = new();
    private string _currentPath;
    private IReadOnlyList<DiskUsageEntry> _currentEntries = Array.Empty<DiskUsageEntry>();
    private CancellationTokenSource? _scanCts;
    private Border? _selectedCell;
    private DiskUsageEntry? _selectedEntry;

    public DiskUsageView(string initialPath)
    {
        InitializeComponent();
        _currentPath = initialPath;
        Loaded += async (_, _) => await ScanAndRenderAsync(_currentPath);
    }

    private async Task ScanAndRenderAsync(string path)
    {
        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        var token = cts.Token;

        LoadingRing.IsActive = true;
        CancelScanButton.IsEnabled = true;
        TreemapCanvas.Children.Clear();
        EmptyStateText.Visibility = Visibility.Collapsed;
        ClearSelection();
        PathText.Text = path;

        IReadOnlyList<DiskUsageEntry> entries;
        try
        {
            entries = await DiskUsageService.ScanLevelAsync(path, token);
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
            }
        }

        if (token.IsCancellationRequested) return; // superseded by a newer scan meanwhile

        _currentPath = path;
        _currentEntries = entries;
        BackButton.IsEnabled = _pathStack.Count > 0;
        Render(entries);
    }

    private void Render(IReadOnlyList<DiskUsageEntry> entries)
    {
        if (entries.Count == 0)
        {
            EmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        var sizes = entries.Select(e => (double)e.SizeBytes).ToList();
        var layout = TreemapLayout.Compute(sizes, new TreemapRect(0, 0, CanvasWidth, CanvasHeight));

        foreach (var (index, rect) in layout)
        {
            var entry = entries[index];
            var cell = BuildCell(entry, rect);
            Canvas.SetLeft(cell, rect.X);
            Canvas.SetTop(cell, rect.Y);
            TreemapCanvas.Children.Add(cell);
        }
    }

    private Border BuildCell(DiskUsageEntry entry, TreemapRect rect)
    {
        var colorHex = IconGlyphMap.GetColorHex(ToPseudoItem(entry)) ?? "#5B6B85";
        var cell = new Border
        {
            Width = Math.Max(0, rect.Width - 1),
            Height = Math.Max(0, rect.Height - 1),
            Background = HexColorToBrushConverter.ToBrush(colorHex),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Tag = entry,
        };
        cell.Tapped += Cell_Tapped;
        cell.DoubleTapped += Cell_DoubleTapped;
        ToolTipService.SetToolTip(cell, $"{entry.Name}\n{entry.SizeDisplay}");

        if (rect.Width > 50 && rect.Height > 20)
        {
            var label = new TextBlock
            {
                Text = $"{entry.Name}\n{entry.SizeDisplay}",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(4, 2, 4, 2),
                MaxWidth = rect.Width - 8,
            };
            cell.Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(2),
                Child = label,
            };
        }

        return cell;
    }

    // IconGlyphMap.GetColorHex takes a FileSystemItem, so DiskUsageEntry (which isn't
    // one - it's a lighter, treemap-specific record) gets wrapped just long enough to
    // reuse the app's existing per-extension color table instead of a second one.
    private static FileSystemItem ToPseudoItem(DiskUsageEntry entry) => new()
    {
        Name = entry.Name,
        FullPath = entry.FullPath,
        Kind = entry.IsDirectory ? FileSystemItemKind.Directory : FileSystemItemKind.File,
        Extension = entry.IsDirectory ? string.Empty : Path.GetExtension(entry.Name),
    };

    private void Cell_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not Border { Tag: DiskUsageEntry entry } cell) return;
        SelectCell(cell, entry);
    }

    private async void Cell_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not Border { Tag: DiskUsageEntry { IsDirectory: true } entry }) return;
        _pathStack.Add(_currentPath);
        await ScanAndRenderAsync(entry.FullPath);
    }

    private void SelectCell(Border cell, DiskUsageEntry entry)
    {
        if (_selectedCell is not null) _selectedCell.BorderThickness = new Thickness(1);
        cell.BorderThickness = new Thickness(2);
        cell.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        _selectedCell = cell;
        _selectedEntry = entry;
        SelectionText.Text = $"{entry.Name} — {entry.SizeDisplay}";
        DeleteButton.IsEnabled = true;
    }

    private void ClearSelection()
    {
        _selectedCell = null;
        _selectedEntry = null;
        SelectionText.Text = string.Empty;
        DeleteButton.IsEnabled = false;
        ConfirmPanel.Visibility = Visibility.Collapsed;
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pathStack.Count == 0) return;
        var previous = _pathStack[^1];
        _pathStack.RemoveAt(_pathStack.Count - 1);
        await ScanAndRenderAsync(previous);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ScanAndRenderAsync(_currentPath);

    private void CancelScanButton_Click(object sender, RoutedEventArgs e) => _scanCts?.Cancel();

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is not { } entry) return;

        ConfirmText.Text = $"\"{entry.Name}\" será movido para a Lixeira. Continuar?";
        ConfirmPanel.Visibility = Visibility.Visible;
    }

    private async void ConfirmYesButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmPanel.Visibility = Visibility.Collapsed;
        if (_selectedEntry is not { } entry) return;

        try
        {
            await Task.Run(() => RecycleBinHelper.SendToRecycleBin(entry.FullPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SelectionText.Text = $"Falha ao excluir \"{entry.Name}\": {ex.Message}";
        }

        await ScanAndRenderAsync(_currentPath);
    }

    private void ConfirmNoButton_Click(object sender, RoutedEventArgs e) => ConfirmPanel.Visibility = Visibility.Collapsed;
}
