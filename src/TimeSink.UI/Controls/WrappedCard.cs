using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TimeSink.UI.Controls;

/// <summary>A small tile (value + caption) at the foot of the "total hours" card.</summary>
public sealed record WrappedTile(string Value, string Caption);

/// <summary>One podium column. <c>Rank</c> is 1-5; <c>Hours</c> is the pre-composed time ("165h"); <c>Icon</c> is the app's own logo, or null to show the letter.</summary>
public sealed record WrappedPodiumEntry(string Letter, string Name, string Hours, int Rank, ImageSource? Icon = null, int? PaletteSlot = null);

/// <summary>A habit tile: small caps key, big value, one-line sub.</summary>
public sealed record WrappedHabit(string Key, string Value, string Sub);

/// <summary>What a <see cref="WrappedCard"/> draws. All text is composed by the view from the string table.</summary>
public abstract record WrappedCardModel(string Label);

public sealed record TotalCardModel(string Label, string Big, string Unit, string Line, string LineHighlight, IReadOnlyList<WrappedTile> Tiles)
    : WrappedCardModel(Label);

/// <param name="Entries">In display order (rank 4, 2, 1, 3, 5: the winner in the middle).</param>
/// <param name="Then">Runs of the footer line; <c>Soft</c> runs are brighter.</param>
public sealed record TopAppsCardModel(string Label, string Headline, IReadOnlyList<WrappedPodiumEntry> Entries, IReadOnlyList<(string Text, bool Soft)> Then)
    : WrappedCardModel(Label);

public sealed record HabitsCardModel(string Label, string Headline, IReadOnlyList<WrappedHabit> Habits) : WrappedCardModel(Label);

public sealed record EmptyCardModel(string Label, string Message) : WrappedCardModel(Label);

/// <summary>
/// One Wrapped card, drawn with <see cref="DrawingContext"/> primitives (board PulseYearWrapped). Every colour, font and
/// size comes from DPs the view binds to theme resources; the text comes pre-composed in the model. Because the whole
/// card is drawn here, rendering this element alone to a bitmap gives the shareable image.
/// </summary>
public sealed class WrappedCard : FrameworkElement
{
    public const double CardHeight = 392, FloorHeight = 300;
    private const double Pad = 20, PadBottom = 18, Gap = 10;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(WrappedCard),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ModelProperty = Reg<WrappedCardModel?>(nameof(Model));
    public static readonly DependencyProperty BackBrushProperty = Reg<Brush?>(nameof(BackBrush));
    public static readonly DependencyProperty TileBackBrushProperty = Reg<Brush?>(nameof(TileBackBrush));
    public static readonly DependencyProperty LineBrushProperty = Reg<Brush?>(nameof(LineBrush));
    public static readonly DependencyProperty TextBrushProperty = Reg<Brush?>(nameof(TextBrush));
    public static readonly DependencyProperty SoftBrushProperty = Reg<Brush?>(nameof(SoftBrush));
    public static readonly DependencyProperty MutedBrushProperty = Reg<Brush?>(nameof(MutedBrush));
    public static readonly DependencyProperty AccentBrushProperty = Reg<Brush?>(nameof(AccentBrush));
    public static readonly DependencyProperty OnAccentBrushProperty = Reg<Brush?>(nameof(OnAccentBrush));
    public static readonly DependencyProperty AppBrushesProperty = Reg<IReadOnlyList<Brush>?>(nameof(AppBrushes));
    public static readonly DependencyProperty FontFaceProperty = Reg<FontFamily?>(nameof(FontFace));
    public static readonly DependencyProperty NumberFontFaceProperty = Reg<FontFamily?>(nameof(NumberFontFace));
    public static readonly DependencyProperty SmallSizeProperty = Reg(nameof(SmallSize), 11.0);
    public static readonly DependencyProperty BodySizeProperty = Reg(nameof(BodySize), 13.0);
    public static readonly DependencyProperty TitleSizeProperty = Reg(nameof(TitleSize), 15.0);
    public static readonly DependencyProperty DisplaySizeProperty = Reg(nameof(DisplaySize), 34.0);
    public static readonly DependencyProperty MediumRadiusProperty = Reg(nameof(MediumRadius), 0.0);
    public static readonly DependencyProperty LargeRadiusProperty = Reg(nameof(LargeRadius), 0.0);

