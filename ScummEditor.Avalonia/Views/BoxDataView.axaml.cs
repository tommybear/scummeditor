using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScummEditor.AvaloniaApp.Views
{
  public partial class BoxDataView : UserControl
  {
    public ObservableCollection<BoxRow> Boxes { get; } = new();
    public ObservableCollection<MatrixRow> Matrix { get; } = new();

    private TextBlock? _header;
    private TextBlock? _meta;
    private ItemsControl? _boxList;
    private ItemsControl? _matrixList;

    public BoxDataView()
    {
      InitializeComponent();
      DataContext = this;
      _header = this.FindControl<TextBlock>("Header");
      _meta = this.FindControl<TextBlock>("Meta");
      _boxList = this.FindControl<ItemsControl>("BoxList");
      _matrixList = this.FindControl<ItemsControl>("MatrixList");
    }

    private void InitializeComponent()
    {
      AvaloniaXamlLoader.Load(this);
    }

    public void LoadBoxd(byte[] data, IReadOnlyList<ScaleSlot>? scaleSlots = null)
    {
      Boxes.Clear();
      Matrix.Clear();

      if (_header != null) _header.Text = "BOXD (walk boxes)";

      var parsed = WalkBoxParser.ParseBoxd(data, scaleSlots);
      foreach (var box in parsed)
      {
        bool isSentinel = box.Points.All(p => p.X <= -32000 && p.Y <= -32000);
        Boxes.Add(new BoxRow(box, isSentinel));
      }

      ToggleViews(showBoxes: true);
      var slotCount = scaleSlots?.Count ?? 0;
      if (_meta != null) _meta.Text = $"Entries: {Boxes.Count} | Raw bytes: {data.Length} | Slots: {slotCount}";
    }

    public void LoadBoxm(byte[] data, int? expectedSide = null)
    {
      Boxes.Clear();
      Matrix.Clear();

      if (_header != null) _header.Text = "BOXM (box matrix)";

      int offset = 0;
      int side = expectedSide ?? 0;

      if (expectedSide.HasValue && expectedSide.Value > 0)
      {
        int expectedBytes = expectedSide.Value * expectedSide.Value;
        if (data.Length == expectedBytes + 2 && BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan()) == expectedSide.Value)
        {
          offset = 2;
        }
        else if (data.Length < expectedBytes && expectedSide.Value > 0)
        {
          offset = 0;
          side = (int)Math.Sqrt(data.Length);
        }
        else if (data.Length > expectedBytes)
        {
          offset = data.Length - expectedBytes;
        }
      }
      else
      {
        side = (int)Math.Sqrt(data.Length);
        if (side * side != data.Length && data.Length > 2)
        {
          int sideWithHeader = (int)Math.Sqrt(data.Length - 2);
          if (sideWithHeader * sideWithHeader == data.Length - 2)
          {
            offset = 2;
            side = sideWithHeader;
          }
        }
        if (side <= 0) side = 1;
      }

      int totalBytes = Math.Max(0, Math.Min(data.Length - offset, side * side));
      var seen = new HashSet<byte>();
      for (int r = 0; r < side; r++)
      {
        var row = new byte[side];
        for (int c = 0; c < side; c++)
        {
          int idx = offset + r * side + c;
          row[c] = idx < offset + totalBytes ? data[idx] : (byte)0xFF;
          seen.Add(row[c]);
        }
        Matrix.Add(new MatrixRow(r, row));
      }

      var distinct = seen.ToList();
      string mode = distinct.All(v => v <= 1) ? "adjacency (0/1)" : "next-hop (0xFF unreachable?)";

      ToggleViews(showBoxes: false);
      if (_meta != null) _meta.Text = $"Matrix: {side}x{side} | Offset: {offset} | Raw bytes: {data.Length} | Values: {string.Join(",", distinct.Select(v => v.ToString("X2")))} | Assumed {mode}";
    }

    private void ToggleViews(bool showBoxes)
    {
      if (_boxList != null) _boxList.IsVisible = showBoxes;
      if (_matrixList != null) _matrixList.IsVisible = !showBoxes;
    }
  }

  public sealed class BoxRow
  {
    public BoxRow(WalkBox box, bool isSentinel)
    {
      Index = box.Index;
      ULX = (short)box.Points.ElementAtOrDefault(0).X;
      ULY = (short)box.Points.ElementAtOrDefault(0).Y;
      URX = (short)box.Points.ElementAtOrDefault(1).X;
      URY = (short)box.Points.ElementAtOrDefault(1).Y;
      LRX = (short)box.Points.ElementAtOrDefault(2).X;
      LRY = (short)box.Points.ElementAtOrDefault(2).Y;
      LLX = (short)box.Points.ElementAtOrDefault(3).X;
      LLY = (short)box.Points.ElementAtOrDefault(3).Y;
      Flags = box.Flags;
      ScaleRaw = box.ScaleRaw;
      UsesScaleSlot = box.UsesScaleSlot;
      ScaleSlot = box.ScaleSlot;
      FixedScale = box.FixedScale;
      IsSentinel = isSentinel;
      ComputedScale = box.ComputedScale;
      Summary = BuildSummary();
    }

    public int Index { get; }
    public short ULX { get; }
    public short ULY { get; }
    public short URX { get; }
    public short URY { get; }
    public short LRX { get; }
    public short LRY { get; }
    public short LLX { get; }
    public short LLY { get; }
    public ushort Flags { get; }
    public ushort ScaleRaw { get; }
    public bool UsesScaleSlot { get; }
    public int ScaleSlot { get; }
    public int FixedScale { get; }
    public bool IsSentinel { get; }
    public double? ComputedScale { get; }
    public string Summary { get; }

    private string BuildSummary()
    {
      if (IsSentinel)
      {
        return $"Box {Index}: sentinel (-32000)";
      }

      var scaleDisplay = UsesScaleSlot
        ? (ComputedScale.HasValue ? $"slot {ScaleSlot} ~{ComputedScale:0}%" : $"slot {ScaleSlot}")
        : FixedScale.ToString();

      return $"Box {Index}: UL({ULX},{ULY}) UR({URX},{URY}) LR({LRX},{LRY}) LL({LLX},{LLY}) Flags=0x{Flags:X4} Scale={scaleDisplay} Raw=0x{ScaleRaw:X4}";
    }
  }

  public sealed class MatrixRow
  {
    public MatrixRow(int rowIndex, byte[] values)
    {
      RowIndex = rowIndex;
      Row = string.Join(" ", values.Select(v => v.ToString("X2")));
    }

    public int RowIndex { get; }
    public string Row { get; }
  }
}