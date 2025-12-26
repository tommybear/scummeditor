using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace ScummEditor.AvaloniaApp.Views
{
  public partial class BoxOverlayView : UserControl
  {
    private Image? _image;
    private Canvas? _overlay;

    public BoxOverlayView()
    {
      InitializeComponent();
      _image = this.FindControl<Image>("BackgroundImage");
      _overlay = this.FindControl<Canvas>("Overlay");
    }

    private void InitializeComponent()
    {
      AvaloniaXamlLoader.Load(this);
    }

    public void Load(AvaloniaBitmap background, IReadOnlyList<BoxRect> boxes)
    {
      if (_image != null)
      {
        _image.Source = background;
      }

      if (_overlay != null)
      {
        _overlay.Children.Clear();
        _overlay.Width = background.PixelSize.Width;
        _overlay.Height = background.PixelSize.Height;

        foreach (var box in boxes)
        {
          var clamped = ClampToBackground(box, background.PixelSize.Width, background.PixelSize.Height);
          if (clamped == null) continue;

          var rect = new Rectangle
          {
            Width = clamped.Value.Width,
            Height = clamped.Value.Height,
            Stroke = Brushes.Lime,
            StrokeThickness = 1,
            Fill = new SolidColorBrush(Color.FromArgb(30, 0, 255, 0))
          };
          Canvas.SetLeft(rect, clamped.Value.X);
          Canvas.SetTop(rect, clamped.Value.Y);
          _overlay.Children.Add(rect);
        }
      }
    }

    private static BoxRect? ClampToBackground(BoxRect box, int maxWidth, int maxHeight)
    {
      if (box.X >= maxWidth || box.Y >= maxHeight) return null;
      var width = Math.Min(box.Width, Math.Max(0, maxWidth - box.X));
      var height = Math.Min(box.Height, Math.Max(0, maxHeight - box.Y));
      if (width <= 0 || height <= 0) return null;
      return box with { Width = width, Height = height };
    }
  }

  public readonly record struct BoxRect(int X, int Y, int Width, int Height);
}