using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Buffers.Binary;
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
    private MemoryStream? _playerStream;
    private SoundPlayer? _player;
    private bool _isWave;
    private byte[] _playData = Array.Empty<byte>();
    private string _format = "unknown";

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

      _playData = _data;
      _isWave = LooksLikeWave(_data);
      _format = _isWave ? "WAV" : "unknown";

      if (!_isWave && LooksLikeVoc(_data))
      {
        var wav = TryDecodeVocToWav(_data, out var desc);
        if (wav != null)
        {
          _playData = wav;
          _isWave = true;
          _format = desc;
        }
      }

      Status.Text = _isWave ? $"Playback enabled ({_format}, Windows only)" : "Unknown format: playback disabled";
    }

    private void OnPlay(object? sender, RoutedEventArgs e)
    {
      if (!_isWave || _playData.Length == 0) return;
      StopPlayer();
      _playerStream = new MemoryStream(_playData, writable: false);
      _player = new SoundPlayer(_playerStream);
      try
      {
        _player.Play();
        Status.Text = "Playing...";
      }
      catch
      {
        Status.Text = "Playback failed.";
        StopPlayer();
      }
    }

    private void OnStop(object? sender, RoutedEventArgs e)
    {
      StopPlayer();
      Status.Text = _isWave ? "Stopped." : "Unknown format: playback disabled";
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

    private void StopPlayer()
    {
      try { _player?.Stop(); } catch { }
      _player?.Dispose();
      _player = null;
      _playerStream?.Dispose();
      _playerStream = null;
    }

    private static bool LooksLikeWave(byte[] data)
    {
      if (data.Length < 12) return false;
      return data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
             && data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E';
    }

    private static bool LooksLikeVoc(byte[] data)
    {
      if (data.Length < 26) return false;
      var signature = "Creative Voice File";
      for (int i = 0; i < signature.Length; i++)
      {
        if (data[i] != signature[i]) return false;
      }
      return true;
    }

    private static byte[]? TryDecodeVocToWav(byte[] data, out string description)
    {
      description = "VOC";
      if (!LooksLikeVoc(data)) return null;

      if (data.Length < 26) return null;
      int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(20));
      if (headerSize <= 0 || headerSize >= data.Length) return null;

      int pos = headerSize;
      var pcm = new MemoryStream();
      int sampleRate = 11025;

      while (pos + 4 <= data.Length)
      {
        byte blockType = data[pos];
        int blockSize = data[pos + 1] | (data[pos + 2] << 8) | (data[pos + 3] << 16);
        pos += 4;
        if (blockType == 0) break; // terminator
        if (blockSize <= 0 || pos + blockSize > data.Length) break;

        if (blockType == 1 && blockSize >= 2)
        {
          byte timeConstant = data[pos];
          byte pack = data[pos + 1];
          if (pack != 0)
          {
            return null; // packed/unsupported
          }

          sampleRate = (int)(1000000.0 / (256 - timeConstant));
          int dataLen = blockSize - 2;
          if (dataLen > 0)
          {
            pcm.Write(data, pos + 2, dataLen);
          }
        }

        pos += blockSize;
      }

      if (pcm.Length == 0) return null;

      var pcmBytes = pcm.ToArray();
      return BuildWav(sampleRate, pcmBytes, out description);
    }

    private static byte[] BuildWav(int sampleRate, byte[] pcm, out string desc)
    {
      desc = $"WAV ({sampleRate} Hz, 8-bit, mono)";
      using var ms = new MemoryStream();
      using var bw = new BinaryWriter(ms);
      int dataLen = pcm.Length;
      int fmtChunkSize = 16;
      int riffSize = 4 + (8 + fmtChunkSize) + (8 + dataLen);

      bw.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
      bw.Write(riffSize);
      bw.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });

      bw.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
      bw.Write(fmtChunkSize);
      bw.Write((short)1); // PCM
      bw.Write((short)1); // channels
      bw.Write(sampleRate);
      bw.Write(sampleRate * 1 * 8 / 8); // byte rate
      bw.Write((short)1); // block align
      bw.Write((short)8); // bits per sample

      bw.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
      bw.Write(dataLen);
      bw.Write(pcm);
      bw.Flush();
      return ms.ToArray();
    }
  }
}
