using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using System.Threading.Tasks;
using ScummEditor;
using ScummEditor.Core.Services;

namespace ScummEditor.AvaloniaApp
{
  public partial class MainWindow : Window
  {
    public MainWindow()
    {
      InitializeComponent();
      HookEvents();
    }

    private void InitializeComponent()
    {
      AvaloniaXamlLoader.Load(this);
    }

    private void HookEvents()
    {
      if (this.FindControl<Button>("OpenButton") is { } openButton)
      {
        openButton.Click += async (_, __) => await OnOpenFileAsync();
      }
    }

    private async Task OnOpenFileAsync()
    {
      if (StorageProvider == null)
      {
        SetStatus("Storage provider unavailable.");
        return;
      }

      var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
      {
        Title = "Open resource",
        AllowMultiple = false
      });

      if (files == null || files.Count == 0)
      {
        SetStatus("No file selected.");
        return;
      }

      var file = files[0];
      var path = file.TryGetLocalPath() ?? file.Name;

      var info = await FileInfoService.GetBasicInfoAsync(path);
      // Demonstrate using a Core type to keep linkage alive.
      var typeName = typeof(BitStreamManager).FullName;
      SetStatus($"{info} (Core type: {typeName})");
    }

    private void SetStatus(string text)
    {
      if (this.FindControl<TextBlock>("StatusText") is { } status)
      {
        status.Text = text;
      }
    }
  }
}
