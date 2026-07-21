using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FastExplorer.Messaging;
using FastExplorer.Models;
using FastExplorer.Services;
using Microsoft.UI.Xaml.Media;

namespace FastExplorer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    public ObservableCollection<FavoriteEntry> Favorites { get; } = new();
    public IReadOnlyList<AppTheme> AvailableThemes { get; } = Enum.GetValues<AppTheme>();
    public IReadOnlyList<AccentColor> AvailableAccentColors { get; } = Enum.GetValues<AccentColor>();

    [ObservableProperty]
    private TabViewModel? selectedTab;

    [ObservableProperty]
    private AppTheme selectedTheme;

    [ObservableProperty]
    private bool showHiddenFiles;

    [ObservableProperty]
    private AccentColor selectedAccentColor;

    // WinUI has no DynamicResource (that's WPF-only), so overriding SystemAccentColor
    // at runtime doesn't reliably repaint already-realized built-in controls. This
    // brush is bound directly wherever the app wants to visibly reflect the chosen
    // accent - ordinary data binding, so it's guaranteed to update live.
    public SolidColorBrush AccentBrush { get; private set; }

    public MainViewModel()
    {
        foreach (var fav in FavoritesService.Load())
        {
            Favorites.Add(fav);
        }
        selectedTheme = SettingsService.LoadTheme();
        showHiddenFiles = SettingsService.LoadShowHiddenFiles();
        selectedAccentColor = SettingsService.LoadAccentColor();
        AccentBrush = new SolidColorBrush(AccentColorPalette.GetBaseColor(selectedAccentColor));

        var savedTabs = SettingsService.LoadOpenTabs();
        if (savedTabs.Count == 0)
        {
            NewTab();
        }
        else
        {
            foreach (var path in savedTabs)
            {
                OpenTab(path);
            }
            SelectedTab = Tabs[0];
        }
    }

    // Called on shutdown (see MainWindow.Closed) so the next launch reopens the
    // same folders - cheap since it's just the current path per tab, written once.
    public void SaveOpenTabs()
    {
        SettingsService.SaveOpenTabs(Tabs.Select(t => t.CurrentPath));
    }

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        SettingsService.SaveTheme(value);
        WeakReferenceMessenger.Default.Send(new ThemeChangedMessage(value));
    }

    partial void OnShowHiddenFilesChanged(bool value)
    {
        SettingsService.SaveShowHiddenFiles(value);
        WeakReferenceMessenger.Default.Send(new ShowHiddenFilesChangedMessage(value));
    }

    partial void OnSelectedAccentColorChanged(AccentColor value)
    {
        SettingsService.SaveAccentColor(value);
        AccentBrush = new SolidColorBrush(AccentColorPalette.GetBaseColor(value));
        OnPropertyChanged(nameof(AccentBrush));
        WeakReferenceMessenger.Default.Send(new AccentColorChangedMessage(value));
    }

    [RelayCommand]
    private void NewTab()
    {
        var tab = new TabViewModel();
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    private void OpenTab(string path)
    {
        var tab = new TabViewModel(path);
        Tabs.Add(tab);
    }

    [RelayCommand]
    private void CloseTab(TabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null) return;
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();

        if (Tabs.Count == 0)
        {
            NewTab();
            return;
        }

        if (SelectedTab == tab)
        {
            SelectedTab = Tabs[Math.Max(0, index - 1)];
        }
    }

    public void NavigateActiveTab(string path)
    {
        SelectedTab?.NavigateTo(path);
    }

    [RelayCommand]
    private void AddFavorite(FileSystemItem? item)
    {
        var path = item?.FullPath ?? SelectedTab?.CurrentPath;
        if (string.IsNullOrEmpty(path)) return;
        if (Favorites.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) return;

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) name = path;
        var entry = new FavoriteEntry { Name = name, Path = path };
        Favorites.Add(entry);
        FavoritesService.Save(Favorites);
    }

    [RelayCommand]
    private void RemoveFavorite(FavoriteEntry entry)
    {
        Favorites.Remove(entry);
        FavoritesService.Save(Favorites);
    }
}
