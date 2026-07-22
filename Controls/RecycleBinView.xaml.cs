using System.Collections.ObjectModel;
using FastExplorer.Models;
using FastExplorer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastExplorer.Controls;

// Hosted directly in a ContentDialog (MainWindow.RecycleBinButton_Click) rather than
// as a real navigable folder - TabViewModel assumes CurrentPath is a real directory
// (watchers, history, etc.), and the Recycle Bin's virtual namespace doesn't fit that
// model, so this is a self-contained browser instead of a special-cased tab.
public sealed partial class RecycleBinView : UserControl
{
    private readonly ObservableCollection<RecycleBinItemInfo> _items = new();

    // The action to run if the inline ConfirmPanel's "Confirmar" is clicked - set by
    // whichever of DeleteButton_Click/EmptyButton_Click opened it, cleared once it
    // either runs or is cancelled.
    private Func<Task>? _pendingConfirmAction;

    public RecycleBinView()
    {
        InitializeComponent();
        ItemsList.ItemsSource = _items;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        LoadingRing.IsActive = true;
        StatusText.Text = string.Empty;

        var items = await Task.Run(RecycleBinService.ListItems);

        _items.Clear();
        foreach (var item in items.OrderByDescending(i => i.DateDeleted))
        {
            _items.Add(item);
        }

        EmptyStateText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = _items.Count == 1 ? "1 item" : $"{_items.Count} itens";
        LoadingRing.IsActive = false;
    }

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = ItemsList.SelectedItems.Count > 0;
        RestoreButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ItemsList.SelectedItems.Cast<RecycleBinItemInfo>().ToList();
        if (selected.Count == 0) return;

        LoadingRing.IsActive = true;
        var restored = await Task.Run(() => selected.Count(item => RecycleBinService.TryRestore(item.OriginalPath)));

        StatusText.Text = restored == selected.Count
            ? $"{restored} item(ns) restaurado(s)."
            : $"{restored} de {selected.Count} item(ns) restaurado(s).";
        await ReloadAsync();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ItemsList.SelectedItems.Cast<RecycleBinItemInfo>().ToList();
        if (selected.Count == 0) return;

        ShowConfirm(
            $"{selected.Count} item(ns) serão excluídos permanentemente e não poderão ser restaurados. Continuar?",
            async () =>
            {
                LoadingRing.IsActive = true;
                var deleted = await Task.Run(() => selected.Count(item => RecycleBinService.TryDeletePermanently(item.OriginalPath)));

                StatusText.Text = deleted == selected.Count
                    ? $"{deleted} item(ns) excluído(s) permanentemente."
                    : $"{deleted} de {selected.Count} item(ns) excluído(s).";
                await ReloadAsync();
            });
    }

    private void EmptyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0) return;

        ShowConfirm(
            $"Todos os {_items.Count} item(ns) da lixeira serão excluídos permanentemente e não poderão ser restaurados. Continuar?",
            async () =>
            {
                LoadingRing.IsActive = true;
                await Task.Run(RecycleBinService.EmptyRecycleBin);
                StatusText.Text = "Lixeira esvaziada.";
                await ReloadAsync();
            });
    }

    private void ShowConfirm(string message, Func<Task> onConfirm)
    {
        _pendingConfirmAction = onConfirm;
        ConfirmText.Text = message;
        ConfirmPanel.Visibility = Visibility.Visible;
    }

    private async void ConfirmYesButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmPanel.Visibility = Visibility.Collapsed;
        var action = _pendingConfirmAction;
        _pendingConfirmAction = null;
        if (action is not null) await action();
    }

    private void ConfirmNoButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmPanel.Visibility = Visibility.Collapsed;
        _pendingConfirmAction = null;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ReloadAsync();
}
