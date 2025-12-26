using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace ScummEditor.AvaloniaApp.Views
{
  public partial class BoxOverlayView : UserControl
  {
    private Image? _image;
    private Canvas? _overlay;
    private IReadOnlyList<ScaleSlot> _scaleSlots = Array.Empty<ScaleSlot>();

    public BoxOverlayView()
    {
      InitializeComponent();
      _image = this.FindControl<Image>("BackgroundImage");
      _overlay = this.FindControl<Canvas>("Overlay");
    }

    private void InitializeComponent()
    {
      AvaloniaXamlLoader.Load(this);
    }

    public void Load(AvaloniaBitmap background, IReadOnlyList<WalkBox> boxes, IReadOnlyList<ScaleSlot>? scaleSlots = null)
    {
      _scaleSlots = scaleSlots ?? Array.Empty<ScaleSlot>();

      if (_image != null)
      {
        _image.Source = background;
      }

      if (_overlay != null)
      {
        _overlay.Children.Clear();
        var pixelSize = background.PixelSize;
        var dpi = background.Dpi;
        double dipScaleX = dpi.X > 0 ? 96.0 / dpi.X : 1.0;
        double dipScaleY = dpi.Y > 0 ? 96.0 / dpi.Y : 1.0;

        double targetWidth = pixelSize.Width * dipScaleX;
        double targetHeight = pixelSize.Height * dipScaleY;

        _overlay.Width = targetWidth;
        _overlay.Height = targetHeight;
        if (_image != null)
        {
          _image.Width = targetWidth;
          _image.Height = targetHeight;
        }

        int slotCount = _scaleSlots.Count;

        foreach (var box in boxes)
        {
          var poly = BuildPolygon(box, pixelSize.Width, pixelSize.Height, dipScaleX, dipScaleY, slotCount, out var centroid);
          if (poly != null)
          {
            _overlay.Children.Add(poly);
            var label = new TextBlock
            {
              Text = BuildLabel(box),
              Foreground = Brushes.White,
              FontSize = 12,
              FontWeight = FontWeight.Bold
            };
            Canvas.SetLeft(label, centroid.X - 10);
            Canvas.SetTop(label, centroid.Y - 8);
            _overlay.Children.Add(label);
          }
        }
      }
    }

    private Polygon? BuildPolygon(WalkBox box, double maxWidthPx, double maxHeightPx, double dipScaleX, double dipScaleY, int slotCount, out Point centroid)
    {
      centroid = default;
      var pts = box.Points;
      if (pts.Count == 0) return null;

      var list = new Avalonia.Collections.AvaloniaList<Point>();
      foreach (var p in pts)
      {
        if (p.X <= -32000 && p.Y <= -32000) continue;
        var x = Math.Clamp(p.X, 0, maxWidthPx) * dipScaleX;
        var y = Math.Clamp(p.Y, 0, maxHeightPx) * dipScaleY;
        list.Add(new Point(x, y));
      }

      if (list.Count < 3) return null;

      double sumX = 0, sumY = 0;
      foreach (var pt in list)
      {
        sumX += pt.X;
        sumY += pt.Y;
      }
      centroid = new Point(sumX / list.Count, sumY / list.Count);

      var strokeBrush = GetBrush(box, slotCount);
      var poly = new Polygon
      {
        Points = list,
        Stroke = strokeBrush,
        StrokeThickness = 1.25,
        Fill = new SolidColorBrush(Color.FromArgb(30, strokeBrush.Color.R, strokeBrush.Color.G, strokeBrush.Color.B))
      };
      return poly;
    }

    private ISolidColorBrush GetBrush(WalkBox box, int slotCount)
    {
      if (!box.UsesScaleSlot)
      {
        return Brushes.Lime;
      }

      if (slotCount <= 0)
      {
        return Brushes.DeepSkyBlue;
      }

      int slot = Math.Max(0, box.ScaleSlot);
      // Spread hues across slot count; fallback to golden ratio to avoid repeats.
      double hue = (slot * 137.508) % 360.0;
      return new SolidColorBrush(ColorFromHsv(hue, 0.85, 0.9));
    }

    private static Color ColorFromHsv(double h, double s, double v)
    {
      h = h % 360;
      double c = v * s;
      double x = c * (1 - Math.Abs((h / 60 % 2) - 1));
      double m = v - c;

      (double r, double g, double b) = h switch
      {
        < 60 => (c, x, 0d),
        < 120 => (x, c, 0d),
        < 180 => (0d, c, x),
        < 240 => (0d, x, c),
        < 300 => (x, 0d, c),
        _ => (c, 0d, x)
      };

      byte R = (byte)Math.Clamp((r + m) * 255, 0, 255);
      byte G = (byte)Math.Clamp((g + m) * 255, 0, 255);
      byte B = (byte)Math.Clamp((b + m) * 255, 0, 255);
      return Color.FromArgb(255, R, G, B);
    }

    private static string BuildLabel(WalkBox box)
    {
      if (box.UsesScaleSlot)
      {
        var scaleText = box.ComputedScale.HasValue ? $"{box.ComputedScale:0}%" : "?";
        return $"{box.Index} s{box.ScaleSlot}:{scaleText}";
      }

      return box.FixedScale >= 0 ? $"{box.Index} {box.FixedScale}%" : box.Index.ToString();
    }
  }

  public sealed class WalkBox
  {
    public int Index { get; init; }
    public List<(int X, int Y)> Points { get; init; } = new();
    public ushort Flags { get; init; }
    public ushort ScaleRaw { get; init; }
    public bool UsesScaleSlot { get; init; }
    public int ScaleSlot { get; init; }
    public int FixedScale { get; init; }
    public double? ComputedScale { get; init; }
    public ScaleSlot? Slot { get; init; }

    public (double X, double Y) GetCentroid()
    {
      if (Points.Count == 0) return (0, 0);
      double sumX = 0, sumY = 0;
      foreach (var p in Points)
      {
        sumX += p.X;
        sumY += p.Y;
      }
      return (sumX / Points.Count, sumY / Points.Count);
    }
  }

  public sealed class ScaleSlot
  {
    public ScaleSlot(int index, short y1, short y2, byte scale1, byte scale2, int bytesPerSlot)
    {
      Index = index;
      Y1 = y1;
      Y2 = y2;
      Scale1 = scale1;
      Scale2 = scale2;
      BytesPerSlot = bytesPerSlot;
    }

    public int Index { get; }
    public short Y1 { get; }
    public short Y2 { get; }
    public byte Scale1 { get; }
    public byte Scale2 { get; }
    public int BytesPerSlot { get; }

    public double Evaluate(short y)
    {
      if (Y1 == Y2) return Scale2;
      var t = Math.Clamp((double)(y - Y1) / (Y2 - Y1), 0.0, 1.0);
      return Scale1 + (Scale2 - Scale1) * t;
    }
  }

  public static class WalkBoxParser
  {
    public static List<WalkBox> ParseBoxd(byte[] data, IReadOnlyList<ScaleSlot>? scaleSlots = null)
    {
      var list = new List<WalkBox>();
      if (data.Length < 2) return list;

      var span = data.AsSpan();
      int numBoxes = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span);
      int recordSize = 8 * 2 + 2 + 2; // 4 vertices + flags + scale
      int offset = 2;
      var slots = scaleSlots ?? Array.Empty<ScaleSlot>();

      for (int i = 0; i < numBoxes; i++)
      {
        if (offset + recordSize > data.Length) break;
        var pts = new List<(int X, int Y)>(4);
        short ulx = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset + 0, 2));
        short uly = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset + 2, 2));
        short urx = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset + 4, 2));
        short ury = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset + 6, 2));
        short lrx = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset + 8, 2));
        short lry = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset + 10, 2));
        short llx = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset + 12, 2));
        short lly = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset + 14, 2));
        ushort flags = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset + 16, 2));
        ushort scale = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset + 18, 2));

        offset += recordSize;

        // skip sentinel box 0 if filled with -32000
        if (ulx <= -32000 && uly <= -32000 && urx <= -32000 && ury <= -32000 && lrx <= -32000 && lry <= -32000 && llx <= -32000 && lly <= -32000)
        {
          continue;
        }

        pts.Add((ulx, uly));
        pts.Add((urx, ury));
        pts.Add((lrx, lry));
        pts.Add((llx, lly));

        bool usesSlot = (scale & 0x8000) != 0;
        int slotIndex = usesSlot ? (scale & 0x7FFF) : -1;
        int fixedScale = usesSlot ? -1 : scale;
        ScaleSlot? slot = usesSlot && slotIndex >= 0 && slotIndex < slots.Count ? slots[slotIndex] : null;
        double? computedScale = null;
        if (slot != null)
        {
          var centroidY = pts.Average(p => p.Y);
          computedScale = slot.Evaluate((short)centroidY);
        }

        list.Add(new WalkBox
        {
          Index = i,
          Points = pts,
          Flags = flags,
          ScaleRaw = scale,
          UsesScaleSlot = usesSlot,
          ScaleSlot = slotIndex,
          FixedScale = fixedScale,
          ComputedScale = computedScale,
          Slot = slot
        });
      }

      return list;
    }

    public static List<ScaleSlot> ParseScal(byte[] data)
    {
      var slots = new List<ScaleSlot>();
      if (data == null || data.Length < 2) return slots;

      var span = data.AsSpan();
      int count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span);
      if (count <= 0) return slots;

      int remaining = data.Length - 2;
      int bytesPerSlot = count > 0 ? remaining / count : 0;
      int offset = 2;

      for (int i = 0; i < count; i++)
      {
        if (offset + bytesPerSlot > data.Length || bytesPerSlot <= 0) break;

        if (bytesPerSlot >= 6)
        {
          short y1 = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset + 0, 2));
          byte s1 = span[offset + 2];
          byte s2 = span[offset + 3];
          short y2 = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset + 4, 2));
          slots.Add(new ScaleSlot(i, y1, y2, s1, s2, bytesPerSlot));
        }
        else if (bytesPerSlot >= 4)
        {
          short y1 = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset + 0, 2));
          byte s1 = span[offset + 2];
          byte s2 = span[offset + 3];
          slots.Add(new ScaleSlot(i, y1, y1, s1, s2, bytesPerSlot));
        }
        else
        {
          break;
        }

        offset += bytesPerSlot;
      }

      return slots;
    }

    public static int PeekBoxCount(byte[] data)
    {
      if (data == null || data.Length < 2) return 0;
      return System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan());
    }
  }
}