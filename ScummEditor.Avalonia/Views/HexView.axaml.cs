using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScummEditor.AvaloniaApp.Views
{
  public partial class HexView : UserControl
  {
    private TextBox? _hex;
    private TextBlock? _meta;

    public HexView()
    {
      InitializeComponent();
      _hex = this.FindControl<TextBox>("HexText");
      _meta = this.FindControl<TextBlock>("MetaText");
    }

    private void InitializeComponent()
    {
      AvaloniaXamlLoader.Load(this);
    }

    public void Load(byte[] data, string label)
    {
      if (_hex != null)
      {
        _hex.Text = ToHexPreview(data, 512);
      }
      if (_meta != null)
      {
        _meta.Text = $"{label} | length: {data?.Length ?? 0}";
      }
    }

    private static string ToHexPreview(byte[] data, int limit)
    {
      if (data == null || data.Length == 0) return string.Empty;
      int take = Math.Min(limit, data.Length);
      char[] table = "0123456789ABCDEF".ToCharArray();
      var span = data.AsSpan(0, take);
      var chars = new char[take * 3];
      int idx = 0;
      for (int i = 0; i < take; i++)
      {
        byte b = span[i];
        chars[idx++] = table[b >> 4];
        chars[idx++] = table[b & 0xF];
        chars[idx++] = (i + 1) % 16 == 0 ? '\n' : ' ';
      }
      return new string(chars);
    }
  }
}
