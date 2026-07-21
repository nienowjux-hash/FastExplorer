using FastExplorer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FastExplorer.Controls;

public sealed partial class PreviewPane : UserControl
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".cs", ".cpp", ".c", ".h", ".js", ".ts", ".py",
        ".java", ".json", ".xml", ".html", ".css", ".csv", ".yaml", ".yml", ".ini", ".config",
    };

    private const long MaxPreviewBytes = 2 * 1024 * 1024; // 2 MB

    private int _requestVersion;

    public PreviewPane()
    {
        InitializeComponent();
    }

    public async void SetItem(FileSystemItem? item)
    {
        var version = ++_requestVersion;
        HideAll();

        if (item is null)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        InfoName.Text = item.Name;
        InfoDetails.Text = item.IsDirectory
            ? item.DateModified.ToString("g")
            : $"{item.SizeDisplay}  ·  {item.DateModified:g}";

        if (item.IsDirectory)
        {
            GenericState.Visibility = Visibility.Visible;
            GenericIcon.Glyph = item.IconGlyph;
            GenericName.Text = item.Name;
            return;
        }

        if (ImageExtensions.Contains(item.Extension))
        {
            try
            {
                var bitmap = new BitmapImage();
                using (var stream = File.OpenRead(item.FullPath))
                {
                    var memStream = new MemoryStream();
                    await stream.CopyToAsync(memStream);
                    memStream.Position = 0;
                    if (version != _requestVersion) return;
                    await bitmap.SetSourceAsync(memStream.AsRandomAccessStream());
                }
                if (version != _requestVersion) return;
                ImagePreview.Source = bitmap;
                ImagePreview.Visibility = Visibility.Visible;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowGenericFallback(item);
            }
            return;
        }

        if (TextExtensions.Contains(item.Extension) && item.SizeBytes <= MaxPreviewBytes)
        {
            try
            {
                var text = await File.ReadAllTextAsync(item.FullPath);
                if (version != _requestVersion) return;
                TextPreview.Text = text;
                TextPreviewScroller.Visibility = Visibility.Visible;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowGenericFallback(item);
            }
            return;
        }

        ShowGenericFallback(item);
    }

    private void ShowGenericFallback(FileSystemItem item)
    {
        GenericState.Visibility = Visibility.Visible;
        GenericIcon.Glyph = item.IconGlyph;
        GenericName.Text = item.Name;
    }

    private void HideAll()
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ImagePreview.Visibility = Visibility.Collapsed;
        ImagePreview.Source = null;
        TextPreviewScroller.Visibility = Visibility.Collapsed;
        GenericState.Visibility = Visibility.Collapsed;
        InfoName.Text = string.Empty;
        InfoDetails.Text = string.Empty;
    }
}
