using CommunityToolkit.Mvvm.Messaging;
using FastExplorer.Controls;
using FastExplorer.Messaging;
using FastExplorer.Models;
using FastExplorer.Services;
using FastExplorer.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinRT.Interop;

namespace FastExplorer;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        RootGrid.DataContext = ViewModel;
        Title = "FastExplorer";
        ApplyTheme(ViewModel.SelectedTheme);
        ApplyAccentColor(ViewModel.SelectedAccentColor);
        ApplyIcon();

        WeakReferenceMessenger.Default.Register<MainWindow, PreviewRequestedMessage>(
            this, (recipient, message) => recipient.PreviewPaneControl.SetItem(message.Item));
        WeakReferenceMessenger.Default.Register<MainWindow, AddFavoriteRequestedMessage>(
            this, (recipient, message) => recipient.ViewModel.AddFavoriteCommand.Execute(message.Item));
        WeakReferenceMessenger.Default.Register<MainWindow, ThemeChangedMessage>(
            this, (recipient, message) => recipient.ApplyTheme(message.Theme));
        WeakReferenceMessenger.Default.Register<MainWindow, AccentColorChangedMessage>(
            this, (recipient, message) => recipient.ApplyAccentColor(message.AccentColor));
        WeakReferenceMessenger.Default.Register<MainWindow, OpenInNewTabRequestedMessage>(
            this, (recipient, message) => recipient.ViewModel.NewTab(recipient.ViewModel.ActivePane, message.Path));

        Closed += (_, _) =>
        {
            ViewModel.SaveOpenTabs();
            WeakReferenceMessenger.Default.UnregisterAll(this);
        };
    }

    private void ApplyTheme(AppTheme theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    // Overriding SystemAccentColor (and its Light1-3/Dark1-3 tints) is the
    // sanctioned way to recolor built-in accent-driven visuals (ListView selection
    // highlight, CheckBox/ComboBox focus states, etc.) app-wide. Existing controls
    // don't repaint just because the resource value changed, so this briefly flips
    // RequestedTheme to force WinUI to re-evaluate ThemeResource lookups, then
    // restores the user's actual theme choice.
    private void ApplyAccentColor(AccentColor accent)
    {
        var baseColor = AccentColorPalette.GetBaseColor(accent);
        var resources = Application.Current.Resources;
        resources["SystemAccentColor"] = baseColor;
        resources["SystemAccentColorLight1"] = AccentColorPalette.Lighten(baseColor, 0.15);
        resources["SystemAccentColorLight2"] = AccentColorPalette.Lighten(baseColor, 0.30);
        resources["SystemAccentColorLight3"] = AccentColorPalette.Lighten(baseColor, 0.45);
        resources["SystemAccentColorDark1"] = AccentColorPalette.Darken(baseColor, 0.15);
        resources["SystemAccentColorDark2"] = AccentColorPalette.Darken(baseColor, 0.30);
        resources["SystemAccentColorDark3"] = AccentColorPalette.Darken(baseColor, 0.45);

        var current = RootGrid.RequestedTheme;
        RootGrid.RequestedTheme = current == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        RootGrid.RequestedTheme = current;
    }

    // ApplicationIcon in the .csproj covers the .exe's own icon (File Explorer,
    // taskbar shortcuts); the live running window's taskbar icon is separate and
    // has to be set on the AppWindow explicitly.
    private void ApplyIcon()
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"));
    }

    private void FavoritesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FavoriteEntry favorite)
        {
            ViewModel.NavigateActiveTab(favorite.Path);
        }
    }

    private void ThisPcButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ActivePane.SelectedTab?.NavigateHome();
    }

    private async void MapNetworkDriveButton_Click(object sender, RoutedEventArgs e)
    {
        var view = new MapNetworkDriveView();
        var dialog = new ContentDialog
        {
            Title = "Mapear unidade de rede",
            Content = view,
            PrimaryButtonText = "Mapear",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!view.TryMap()) args.Cancel = true;
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.ActivePane.SelectedTab?.RefreshCommand.Execute(null);
        }
    }

    private void NewTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.NewTab(ViewModel.ActivePane);
        args.Handled = true;
    }

    private void CloseTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.CloseTab(ViewModel.ActivePane, null);
        args.Handled = true;
    }

    private void NextTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        CycleTab(1);
        args.Handled = true;
    }

    private void PreviousTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        CycleTab(-1);
        args.Handled = true;
    }

    private void CycleTab(int direction)
    {
        var pane = ViewModel.ActivePane;
        if (pane.Tabs.Count == 0) return;
        var index = pane.SelectedTab is null ? 0 : pane.Tabs.IndexOf(pane.SelectedTab);
        index = (index + direction + pane.Tabs.Count) % pane.Tabs.Count;
        pane.SelectedTab = pane.Tabs[index];
    }
}
