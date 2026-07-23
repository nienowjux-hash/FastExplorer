using FastExplorer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastExplorer.Controls;

// Hosted directly in a ContentDialog from FolderView's context menu (visible only when
// every selected item is an image, per FileTypeCategory.Image). Always writes into a
// "Convertidas" subfolder of the images' own folder rather than overwriting the
// originals - the same "never destroy the source" instinct as the app's other bulk
// operations (Organizar arquivos, Copy). Not nested inside another ContentDialog (unlike
// DiskUsageView/RecycleBinView/DuplicatesView), so no inline-confirm-panel trick needed
// here - this is invoked directly from FolderView's own context menu.
public sealed partial class ImageBatchView : UserControl
{
    private const string OutputFolderName = "Convertidas";

    private readonly IReadOnlyList<string> _sourcePaths;
    private readonly string _destinationFolder;

    public ImageBatchView(IReadOnlyList<string> sourcePaths, string parentFolder)
    {
        InitializeComponent();
        _sourcePaths = sourcePaths;
        _destinationFolder = Path.Combine(parentFolder, OutputFolderName);
        CountText.Text = sourcePaths.Count == 1 ? "1 imagem selecionada" : $"{sourcePaths.Count} imagens selecionadas";
        DestinationText.Text = $"Salvo em: {_destinationFolder}";

        // Only meaningful for a single image - with several selected, sizes typically
        // vary, and probing every one just to show a range isn't worth the extra decode.
        if (sourcePaths.Count == 1) Loaded += async (_, _) => await ShowSourceSizeAsync(sourcePaths[0]);
    }

    private async Task ShowSourceSizeAsync(string path)
    {
        // Same Task.Run fix as RunButton_Click below, and just as needed here: this runs
        // the instant the dialog opens (via Loaded), so without it the crash happened on
        // opening the dialog at all, before ever touching the Converter button.
        var dimensions = await Task.Run(() => ImageBatchService.TryGetDimensionsAsync(path));
        if (dimensions is not { } size) return;

        SourceSizeText.Text = $"Tamanho atual: {size.Width} × {size.Height} px";
        SourceSizeText.Visibility = Visibility.Visible;
    }

    // "Personalizado..." reveals the NumberBox for a free-typed value; every other
    // preset sets the dimension directly and hides it - most people want one of a
    // handful of common sizes, not to type an exact pixel count from scratch. The
    // upscale checkbox only makes sense once a target size is actually chosen.
    private void SizePresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SizePresetCombo's SelectedIndex="0" in XAML fires this once during
        // InitializeComponent() itself, before MaxDimensionBox/AllowUpscaleCheck
        // (declared later in the XAML) have been assigned by the generated field-wiring
        // code - this was throwing a NullReferenceException that aborted the constructor
        // entirely (root cause found via App.xaml.cs's crash log after it caught what
        // would otherwise have been a whole-app crash).
        if (MaxDimensionBox is null || AllowUpscaleCheck is null) return;

        var sizeTag = (SizePresetCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        MaxDimensionBox.Visibility = sizeTag == "custom" ? Visibility.Visible : Visibility.Collapsed;
        AllowUpscaleCheck.Visibility = string.IsNullOrEmpty(sizeTag) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        LoadingRing.IsActive = true;
        StatusText.Text = "Processando...";

        var format = ((FormatCombo.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "Jpeg" => ImageOutputFormat.Jpeg,
            "Png" => ImageOutputFormat.Png,
            _ => ImageOutputFormat.KeepOriginal,
        };

        var sizeTag = (SizePresetCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        int? maxDimension = sizeTag switch
        {
            null or "" => null,
            "custom" => double.IsNaN(MaxDimensionBox.Value) ? null : (int)MaxDimensionBox.Value,
            _ => int.Parse(sizeTag),
        };

        var allowUpscale = AllowUpscaleCheck.IsChecked == true;

        int processed;
        try
        {
            // Task.Run, not a direct await, matching every other heavy operation in this
            // app (DeleteSelectedAsync, DiskUsageService, OrganizeService, ...): running
            // BitmapDecoder/BitmapEncoder's native decode/encode work directly on the UI
            // thread's async continuation chain - while hosted inside an open
            // ContentDialog - was the actual cause of a hard crash here (confirmed via
            // Windows' Application event log: 0xc000027b/STOWED_EXCEPTION faulting in
            // Microsoft.UI.Xaml.dll itself, not in the imaging code - a reentrancy fault
            // in the native XAML engine, not a catchable .NET exception, which is why
            // wrapping the call in try/catch alone didn't fix it).
            processed = await Task.Run(() =>
                ImageBatchService.ProcessAsync(_sourcePaths, _destinationFolder, maxDimension, allowUpscale, format, CancellationToken.None));
        }
        // Belt-and-suspenders on top of ProcessAsync's own broad catch and App.xaml.cs's
        // global UnhandledException handler: this is an async void event handler, so
        // anything escaping here would otherwise have no local recovery path.
        catch (Exception ex)
        {
            LoadingRing.IsActive = false;
            RunButton.IsEnabled = true;
            StatusText.Text = $"Falha: {ex.Message}";
            return;
        }

        LoadingRing.IsActive = false;
        RunButton.IsEnabled = true;
        StatusText.Text = processed == _sourcePaths.Count
            ? $"{processed} imagem(ns) processada(s)."
            : $"{processed} de {_sourcePaths.Count} imagem(ns) processada(s).";
    }
}
