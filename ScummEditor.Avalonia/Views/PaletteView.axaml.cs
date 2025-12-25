using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ScummEditor.Structures.DataFile;

namespace ScummEditor.AvaloniaApp.Views
{
  public partial class PaletteView : UserControl
  {
    public ObservableCollection<PaletteEntry> Entries { get; } = new();

    public PaletteView()
    {
      InitializeComponent();
      DataContext = this;
    }

    private void InitializeComponent()
    {
      AvaloniaXamlLoader.Load(this);
    }

    public void Load(PaletteData palette)
    {
      Entries.Clear();
      if (palette.Colors == null) return;

      for (int i = 0; i < palette.Colors.Length; i++)
      {
        var c = palette.Colors[i];
        Entries.Add(new PaletteEntry($"{i:D3} #{c.R:X2}{c.G:X2}{c.B:X2}", new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B))));
      }
    }
  }

  public class PaletteEntry
  {
    public PaletteEntry(string label, IBrush brush)
    {
      Label = label;
      Brush = brush;
    }

    public string Label { get; }
    public IBrush Brush { get; }
  }
}
