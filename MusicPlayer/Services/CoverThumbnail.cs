namespace MusicPlayer.Services;

internal static class CoverThumbnail
{
    public static CoverArt Create(CoverArt cover)
    {
#if ANDROID
        // Sample before decoding so a large source image never needs a full-size bitmap.
        using var bounds = new global::Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        global::Android.Graphics.BitmapFactory.DecodeByteArray(cover.Bytes, 0, cover.Bytes.Length, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
            throw new InvalidDataException("The cover image could not be decoded.");
        var sample = 1;
        while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= 512) sample *= 2;
        using var options = new global::Android.Graphics.BitmapFactory.Options { InSampleSize = sample };
        using var bitmap = global::Android.Graphics.BitmapFactory.DecodeByteArray(cover.Bytes, 0, cover.Bytes.Length, options)
            ?? throw new InvalidDataException("The cover image could not be decoded.");
        var scale = Math.Min(1, 512.0 / Math.Max(bitmap.Width, bitmap.Height));
        using var resized = global::Android.Graphics.Bitmap.CreateScaledBitmap(bitmap,
            Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)), true);
        using var output = new MemoryStream();
        resized!.Compress(global::Android.Graphics.Bitmap.CompressFormat.Jpeg!, 85, output);
        return new CoverArt(output.ToArray(), "image/jpeg");
#elif IOS || MACCATALYST || WINDOWS
        using var input = new MemoryStream(cover.Bytes);
        using var image = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(input);
        using var resized = image.Downsize(512, 512);
        using var output = new MemoryStream();
        resized.Save(output, Microsoft.Maui.Graphics.ImageFormat.Jpeg, 0.85f);
        return new CoverArt(output.ToArray(), "image/jpeg");
#else
        // Desktop regression tests exercise storage with small image fixtures.
        return cover;
#endif
    }
}
