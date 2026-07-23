using Windows.Graphics.Imaging;
using Windows.Storage;

namespace FastExplorer.Services;

public enum ImageOutputFormat { KeepOriginal, Jpeg, Png }

// Batch-resizes and/or converts images using the imaging APIs already built into
// Windows (Windows.Graphics.Imaging's BitmapDecoder/BitmapEncoder) - no third-party
// image library needed, matching this codebase's general preference for the OS/.NET's
// own APIs over new NuGet dependencies. Processes one file at a time (not in parallel,
// unlike GetDrivesAsync/DiskUsageService's fan-out): WinRT's imaging pipeline is
// already fast per image, and several concurrent decode/encode passes would mostly
// just contend for the same disk I/O rather than actually finish sooner.
public static class ImageBatchService
{
    // Used by ImageBatchView to show the source image's current resolution before the
    // user picks a target size - BitmapDecoder.CreateAsync only reads the header/metadata
    // needed for PixelWidth/PixelHeight, not the full pixel data (that's the separate,
    // heavier GetPixelDataAsync call ProcessOneAsync makes), so this is cheap.
    public static async Task<(int Width, int Height)?> TryGetDimensionsAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return ((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        }
        catch (Exception)
        {
            // Windows' WIC-backed imaging APIs throw COMException (unsupported/corrupt
            // codec), not just IOException/UnauthorizedAccessException, for arbitrary
            // user-picked files - this is just a "can't show a size" case, not a bug.
            return null;
        }
    }

    public static async Task<int> ProcessAsync(
        IReadOnlyList<string> sourcePaths, string destinationFolder, int? maxDimension,
        bool allowUpscale, ImageOutputFormat format, CancellationToken cancellationToken)
    {
        StorageFolder destFolder;
        try
        {
            Directory.CreateDirectory(destinationFolder);
            destFolder = await StorageFolder.GetFolderFromPathAsync(destinationFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        var processed = 0;
        foreach (var path in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ProcessOneAsync(path, destFolder, maxDimension, allowUpscale, format);
                processed++;
            }
            // Deliberately broad: BitmapDecoder/BitmapEncoder are thin wrappers over WIC
            // COM codecs, and an unsupported/corrupt/animated image can throw almost
            // anything (COMException, NotSupportedException, ArgumentException, ...) -
            // narrowly-typed catches here would let one bad image in a multi-file batch
            // crash the whole app (this app has no Application.UnhandledException
            // handler - see CLAUDE.md). One file failing should just be skipped.
            catch (Exception)
            {
            }
        }
        return processed;
    }

    private static async Task ProcessOneAsync(string sourcePath, StorageFolder destFolder, int? maxDimension, bool allowUpscale, ImageOutputFormat format)
    {
        var sourceFile = await StorageFile.GetFileFromPathAsync(sourcePath);
        using var inputStream = await sourceFile.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(inputStream);

        var (scaledWidth, scaledHeight) = ScaleToFit(decoder.PixelWidth, decoder.PixelHeight, maxDimension, allowUpscale);
        var needsResize = scaledWidth != (int)decoder.PixelWidth || scaledHeight != (int)decoder.PixelHeight;

        var extension = format switch
        {
            ImageOutputFormat.Jpeg => ".jpg",
            ImageOutputFormat.Png => ".png",
            _ => Path.GetExtension(sourcePath),
        };
        var destFile = await destFolder.CreateFileAsync(
            Path.GetFileNameWithoutExtension(sourcePath) + extension, CreationCollisionOption.GenerateUniqueName);
        using var outputStream = await destFile.OpenAsync(FileAccessMode.ReadWrite);

        BitmapEncoder encoder;
        if (format == ImageOutputFormat.KeepOriginal)
        {
            // Transcoding keeps the source's own codec and metadata (EXIF orientation,
            // etc.) - simpler and more faithful than manually round-tripping raw pixels
            // just to re-save in the same format.
            encoder = await BitmapEncoder.CreateForTranscodingAsync(outputStream, decoder);
        }
        else
        {
            var encoderId = format == ImageOutputFormat.Jpeg ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId;
            encoder = await BitmapEncoder.CreateAsync(encoderId, outputStream);

            // JPEG has no alpha channel. Handing its WIC encoder pixel data still
            // carrying the source's own alpha mode (PNG screenshots are commonly Straight
            // or Premultiplied) doesn't reliably throw a catchable .NET exception - it
            // can fault the native codec and take the whole process down, which is
            // exactly what happened converting a PNG screenshot to JPG before this fix
            // (try/catch around this call, however broad, can't recover from that kind
            // of native-level failure). Requesting pixel data pre-converted to
            // BitmapAlphaMode.Ignore for JPEG output means the JPEG encoder is never
            // handed an alpha channel it can't represent in the first place.
            var alphaMode = format == ImageOutputFormat.Jpeg ? BitmapAlphaMode.Ignore : decoder.BitmapAlphaMode;
            var pixelData = await decoder.GetPixelDataAsync(
                decoder.BitmapPixelFormat, alphaMode, new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
            encoder.SetPixelData(
                decoder.BitmapPixelFormat, alphaMode,
                decoder.PixelWidth, decoder.PixelHeight,
                decoder.DpiX, decoder.DpiY,
                pixelData.DetachPixelData());
        }

        if (needsResize)
        {
            encoder.BitmapTransform.ScaledWidth = (uint)scaledWidth;
            encoder.BitmapTransform.ScaledHeight = (uint)scaledHeight;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        await encoder.FlushAsync();
    }

    // Default (allowUpscale: false) treats maxDimension as a ceiling - only shrinks
    // images already bigger than it, same as most editors' "resize" and safer as a
    // default (an upscaled image just looks soft/blurry, never wrong). With
    // allowUpscale: true, a smaller source is scaled *up* to match instead - the user
    // explicitly opted into that quality trade-off via ImageBatchView's checkbox.
    private static (int Width, int Height) ScaleToFit(uint width, uint height, int? maxDimension, bool allowUpscale)
    {
        if (maxDimension is not { } max) return ((int)width, (int)height);
        if (!allowUpscale && width <= max && height <= max) return ((int)width, (int)height);

        var scale = (double)max / Math.Max(width, height);
        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }
}
