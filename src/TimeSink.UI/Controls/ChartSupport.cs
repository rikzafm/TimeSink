using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TimeSink.UI.Controls;

/// <summary>
/// Helpers shared by the drawn (OnRender) controls: label text with the theme font and a theme fallback brush, the
/// invisible hit-test fill, and rectangle inflate/deflate. Fallbacks come from theme resources, never from literals.
/// </summary>
internal static class ChartSupport
{
    /// <summary>Label typeface: the control's font, else the theme UI font resource, else the system UI font.</summary>
    public static Typeface Typeface(FrameworkElement owner, FontFamily? family, FontWeight weight)
    {
        var face = family ?? owner.TryFindResource("FontFamily.Ui") as FontFamily ?? SystemFonts.MessageFontFamily;
        return new Typeface(face, FontStyles.Normal, weight, FontStretches.Normal);
    }

    /// <summary>One line of label text; a missing brush falls back to the theme's muted text brush.</summary>
    public static FormattedText Text(FrameworkElement owner, string text, FontFamily? family, double size, Brush? brush, FontWeight weight)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface(owner, family, weight), size,
            brush ?? owner.TryFindResource("Brush.TextMuted") as Brush ?? Brushes.Transparent,
            VisualTreeHelper.GetDpi(owner).PixelsPerDip);

    /// <summary>A theme brush by resource key; transparent when the key is missing.</summary>
    public static Brush Brush(FrameworkElement owner, string key) => owner.TryFindResource(key) as Brush ?? Brushes.Transparent;

    /// <summary>Makes the whole area hit-testable (WPF only hit-tests drawn pixels).</summary>
    public static void HitArea(DrawingContext dc, double width, double height)
        => dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

    public static Rect Deflate(Rect r, double d) => new(r.Left + d, r.Top + d, Math.Max(0, r.Width - 2 * d), Math.Max(0, r.Height - 2 * d));
    public static Rect Inflate(Rect r, double d) => Deflate(r, -d);
}
