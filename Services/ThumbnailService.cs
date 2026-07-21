using Microsoft.UI.Xaml.Media.Imaging;

namespace FastExplorer.Services;

// Small, on-demand image thumbnails for the file list. Deliberately not shell
// thumbnail extraction (IThumbnailProvider) - just decoding the image itself at a
// tiny target size, and only ever for the item actually being realized on screen
// (see FolderView's ListView.ContainerContentChanging), so it never runs ahead for
// off-screen rows in a big folder.
public static class ThumbnailService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp",
    };

    public static bool IsImage(string extension) => ImageExtensions.Contains(extension);

    public static async Task<BitmapImage?> LoadThumbnailAsync(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            // Decoding at a small pixel width is what keeps this cheap even for a
            // multi-megapixel photo - the decoder downsamples during decode.
            var bitmap = new BitmapImage { DecodePixelWidth = 32 };
            await bitmap.SetSourceAsync(memoryStream.AsRandomAccessStream());
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
