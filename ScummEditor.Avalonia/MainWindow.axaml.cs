using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Threading.Tasks;
using ScummEditor;

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
      var dialog = new OpenFileDialog
      {
        Title = "Open resource",
        AllowMultiple = false
      };

      var result = await dialog.ShowAsync(this);
      if (result == null || result.Length == 0)
      {
        SetStatus("No file selected.");
        return;
      }

      var path = result[0];

      // Demonstrate using a Core type to keep linkage alive.
      var typeName = typeof(BitStreamManager).FullName;
      SetStatus($"Loaded: {System.IO.Path.GetFileName(path)} (Core type: {typeName})");
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
