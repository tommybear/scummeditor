using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScummEditor.AvaloniaApp.Views
{
  public partial class DetailsListView : UserControl
  {
    public DetailsListView()
    {
      InitializeComponent();
    }

    private void InitializeComponent()
    {
      AvaloniaXamlLoader.Load(this);
    }
  }
}
