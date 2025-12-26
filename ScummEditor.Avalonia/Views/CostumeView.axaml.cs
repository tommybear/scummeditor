using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ScummEditor.Encoders;
using ScummEditor.Structures.DataFile;
using DrawingBitmap = System.Drawing.Bitmap;

#pragma warning disable CA1416

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
    private IReadOnlyList<AnimationEntry> _animations = Array.Empty<AnimationEntry>();
    private IReadOnlyList<CommandEntry> _commands = Array.Empty<CommandEntry>();
    private IReadOnlyList<LimbEntry> _limbs = Array.Empty<LimbEntry>();
    private ObservableCollection<PaletteEntry> _paletteEntries = new();
    private int _paletteCount = 0;

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

      _animations = BuildAnimations(costume);
      AnimationsList.ItemsSource = _animations;

      _commands = BuildCommands(costume);
      CommandsList.ItemsSource = _commands;

      _limbs = BuildLimbs(costume);
      LimbsList.ItemsSource = _limbs;

      BuildPaletteEntries(room, costume);
      PaletteList.ItemsSource = _paletteEntries;
      PopulatePaletteComboChoices();
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

    private IReadOnlyList<AnimationEntry> BuildAnimations(Costume costume)
    {
      if (costume.Animations == null) return Array.Empty<AnimationEntry>();
      var list = new List<AnimationEntry>();
      int idx = 0;
      foreach (var anim in costume.Animations)
      {
        var defs = anim.AnimDefinitions?.Select((d, i) => new AnimationEntry.DefInfo(i + 1, d.Start, d.NoLoop, d.Disabled, d.NoLoopAndEndOffset)).ToList() ?? new();
        list.Add(new AnimationEntry(idx++, anim.LimbMask, anim.NumLimbs, defs));
      }
      return list;
    }

    private IReadOnlyList<CommandEntry> BuildCommands(Costume costume)
    {
      if (costume.Commands == null) return Array.Empty<CommandEntry>();
      var list = new List<CommandEntry>();
      foreach (var cmd in costume.Commands)
      {
        list.Add(new CommandEntry(cmd, DescribeCommand(cmd)));
      }
      return list;
    }

    private IReadOnlyList<LimbEntry> BuildLimbs(Costume costume)
    {
      if (costume.Limbs == null) return Array.Empty<LimbEntry>();
      var list = new List<LimbEntry>();
      int idx = 0;
      foreach (var limb in costume.Limbs)
      {
        var images = limb.ImageOffsets?.Select(io => $"Image offset: {io}").ToList() ?? new List<string>();
        list.Add(new LimbEntry(++idx, limb.OffSet, limb.Size, images));
      }
      return list;
    }

    private void BuildPaletteEntries(RoomBlock room, Costume costume)
    {
      _paletteEntries.Clear();
      _paletteCount = room.GetDefaultPalette()?.Colors?.Length ?? 0;
      if (_paletteCount == 0 || costume.Palette == null) return;

      var defaultPalette = room.GetDefaultPalette();
      for (int i = 0; i < costume.Palette.Count; i++)
      {
        var realIndex = costume.Palette[i];
        var color = realIndex < defaultPalette.Colors.Length ? defaultPalette.Colors[realIndex] : System.Drawing.Color.Black;
        _paletteEntries.Add(new PaletteEntry(i, realIndex, color));
      }
    }

    private void PopulatePaletteComboChoices()
    {
      // Populate combo items via code because XAML array is static.
      PaletteList.ItemTemplate = PaletteList.ItemTemplate; // force refresh; items themselves carry selected index.
    }

    private void OnPaletteEntryChanged(object? sender, SelectionChangedEventArgs e)
    {
      if (_room == null || _costume == null) return;
      if (sender is not ComboBox combo || combo.DataContext is not PaletteEntry entry) return;
      if (combo.SelectedItem is not int selected) return;
      if (selected < 0 || selected >= _paletteCount) return;

      entry.SelectedIndex = selected;
      _costume.Palette[entry.Index] = (byte)selected;

      var defaultPalette = _room.GetDefaultPalette();
      var color = selected < defaultPalette.Colors.Length ? defaultPalette.Colors[selected] : System.Drawing.Color.Black;
      entry.UpdateColor(color);
      PaletteList.ItemsSource = null;
      PaletteList.ItemsSource = _paletteEntries;
      RenderSelectedFrame();
    }

    private void OnPaletteComboLoaded(object? sender, RoutedEventArgs e)
    {
      if (sender is not ComboBox combo) return;
      combo.ItemsSource = Enumerable.Range(0, _paletteCount).ToList();
      if (combo.DataContext is PaletteEntry entry)
      {
        combo.SelectedItem = entry.SelectedIndex;
      }
    }

    private static string DescribeCommand(byte command)
    {
      return command switch
      {
        >= 0x71 and <= 0x78 => "Add Sound",
        0x79 => "Stop",
        0x7A => "Start",
        0x7B => "Hide",
        0x7C => "SkipFrame",
        _ => ""
      };
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

    private sealed class AnimationEntry
    {
      public AnimationEntry(int index, ushort limbMask, int numLimbs, IReadOnlyList<DefInfo> defs)
      {
        Title = $"Anim {index} • Limbs: {numLimbs} • Mask: 0x{limbMask:X4}";
        Details = defs.Count == 0 ? "No definitions" : string.Join("; ", defs.Select(d => d.ToString()));
      }

      public string Title { get; }
      public string Details { get; }

      public sealed class DefInfo
      {
        public DefInfo(int index, ushort start, bool noLoop, bool disabled, byte noLoopAndEndOffset)
        {
          Index = index;
          Start = start;
          NoLoop = noLoop;
          Disabled = disabled;
          EndOffset = noLoopAndEndOffset;
        }

        public int Index { get; }
        public ushort Start { get; }
        public bool NoLoop { get; }
        public bool Disabled { get; }
        public byte EndOffset { get; }

        public override string ToString() => Disabled ? $"Def {Index}: disabled" : $"Def {Index}: start {Start}, end {EndOffset}, noLoop={NoLoop}";
      }
    }

    private sealed class CommandEntry
    {
      public CommandEntry(byte value, string meaning)
      {
        Label = $"0x{value:X2} ({value})";
        Meaning = string.IsNullOrWhiteSpace(meaning) ? "" : meaning;
      }

      public string Label { get; }
      public string Meaning { get; }
    }

    private sealed class LimbEntry
    {
      public LimbEntry(int index, ushort offset, ushort size, IReadOnlyList<string> images)
      {
        Title = $"Limb {index} • Offset {offset} • Size {size}";
        Details = images.Count == 0 ? "No images" : $"Images: {images.Count}";
        Images = images;
      }

      public string Title { get; }
      public string Details { get; }
      public IReadOnlyList<string> Images { get; }
    }

    private sealed class PaletteEntry
    {
      public PaletteEntry(int index, byte selectedIndex, System.Drawing.Color color)
      {
        Index = index;
        SelectedIndex = selectedIndex;
        UpdateColor(color);
      }

      public int Index { get; }
      public int SelectedIndex { get; set; }
      public IBrush Brush { get; private set; } = Brushes.Black;
      public string Label => $"Entry {Index}";
      public string ColorText { get; private set; } = "";

      public void UpdateColor(System.Drawing.Color color)
      {
        Brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        ColorText = $"Map → {SelectedIndex} (#{color.R:X2}{color.G:X2}{color.B:X2})";
      }
    }
  }
}

#pragma warning restore CA1416
