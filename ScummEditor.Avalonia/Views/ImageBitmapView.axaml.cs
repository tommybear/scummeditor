using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;

namespace ScummEditor.AvaloniaApp.Views
{
  public partial class ImageBitmapView : UserControl
  {
    private Image? _image;

    public ImageBitmapView()
    {
      InitializeComponent();
      _image = this.FindControl<Image>("ImageControl");
    }

    private void InitializeComponent()
    {
      AvaloniaXamlLoader.Load(this);
    }

    public void SetBitmap(Bitmap bitmap)
    {
      if (_image != null)
      {
        _image.Source = bitmap;
      }
    }
  }
}
