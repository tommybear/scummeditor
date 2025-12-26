using System;
using System.Collections.ObjectModel;
using System.IO;
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

    public void LoadBoxd(byte[] data)
    {
      Boxes.Clear();
      Matrix.Clear();

      if (_header != null) _header.Text = "BOXD (walk boxes)";

      int count = data.Length / 8; // 4 int16 values per box
      for (int i = 0; i < count; i++)
      {
        short x1 = ReadInt16(data, i * 8 + 0);
        short y1 = ReadInt16(data, i * 8 + 2);
        short x2 = ReadInt16(data, i * 8 + 4);
        short y2 = ReadInt16(data, i * 8 + 6);
        Boxes.Add(new BoxRow(i, x1, y1, x2, y2));
      }

      ToggleViews(showBoxes: true);
      if (_meta != null) _meta.Text = $"Entries: {Boxes.Count} | Raw bytes: {data.Length}";
    }

    public void LoadBoxm(byte[] data)
    {
      Boxes.Clear();
      Matrix.Clear();

      if (_header != null) _header.Text = "BOXM (box matrix)";

      int entries = data.Length / 2; // 16-bit per entry
      int side = (int)Math.Sqrt(entries);
      for (int r = 0; r < side; r++)
      {
        var row = new short[side];
        for (int c = 0; c < side; c++)
        {
          row[c] = ReadInt16(data, (r * side + c) * 2);
        }
        Matrix.Add(new MatrixRow(r, row));
      }

      ToggleViews(showBoxes: false);
      if (_meta != null) _meta.Text = $"Matrix: {side}x{side} | Raw bytes: {data.Length}";
    }

    private void ToggleViews(bool showBoxes)
    {
      if (_boxList != null) _boxList.IsVisible = showBoxes;
      if (_matrixList != null) _matrixList.IsVisible = !showBoxes;
    }

    private static short ReadInt16(byte[] data, int offset)
    {
      if (offset + 1 >= data.Length) return 0;
      return (short)(data[offset] | (data[offset + 1] << 8));
    }
  }

  public sealed class BoxRow
  {
    public BoxRow(int index, short x1, short y1, short x2, short y2)
    {
      Index = index;
      X1 = x1;
      Y1 = y1;
      X2 = x2;
      Y2 = y2;
    }

    public int Index { get; }
    public short X1 { get; }
    public short Y1 { get; }
    public short X2 { get; }
    public short Y2 { get; }
  }

  public sealed class MatrixRow
  {
    public MatrixRow(int rowIndex, short[] values)
    {
      RowIndex = rowIndex;
      Row = string.Join(" ", values);
    }

    public int RowIndex { get; }
    public string Row { get; }
  }
}