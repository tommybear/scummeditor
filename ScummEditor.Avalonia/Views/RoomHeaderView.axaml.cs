using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScummEditor.AvaloniaApp.Views
{
  public partial class RoomHeaderView : UserControl
  {
    public RoomHeaderView()
    {
      InitializeComponent();
    }

    private void InitializeComponent()
    {
      AvaloniaXamlLoader.Load(this);
    }
  }
}
