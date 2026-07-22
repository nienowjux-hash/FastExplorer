using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FastExplorer.Messaging;
using FastExplorer.Models;
using FastExplorer.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FastExplorer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<FavoriteEntry> Favorites { get; } = new();
    public IReadOnlyList<AppTheme> AvailableThemes { get; } = Enum.GetValues<AppTheme>();
    public IReadOnlyList<AccentColor> AvailableAccentColors { get; } = Enum.GetValues<AccentColor>();

    // The split-pane tree. Restart always starts from a single unsplit pane -
    // splits are a live-session feature, not persisted (see SaveOpenTabs).
    [ObservableProperty]
    private PaneNode rootPane = null!;

    // Which leaf pane global keyboard shortcuts (Ctrl+T, Ctrl+W, Ctrl+Tab) and the
    // sidebar's "This PC"/favorites/network-drive actions apply to. Kept in sync by
    // PaneGroupView's PointerPressed/GotFocus handlers via SetActivePane, mirroring
    // the FileList focus-on-load pattern already used for per-tab keyboard shortcuts.
    [ObservableProperty]
    private PaneViewModel activePane = null!;

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

        var root = new PaneViewModel { Owner = this };
        var savedTabs = SettingsService.LoadOpenTabs();
        if (savedTabs.Count == 0)
        {
            root.Tabs.Add(new TabViewModel());
        }
        else
        {
            foreach (var path in savedTabs)
            {
                root.Tabs.Add(new TabViewModel(path));
            }
        }
        root.SelectedTab = root.Tabs[0];

        rootPane = root;
        activePane = root;
    }

    // Called on shutdown (see MainWindow.Closed) so the next launch reopens the
    // same folders - cheap since it's just the current path per pane's tabs,
    // flattened across every pane in the tree, written once.
    public void SaveOpenTabs()
    {
        SettingsService.SaveOpenTabs(EnumerateLeaves(RootPane).SelectMany(p => p.Tabs).Select(t => t.CurrentPath));
    }

    private static IEnumerable<PaneViewModel> EnumerateLeaves(PaneNode node)
    {
        switch (node)
        {
            case PaneViewModel leaf:
                yield return leaf;
                break;
            case SplitPaneNode split:
                foreach (var leaf in EnumerateLeaves(split.First)) yield return leaf;
                foreach (var leaf in EnumerateLeaves(split.Second)) yield return leaf;
                break;
        }
    }

    private static PaneViewModel LeftmostLeaf(PaneNode node) => node switch
    {
        PaneViewModel leaf => leaf,
        SplitPaneNode split => LeftmostLeaf(split.First),
        _ => throw new InvalidOperationException("Unreachable pane node type."),
    };

    public void SetActivePane(PaneViewModel pane) => ActivePane = pane;

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

    public void NewTab(PaneViewModel pane, string? initialPath = null)
    {
        var tab = initialPath is null ? new TabViewModel() : new TabViewModel(initialPath);
        pane.Tabs.Add(tab);
        pane.SelectedTab = tab;
    }

    public void CloseTab(PaneViewModel pane, TabViewModel? tab)
    {
        tab ??= pane.SelectedTab;
        if (tab is null) return;
        RemoveTabFromPane(pane, tab);
    }

    // Shared by ordinary tab-close and drag-a-tab-to-another-pane: removes `tab`
    // from `pane`, then either reselects (tabs remain), refills with a fresh tab
    // (pane is the sole pane left in the whole tree), or collapses `pane` out of
    // the tree entirely by promoting its sibling into its old slot.
    public void RemoveTabFromPane(PaneViewModel pane, TabViewModel tab)
    {
        var index = pane.Tabs.IndexOf(tab);
        if (index < 0) return;

        pane.Tabs.RemoveAt(index);
        tab.Dispose();

        if (pane.Tabs.Count > 0)
        {
            if (pane.SelectedTab == tab)
            {
                pane.SelectedTab = pane.Tabs[Math.Max(0, index - 1)];
            }
            return;
        }

        CollapseEmptyPane(pane);
    }

    // Moves `tab` from `source` into `target` (drag-to-dock's Center zone, or a
    // tab dragged directly onto another pane's tab strip) without disposing it -
    // unlike RemoveTabFromPane/CloseTab, the tab keeps living, just in a new pane.
    public void MoveTabToPane(TabViewModel tab, PaneViewModel source, PaneViewModel target)
    {
        var index = source.Tabs.IndexOf(tab);
        if (index < 0) return;

        source.Tabs.RemoveAt(index);
        if (source.Tabs.Count > 0)
        {
            if (source.SelectedTab == tab)
            {
                source.SelectedTab = source.Tabs[Math.Max(0, index - 1)];
            }
        }
        else
        {
            CollapseEmptyPane(source);
        }

        target.Tabs.Add(tab);
        target.SelectedTab = tab;
        ActivePane = target;
    }

    // `pane` has just lost its last tab: refill it if it's the sole pane left in
    // the whole tree (never leave the window fully empty), otherwise splice it
    // out of the tree entirely by promoting its sibling into its old slot.
    private void CollapseEmptyPane(PaneViewModel pane)
    {
        if (pane.Parent is not SplitPaneNode parentSplit)
        {
            NewTab(pane);
            return;
        }

        var sibling = parentSplit.SiblingOf(pane);
        if (sibling is null) return;

        if (parentSplit.Parent is SplitPaneNode grandparent)
        {
            grandparent.Replace(parentSplit, sibling);
        }
        else
        {
            sibling.Parent = null;
            RootPane = sibling;
        }

        if (ActivePane == pane)
        {
            ActivePane = LeftmostLeaf(sibling);
        }
    }

    public void NavigateActiveTab(string path)
    {
        ActivePane.SelectedTab?.NavigateTo(path);
    }

    // Splices `newPane` in next to `target`, replacing target's old slot in the
    // tree with a fresh SplitPaneNode containing both.
    public void SplitPane(PaneViewModel target, Orientation orientation, PaneViewModel newPane, bool newPaneIsSecond)
    {
        var oldParent = target.Parent;
        var split = newPaneIsSecond
            ? new SplitPaneNode(orientation, target, newPane)
            : new SplitPaneNode(orientation, newPane, target);

        if (oldParent is SplitPaneNode parentSplit)
        {
            parentSplit.Replace(target, split);
        }
        else
        {
            split.Parent = null;
            RootPane = split;
        }
    }

    // Drag-to-dock's edge-zone entry point (see PaneGroupView.DragOverlay_Drop):
    // moves `tab` out of `sourcePane` into a brand-new sibling pane docked next to
    // `targetPane`. Order matters here - the split MUST happen before any
    // collapse-eligible removal runs, not after: dragging a pane's own single tab
    // to split itself (sourcePane == targetPane) used to call RemoveTabFromPane
    // first, which - if that was the pane's last tab - collapsed it out of the
    // tree (splicing its sibling into its old slot) *before* SplitPane ran, so
    // SplitPane then reparented the split under a Parent reference that no longer
    // led anywhere reachable from RootPane. The pane would simply vanish. Removing
    // the tab from the plain list first (not through the collapse-checking
    // RemoveTabFromPane), splitting while targetPane's tree position is still
    // valid, and only then checking whether the source needs to collapse (using
    // its now-correct, possibly brand-new parent) avoids that entirely - for both
    // the self-split and cross-pane-drag cases.
    public void SplitPaneWithTab(PaneViewModel targetPane, Orientation orientation, bool newPaneIsSecond, TabViewModel tab, PaneViewModel sourcePane)
    {
        if (ReferenceEquals(sourcePane, targetPane) && sourcePane.Tabs.Count <= 1)
        {
            // Nothing would be left on one side of the split - a single-tab pane
            // can't meaningfully split against itself.
            return;
        }

        var index = sourcePane.Tabs.IndexOf(tab);
        if (index < 0) return;

        sourcePane.Tabs.RemoveAt(index);

        var newPane = new PaneViewModel { Owner = this };
        newPane.Tabs.Add(tab);
        newPane.SelectedTab = tab;

        SplitPane(targetPane, orientation, newPane, newPaneIsSecond);

        if (sourcePane.Tabs.Count > 0)
        {
            if (sourcePane.SelectedTab == tab)
            {
                sourcePane.SelectedTab = sourcePane.Tabs[Math.Max(0, index - 1)];
            }
        }
        else
        {
            CollapseEmptyPane(sourcePane);
        }

        ActivePane = newPane;
    }

    [RelayCommand]
    private void AddFavorite(FileSystemItem? item)
    {
        var path = item?.FullPath ?? ActivePane.SelectedTab?.CurrentPath;
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
