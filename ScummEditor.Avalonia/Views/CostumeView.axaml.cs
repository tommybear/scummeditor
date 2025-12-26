using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using ScummEditor.Encoders;
using ScummEditor.Structures.DataFile;
using DrawingBitmap = System.Drawing.Bitmap;

namespace ScummEditor.AvaloniaApp.Views
{
  public partial class CostumeView : UserControl
  {
    private RoomBlock? _room;
    private Costume? _costume;
    private readonly CostumeImageDecoder _decoder = new CostumeImageDecoder { UseTransparentColor = true };
    private Bitmap? _currentBitmap;
    private IReadOnlyList<PaletteOption> _palettes = Array.Empty<PaletteOption>();
    private IReadOnlyList<FrameEntry> _frames = Array.Empty<FrameEntry>();

    public CostumeView()
    {
      InitializeComponent();
    }

    public void Load(RoomBlock room, Costume costume)
    {
      _room = room;
      _costume = costume;

      TitleText.Text = string.IsNullOrWhiteSpace(costume.Header) ? "Costume" : costume.Header;
      DetailText.Text = $"Animations: {costume.NumAnim} • Frames: {costume.Pictures?.Count ?? 0} • Palette entries: {costume.Palette?.Count ?? 0}";

      _palettes = BuildPaletteOptions(room);
      PalettePicker.ItemsSource = _palettes;
      PalettePicker.SelectedIndex = _palettes.Count > 0 ? 0 : -1;

      _frames = costume.Pictures == null
        ? Array.Empty<FrameEntry>()
        : costume.Pictures.Select((p, i) => new FrameEntry(i, p)).ToList();

      FramesList.ItemsSource = _frames;
      FramesList.SelectedIndex = _frames.Count > 0 ? 0 : -1;
      RenderSelectedFrame();
    }

    private IReadOnlyList<PaletteOption> BuildPaletteOptions(RoomBlock room)
    {
      var options = new List<PaletteOption> { new PaletteOption(0, "Default palette") };
      var apals = room.GetPALS()?.GetWRAP()?.GetAPALs();
      if (apals != null)
      {
        for (int i = 0; i < apals.Count; i++)
        {
          options.Add(new PaletteOption(i, $"APAL {i}"));
        }
      }
      return options;
    }

    private void OnFrameSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
      RenderSelectedFrame();
    }

    private void OnPaletteChanged(object? sender, SelectionChangedEventArgs e)
    {
      RenderSelectedFrame();
    }

    private void OnTransparencyChanged(object? sender, RoutedEventArgs e)
    {
      RenderSelectedFrame();
    }

    private void RenderSelectedFrame()
    {
      if (_room == null || _costume == null)
      {
        return;
      }

      var selectedFrame = FramesList.SelectedItem as FrameEntry;
      if (selectedFrame == null)
      {
        FrameImage.Source = null;
        FrameMeta.Text = string.Empty;
        return;
      }

      _decoder.UseTransparentColor = TransparentCheck.IsChecked ?? true;
      var selectedPalette = PalettePicker.SelectedItem as PaletteOption;
      _decoder.PaletteIndex = selectedPalette?.Index ?? 0;

      _currentBitmap?.Dispose();
      using var bmp = _decoder.Decode(_room, _costume, selectedFrame.Index);
      if (bmp == null)
      {
        FrameImage.Source = null;
        FrameMeta.Text = "Frame has no image data.";
        return;
      }

      _currentBitmap = ConvertToAvaloniaBitmap(bmp);
      FrameImage.Source = _currentBitmap;
      FrameMeta.Text = selectedFrame.Meta;
    }

    private static Bitmap ConvertToAvaloniaBitmap(DrawingBitmap source)
    {
      using var ms = new MemoryStream();
      source.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
      ms.Position = 0;
      return new Bitmap(ms);
    }

    private sealed class PaletteOption
    {
      public PaletteOption(int index, string label)
      {
        Index = index;
        Label = label;
      }

      public int Index { get; }
      public string Label { get; }

      public override string ToString() => Label;
    }

    private sealed class FrameEntry
    {
      public FrameEntry(int index, CostumeImageData data)
      {
        Index = index;
        Label = $"Frame {index}";
        Meta = $"{data.Width}x{data.Height} rel({data.RelX},{data.RelY}) move({data.MoveX},{data.MoveY})";
      }

      public int Index { get; }
      public string Label { get; }
      public string Meta { get; }
    }
  }
}
