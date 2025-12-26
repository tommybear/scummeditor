using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ScummEditor.AvaloniaApp.Views
{
  public partial class SoundView : UserControl
  {
    private byte[] _data = Array.Empty<byte>();
    private string _name = "SOUN";

    public SoundView()
    {
      InitializeComponent();
    }

    public void Load(string name, byte[] data)
    {
      _name = name;
      _data = data ?? Array.Empty<byte>();
      Title.Text = $"{name} (raw)";
      Details.Text = $"Length: {_data.Length} bytes";
    }

    private async void OnSaveRaw(object? sender, RoutedEventArgs e)
    {
      var top = TopLevel.GetTopLevel(this);
      if (top?.StorageProvider == null) return;

      var suggested = $"{_name}.raw";
      var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
      {
        SuggestedFileName = suggested,
        FileTypeChoices = new[] { new FilePickerFileType("Raw sound") { Patterns = new[] { "*.raw", "*.soun" } } }
      });

      if (file == null) return;

      await using var stream = await file.OpenWriteAsync();
      await stream.WriteAsync(_data, 0, _data.Length);
    }

    private async void OnCopySize(object? sender, RoutedEventArgs e)
    {
      var top = TopLevel.GetTopLevel(this);
      if (top == null) return;
      var cb = top.Clipboard;
      if (cb == null) return;
      await cb.SetTextAsync(_data.Length.ToString());
    }
  }
}
