using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScummEditor.AvaloniaApp.Views
{
  public partial class RoomImageHeaderView : UserControl
  {
    public RoomImageHeaderView()
    {
      InitializeComponent();
    }

    private void InitializeComponent()
    {
      AvaloniaXamlLoader.Load(this);
    }
  }
}
