using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScummEditor.AvaloniaApp.Views
{
  public partial class PlaceholderView : UserControl
  {
    private TextBlock? _title;
    private TextBlock? _body;

    public PlaceholderView()
    {
      InitializeComponent();
      _title = this.FindControl<TextBlock>("Title");
      _body = this.FindControl<TextBlock>("Body");
    }

    private void InitializeComponent()
    {
      AvaloniaXamlLoader.Load(this);
    }

    public void SetText(string title, string body)
    {
      if (_title != null) _title.Text = title;
      if (_body != null) _body.Text = body;
    }
  }
}