    public WrappedCardModel? Model { get => (WrappedCardModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public Brush? BackBrush { get => (Brush?)GetValue(BackBrushProperty); set => SetValue(BackBrushProperty, value); }
    public Brush? TileBackBrush { get => (Brush?)GetValue(TileBackBrushProperty); set => SetValue(TileBackBrushProperty, value); }
    public Brush? LineBrush { get => (Brush?)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public Brush? TextBrush { get => (Brush?)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush? SoftBrush { get => (Brush?)GetValue(SoftBrushProperty); set => SetValue(SoftBrushProperty, value); }
    public Brush? MutedBrush { get => (Brush?)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public Brush? AccentBrush { get => (Brush?)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public Brush? OnAccentBrush { get => (Brush?)GetValue(OnAccentBrushProperty); set => SetValue(OnAccentBrushProperty, value); }
    public IReadOnlyList<Brush>? AppBrushes { get => (IReadOnlyList<Brush>?)GetValue(AppBrushesProperty); set => SetValue(AppBrushesProperty, value); }
    public FontFamily? FontFace { get => (FontFamily?)GetValue(FontFaceProperty); set => SetValue(FontFaceProperty, value); }
    public FontFamily? NumberFontFace { get => (FontFamily?)GetValue(NumberFontFaceProperty); set => SetValue(NumberFontFaceProperty, value); }
    public double SmallSize { get => (double)GetValue(SmallSizeProperty); set => SetValue(SmallSizeProperty, value); }
    public double BodySize { get => (double)GetValue(BodySizeProperty); set => SetValue(BodySizeProperty, value); }
    public double TitleSize { get => (double)GetValue(TitleSizeProperty); set => SetValue(TitleSizeProperty, value); }
    public double DisplaySize { get => (double)GetValue(DisplaySizeProperty); set => SetValue(DisplaySizeProperty, value); }
    public double MediumRadius { get => (double)GetValue(MediumRadiusProperty); set => SetValue(MediumRadiusProperty, value); }
    public double LargeRadius { get => (double)GetValue(LargeRadiusProperty); set => SetValue(LargeRadiusProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 388 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? CardHeight : Math.Clamp(availableSize.Height, FloorHeight, CardHeight));

    private FormattedText Text(string text, double size, Brush? brush, bool bold = false, bool numbers = false, double maxWidth = 0, int maxLines = 0)
    {
        var ft = ChartSupport.Text(this, text, numbers ? NumberFontFace : FontFace, size, brush ?? TextBrush,
            bold ? FontWeights.Bold : FontWeights.Normal);
        if (maxWidth > 0) ft.MaxTextWidth = maxWidth;
        ft.Trimming = TextTrimming.CharacterEllipsis;
        if (maxLines > 0) ft.MaxLineCount = maxLines;
        return ft;
    }

    /// <summary>Text that shrinks (down to 12) until it fits <paramref name="maxWidth"/> on one line.</summary>
    private FormattedText Fit(string text, double size, Brush? brush, double maxWidth, bool bold = true)
    {
        var ft = Text(text, size, brush, bold, numbers: true);
        while (ft.WidthIncludingTrailingWhitespace > maxWidth && size > 12) ft = Text(text, size -= 1, brush, bold, numbers: true);
        return ft;
    }

    private Pen? _pen;
    private Pen Line() => _pen = new Pen(LineBrush, 1);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var outer = new Rect(0.5, 0.5, w - 1, h - 1);
        dc.DrawRoundedRectangle(BackBrush, Line(), outer, LargeRadius + 6, LargeRadius + 6);
        dc.PushClip(new RectangleGeometry(outer, LargeRadius + 6, LargeRadius + 6));
        DrawDither(dc, w);
        switch (Model)
        {
            case TotalCardModel m: DrawTotal(dc, m, w, h); break;
            case TopAppsCardModel m: DrawTop(dc, m, w, h); break;
            case HabitsCardModel m: DrawHabits(dc, m, w, h); break;
            case EmptyCardModel m: DrawEmpty(dc, m, w); break;
        }
        dc.Pop();
    }

    /// <summary>The dotted accent texture in the top-right corner, fading toward the bottom left.</summary>
    private void DrawDither(DrawingContext dc, double w)
    {
        for (double x = w - 200; x < w; x += 10)
            for (double y = 0; y < 170; y += 10)
            {
                var t = ((w - x) / 200 + y / 170) / 2; // 0 at the top-right corner, 1 at the far corner
                var a = 0.35 * Math.Max(0, 1 - t / 0.7);
                if (a <= 0.01) continue;
                dc.PushOpacity(a);
                dc.DrawEllipse(AccentBrush, null, new Point(x + 5, y + 5), 1.4, 1.4);
                dc.Pop();
            }
    }

    private void DrawLabel(DrawingContext dc, string label)
    {
        dc.DrawText(Text(label, SmallSize + 1, MutedBrush, bold: true), new Point(Pad, Pad));
    }

    private void DrawTile(DrawingContext dc, Rect r, double radius, Brush? fill)
    {
        dc.DrawRoundedRectangle(fill, Line(), r, radius, radius);
    }

    private void DrawEmpty(DrawingContext dc, EmptyCardModel m, double w)
    {
        DrawLabel(dc, m.Label);
        var msg = Text(m.Message, TitleSize + 3, SoftBrush, bold: true, maxWidth: w - 2 * Pad);
        dc.DrawText(msg, new Point(Pad, Pad + 40));
    }

    private void DrawTotal(DrawingContext dc, TotalCardModel m, double w, double h)
    {
        DrawLabel(dc, m.Label);
        var cw = w - 2 * Pad;
        var unit = Text(m.Unit, DisplaySize * 0.76, TextBrush, bold: true);
        var bigSize = DisplaySize * 3;
        var big = Text(m.Big, bigSize, AccentBrush, bold: true, numbers: true);
        while (big.WidthIncludingTrailingWhitespace + 8 + unit.Width > cw && bigSize > DisplaySize) // very large totals shrink to fit
            big = Text(m.Big, bigSize -= 4, AccentBrush, bold: true, numbers: true);
        var bigY = Pad + 18;
        dc.DrawText(big, new Point(Pad - 3, bigY));
        dc.DrawText(unit, new Point(Pad - 3 + big.WidthIncludingTrailingWhitespace + 8, bigY + big.Baseline - unit.Baseline));

        var line = Text(m.Line, TitleSize * 1.2, TextBrush, bold: true, maxWidth: Math.Min(cw, 320));
        if (m.LineHighlight.Length > 0 && m.Line.IndexOf(m.LineHighlight, StringComparison.Ordinal) is var i and >= 0)
            line.SetForegroundBrush(AccentBrush, i, m.LineHighlight.Length);
        dc.DrawText(line, new Point(Pad, bigY + big.Height + 6));

        var tiles = m.Tiles;
        if (tiles.Count == 0) return;
        var tw = (cw - Gap * (tiles.Count - 1)) / tiles.Count;
        var parts = tiles.Select(t => (Value: Text(t.Value, TitleSize * 1.2, TextBrush, bold: true, numbers: true),
            Caption: Text(t.Caption, SmallSize, MutedBrush, maxWidth: tw - 24, maxLines: 2))).ToList();
        var th = 16 + parts.Max(p => p.Value.Height + p.Caption.Height);
        var top = h - PadBottom - th;
        for (var k = 0; k < parts.Count; k++)
        {
            var x = Pad + k * (tw + Gap);
            DrawTile(dc, new Rect(x + 0.5, top + 0.5, tw - 1, th - 1), MediumRadius, null);
            dc.DrawText(parts[k].Value, new Point(x + 12, top + 8));
            dc.DrawText(parts[k].Caption, new Point(x + 12, top + 8 + parts[k].Value.Height));
        }
    }

    private void DrawTop(DrawingContext dc, TopAppsCardModel m, double w, double h)
    {
        DrawLabel(dc, m.Label);
        var cw = w - 2 * Pad;
        var head = Text(m.Headline, TitleSize * 1.33, TextBrush, bold: true, maxWidth: cw, maxLines: 1);
        dc.DrawText(head, new Point(Pad, Pad + 22));

        // Footer: separator + "Then ..." runs.
        var thenText = string.Concat(m.Then.Select(r => r.Text));
        var then = Text(thenText, SmallSize + 1, MutedBrush, maxWidth: cw, maxLines: 2);
        var pos = 0;
        foreach (var (text, soft) in m.Then)
        {
            if (soft) then.SetForegroundBrush(SoftBrush, pos, text.Length);
            pos += text.Length;
        }
        var footTop = h - PadBottom - then.Height - 10;
        if (thenText.Length > 0)
        {
            dc.DrawLine(Line(), new Point(Pad, footTop), new Point(w - Pad, footTop));
            dc.DrawText(then, new Point(Pad, footTop + 10));
        }
        else footTop = h - PadBottom;

        var n = m.Entries.Count;
        if (n == 0) return;
        var slot = Math.Min((cw - 2 * Gap) / 3, (cw - (n - 1) * Gap) / n);
        var groupW = n * slot + (n - 1) * Gap;
        var x0 = Pad + (cw - groupW) / 2;
        var baseY = footTop - 12;
        var brushes = AppBrushes;
        for (var k = 0; k < n; k++)
        {
            var e = m.Entries[k];
            var x = x0 + k * (slot + Gap);
            var cx = x + slot / 2;
            var color = brushes is { Count: > 0 } ? brushes[(e.PaletteSlot ?? e.Rank - 1) % brushes.Count] : AccentBrush;
            var pillarH = e.Rank switch { 1 => 108.0, 2 => 84.0, 3 => 68.0, 4 => 54.0, _ => 42.0 };
            dc.DrawGeometry(color, null, TopRounded(new Rect(x, baseY - pillarH, slot, pillarH), 8));
            var rank = Text(e.Rank.ToString(CultureInfo.CurrentCulture), DisplaySize * 0.76, OnAccentBrush, bold: true, numbers: true);
            dc.DrawText(rank, new Point(cx - rank.Width / 2, baseY - pillarH + 6));

            var hours = Text(e.Hours, SmallSize + 1, SoftBrush, numbers: true, maxWidth: slot, maxLines: 1);
            var hoursY = baseY - pillarH - 4 - hours.Height;
            dc.DrawText(hours, new Point(cx - Math.Min(slot, hours.Width) / 2, hoursY));
            var name = Text(e.Name, BodySize, TextBrush, bold: true, maxWidth: slot, maxLines: 1);
            var nameY = hoursY - 4 - name.Height;
            dc.DrawText(name, new Point(cx - Math.Min(slot, name.Width) / 2, nameY));
            var tileR = new Rect(cx - 18, nameY - 4 - 36, 36, 36);
            if (e.Icon is { } icon)
                dc.DrawImage(icon, tileR); // the logo alone, no tile behind it
            else
            {
                DrawTile(dc, tileR, MediumRadius, TileBackBrush);
                var letter = Text(e.Letter, TitleSize + 2, color, bold: true);
                dc.DrawText(letter, new Point(cx - letter.Width / 2, tileR.Y + (36 - letter.Height) / 2));
            }
        }
    }

    private static Geometry TopRounded(Rect r, double radius)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left, r.Bottom), true, true);
            c.LineTo(new Point(r.Left, r.Top + radius), true, false);
            c.ArcTo(new Point(r.Left + radius, r.Top), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(r.Right - radius, r.Top), true, false);
            c.ArcTo(new Point(r.Right, r.Top + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(r.Right, r.Bottom), true, false);
        }
        g.Freeze();
        return g;
    }

    private void DrawHabits(DrawingContext dc, HabitsCardModel m, double w, double h)
    {
        DrawLabel(dc, m.Label);
        var cw = w - 2 * Pad;
        var head = Text(m.Headline, TitleSize * 1.33, TextBrush, bold: true, maxWidth: cw, maxLines: 1);
        dc.DrawText(head, new Point(Pad, Pad + 22));

        var top = Pad + 22 + head.Height + 14;
        var rows = (m.Habits.Count + 1) / 2;
        if (rows == 0) return;
        var th = (h - PadBottom - top - Gap * (rows - 1)) / rows;
        var tw = (cw - Gap) / 2;
        for (var k = 0; k < m.Habits.Count; k++)
        {
            var x = Pad + k % 2 * (tw + Gap);
            var y = top + k / 2 * (th + Gap);
            DrawTile(dc, new Rect(x + 0.5, y + 0.5, tw - 1, th - 1), LargeRadius, TileBackBrush);
            var hb = m.Habits[k];
            dc.DrawText(Text(hb.Key, SmallSize, MutedBrush, maxWidth: tw - 24, maxLines: 1), new Point(x + 12, y + 12));
            var value = Fit(hb.Value, DisplaySize * 0.76, AccentBrush, tw - 24);
            dc.DrawText(value, new Point(x + 12, y + (th - value.Height) / 2));
            var sub = Text(hb.Sub, SmallSize + 1, SoftBrush, maxWidth: tw - 24, maxLines: 1);
            dc.DrawText(sub, new Point(x + 12, y + th - 12 - sub.Height));
        }
    }
}
