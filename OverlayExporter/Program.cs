using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using ScummEditor;
using ScummEditor.Encoders;
using ScummEditor.Structures;
using ScummEditor.Structures.DataFile;

// Usage:
// dotnet run --project OverlayExporter -- <index-or-data-file-path> <roomIndex> <outputPng>
// The first argument can be any file in the game directory (e.g., MONKEY2.000).
// The exporter will detect the game, load BOXD/SCAL, draw polygons over the room background, and save a PNG.

if (args.Length < 3)
{
  Console.WriteLine("Usage: OverlayExporter <game-file-path> <roomIndex> <outputPng>");
  return;
}

var gamePath = args[0];
if (!File.Exists(gamePath))
{
  Console.WriteLine($"File not found: {gamePath}");
  return;
}

if (!int.TryParse(args[1], out var roomIndex))
{
  Console.WriteLine("roomIndex must be an integer");
  return;
}

var outputPath = args[2];

var game = new ScummV6GameData();
Console.WriteLine("Detecting game...");
var info = Functions.FindScummGame(gamePath);
if (info.LoadedGame == ScummGame.None)
{
  Console.WriteLine("Unsupported game or files not found in directory.");
  return;
}

game.LoadedGameInfo = info;
Console.WriteLine($"Loading index: {info.IndexFile}");
game.LoadIndexFromBinaryReader(new XoredFileStream(info.XorKey, info.IndexFile, FileMode.Open, FileAccess.Read));
Console.WriteLine($"Loading data: {info.DataFile}");
game.LoadDataFromBinaryReader(new XoredFileStream(info.XorKey, info.DataFile, FileMode.Open, FileAccess.Read));

var disks = game.DataFile.GetLFLFs();
if (roomIndex < 0 || roomIndex >= disks.Count)
{
  Console.WriteLine($"roomIndex out of range. Found {disks.Count} rooms.");
  return;
}

var room = disks[roomIndex].GetROOM();
Console.WriteLine($"Rendering room {roomIndex}...");

var boxd = room.Childrens.OfType<NotImplementedDataBlock>().FirstOrDefault(b => b.BlockType == "BOXD");
var scal = room.Childrens.OfType<NotImplementedDataBlock>().FirstOrDefault(b => b.BlockType == "SCAL");
if (boxd?.Contents == null)
{
  Console.WriteLine("BOXD not found in room.");
  return;
}

var scaleSlots = WalkBoxParser.ParseScal(scal?.Contents);
var boxes = WalkBoxParser.ParseBoxd(boxd.Contents, scaleSlots);

var decoder = new ImageDecoder();
using var background = decoder.Decode(room);
if (background == null)
{
  Console.WriteLine("Failed to decode background.");
  return;
}

using var canvas = new Bitmap(background.Width, background.Height);
using (var g = Graphics.FromImage(canvas))
{
  g.DrawImage(background, Point.Empty);
  foreach (var box in boxes)
  {
    var pts = box.Points;
    if (pts.Count < 3) continue;
    var points = pts.Select(p => new PointF(p.X, p.Y)).ToArray();
    using var pen = new Pen(Color.Lime, 1);
    using var brush = new SolidBrush(Color.FromArgb(40, 0, 255, 0));
    g.FillPolygon(brush, points);
    g.DrawPolygon(pen, points);

    var center = box.GetCentroid();
    var label = box.UsesScaleSlot ? $"{box.Index} s{box.ScaleSlot}" : $"{box.Index} {box.FixedScale}%";
    using var font = new Font("Arial", 10, FontStyle.Bold);
    using var textBrush = new SolidBrush(Color.White);
    g.DrawString(label, font, textBrush, new PointF((float)center.X - 10, (float)center.Y - 8));
  }
}

directoryCreate(outputPath);
canvas.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
Console.WriteLine($"Wrote {outputPath}");

static void directoryCreate(string path)
{
  var dir = Path.GetDirectoryName(path);
  if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
  {
    Directory.CreateDirectory(dir);
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

  public static List<ScaleSlot> ParseScal(byte[]? data)
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
}
