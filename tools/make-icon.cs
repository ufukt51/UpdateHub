#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false

// UpdateHub ikonunu üretir: dotnet run tools/make-icon.cs -- <çıktı.ico> [önizleme.png]
// Her boyut WPF ile çizilir ve PNG olarak ICO içine paketlenir (Vista+ biçimi).

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var output = args.Length > 0 ? args[0] : "UpdateHub.ico";
var preview = args.Length > 1 ? args[1] : null;

// WPF görselleri STA iş parçacığı ister.
var thread = new Thread(() => Run(output, preview));
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

static void Run(string output, string? preview)
{
    int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
    var images = sizes.Select(s => (Size: s, Png: Render(s))).ToList();

    using (var fs = File.Create(output))
    using (var w = new BinaryWriter(fs))
    {
        w.Write((short)0);            // reserved
        w.Write((short)1);            // tür: ikon
        w.Write((short)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);         // palet yok
            w.Write((byte)0);
            w.Write((short)1);        // düzlem
            w.Write((short)32);       // bpp
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images)
            w.Write(png);
    }

    if (preview is not null)
        File.WriteAllBytes(preview, images.Single(i => i.Size == 256).Png);

    Console.WriteLine($"{output}: {images.Count} boyut");
}

static byte[] Render(int size)
{
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));

        var background = new LinearGradientBrush(
            Color.FromRgb(0x1E, 0x90, 0xE8), Color.FromRgb(0x5B, 0x3F, 0xD1), new Point(0, 0), new Point(1, 1));
        dc.DrawRoundedRectangle(background, null, new Rect(8, 8, 240, 240), 56, 56);

        var white = Brushes.White;
        // Tüm boyutlar aynı tasarım (pencere içi ve dışı aynı görünsün); küçüklerde çizgi kalınlaşır.
        var stroke = new Pen(white, size <= 24 ? 26 : 22) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

        // Yenileme halkası: tepede boşluk bırakan 280°'lik yay.
        var ring = new PathGeometry([new PathFigure(new Point(174.3, 72.8),
            [new ArcSegment(new Point(81.7, 72.8), new Size(72, 72), 0, true, SweepDirection.Clockwise, true)], false)]);
        dc.DrawGeometry(null, stroke, ring);

        // Halkanın ucundaki ok başı (saat yönünde teğet).
        var head = new PathGeometry([new PathFigure(new Point(108, 50),
            [new LineSegment(new Point(62, 51), true), new LineSegment(new Point(97, 96), true)], true)]);
        dc.DrawGeometry(white, new Pen(white, 6) { LineJoin = PenLineJoin.Round }, head);

        // Ortadaki yukarı ok.
        var (top, bottom, wing) = (98.0, 168.0, 30.0);
        dc.DrawLine(stroke, new Point(128, bottom), new Point(128, top));
        dc.DrawGeometry(null, stroke, new PathGeometry([new PathFigure(new Point(128 - wing, top + wing),
            [new LineSegment(new Point(128, top), true), new LineSegment(new Point(128 + wing, top + wing), true)], false)]));

        dc.Pop();
    }

    var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var ms = new MemoryStream();
    encoder.Save(ms);
    return ms.ToArray();
}
