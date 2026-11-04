using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RenderTest;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var icons = new (string Name, string PathData)[]
        {
            // Social Media: Chat bubbles
            ("social", "M15.5,2 H3.5 A2.5,2.5 0 0 0 1,4.5 V15 L4,19.5 L7.5,15 H15.5 A2.5,2.5 0 0 0 18,12.5 V4.5 A2.5,2.5 0 0 0 15.5,2 Z M18,7 H21 A2,2 0 0 1 23,9 V16.5 L20,21.5 L16.5,17 H12"),
            
            // Browsing: Compass
            ("browsing", "M12,1 A11,11 0 1 0 12,23 A11,11 0 1 0 12,1 Z M16.2,7.4 L14.6,13.0 L7.4,16.2 L9.0,10.6 Z"),
            
            // Coding: Code brackets
            ("coding", "M7,4.5 L0.8,11.2 L7,18 M17,4.5 L23.2,11.2 L17,18 M14.2,2 L9.8,20.5"),
            
            // Productivity: Checklist
            ("prod", "M6,1.5 H18 A4,4 0 0 1 22,5.5 V18.5 A4,4 0 0 1 18,22.5 H6 A4,4 0 0 1 2,18.5 V5.5 A4,4 0 0 1 6,1.5 Z M6.5,8.5 L8.5,10.5 L12,7 M14,9 H18 M6.5,15.5 L8.5,17.5 L12,14 M14,16 H18"),
            
            // Entertainment: Clapperboard
            ("ent", "M1,9 H23 V19 A2,2 0 0 1 21,21 H3 A2,2 0 0 1 1,19 Z M3.8,2.2 L22.2,5.8 L21.6,8.5 H1.5 Z M7.5,3 L6.2,8.5 M14,4.5 L13,8.5 M9.5,12.5 L15,15.5 L9.5,18.5 Z")
        };

        var outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rendered");
        Directory.CreateDirectory(outDir);

        var strokeColor = Color.FromRgb(184, 240, 75);
        var bgColor = Color.FromRgb(28, 31, 36);

        foreach (var (name, pathData) in icons)
        {
            RenderIcon(name, pathData, 60, 2.5, 1.75, strokeColor, bgColor, Path.Combine(outDir, $"{name}_rendered_60.png"));
            RenderIcon(name, pathData, 18, 0.75, 1.75, strokeColor, bgColor, Path.Combine(outDir, $"{name}_rendered_18.png"));
        }

        Console.WriteLine("All rendered successfully to " + outDir);
    }

    private static void RenderIcon(string name, string pathData, int pixelSize, double scale, double strokeThickness, Color strokeColor, Color bgColor, string outPath)
    {
        var geom = Geometry.Parse(pathData);
        var pen = new Pen(new SolidColorBrush(strokeColor), strokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(bgColor), null, new Rect(0, 0, pixelSize, pixelSize));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(null, pen, geom);
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(outPath);
        enc.Save(fs);
    }
}
