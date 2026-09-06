using Mapsui.Styles;
using SkiaSharp;

namespace WayfarerMobile.Services;

/// <summary>Draws the Web badge treatment without importing its layout or ownership system.</summary>
internal static class SegmentBadgeImage
{
    public static (double Width, double Height) Measure(string label) =>
        (label.Length > 1 ? Math.Max(34, 14 + label.Length * 9) : 24, 24);

    public static ImageStyle Create(string label)
    {
        var (width, height) = Measure(label);
        // Match Web's two-times raster density, then display in logical map units.
        using var bitmap = new SKBitmap((int)width * 2, (int)height * 2);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(2);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#0057b8") };
        var bounds = new SKRect(0, 0, (float)width, (float)height);
        canvas.DrawRoundRect(bounds, 12, 12, paint);
        paint.Color = SKColors.White;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 2;
        canvas.DrawRoundRect(bounds, 12, 12, paint);
        paint.Style = SKPaintStyle.Fill;
        using var typeface = SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 12);
        var baseline = (float)height / 2 - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
        canvas.DrawText(label, (float)width / 2, baseline, SKTextAlign.Center, font, paint);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new ImageStyle
        {
            Image = "base64-content://" + Convert.ToBase64String(data.ToArray()),
            SymbolScale = 0.5,
            // Image offsets are upward-positive raster units and scale with the symbol.
            Offset = new Offset(0, 68)
        };
    }
}
