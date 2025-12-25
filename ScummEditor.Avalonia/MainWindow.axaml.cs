using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ScummEditor.AvaloniaApp.Views;
using ScummEditor.Core;
using ScummEditor.Encoders;
using ScummEditor.Structures;
using ScummEditor.Structures.DataFile;
using ScummEditor.Structures.IndexFile;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = System.Drawing.Bitmap;

namespace ScummEditor.AvaloniaApp
{
  public partial class MainWindow : Window
  {
    public ObservableCollection<ResourceNode> Nodes { get; } = new();
    public ObservableCollection<DetailRow> Details { get; } = new();

    private readonly List<ResourceNode> _allNodes = new();
    private string _currentFilter = string.Empty;
    private TextBox? _searchBox;
    private Button? _saveButton;
    private ScummV6GameData? _currentGame;
    private string? _currentPath;

    public MainWindow()
    {
      InitializeComponent();
      DataContext = this;
      HookEvents();
      SetViewer(null);
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

      _saveButton = this.FindControl<Button>("SaveButton");
      if (_saveButton != null)
      {
        _saveButton.Click += async (_, __) => await OnSaveAsync();
        _saveButton.IsEnabled = false;
      }

      if (this.FindControl<Button>("ExportButton") is { } exportButton)
      {
        exportButton.Click += (_, __) => ShowPlaceholder("Export", "Export not implemented yet in Avalonia. Use WinForms for now.");
      }

      if (this.FindControl<Button>("ImportButton") is { } importButton)
      {
        importButton.Click += (_, __) => ShowPlaceholder("Import", "Import not implemented yet in Avalonia. Use WinForms for now.");
      }

      _searchBox = this.FindControl<TextBox>("SearchBox");
      if (_searchBox != null)
      {
        _searchBox.TextChanged += (_, __) =>
        {
          _currentFilter = _searchBox.Text ?? string.Empty;
          ApplyFilter();
        };
      }

      if (this.FindControl<Button>("ClearSearchButton") is { } clearButton)
      {
        clearButton.Click += (_, __) =>
        {
          if (_searchBox != null) _searchBox.Text = string.Empty;
        };
      }

      if (this.FindControl<TreeView>("ResourceTree") is { } tree)
      {
        tree.SelectionChanged += (_, __) =>
        {
          if (tree.SelectedItem is ResourceNode node)
          {
            SetDetails(node);
          }
        };
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

      await LoadGameAsync(path);
    }

    private async Task LoadGameAsync(string path)
    {
      try
      {
        SetStatus($"Loading {path}...");

        var game = await Task.Run(() =>
        {
          var g = new ScummV6GameData();
          g.LoadDataFromDisc(path);
          return g;
        });

        _currentGame = game;
        _currentPath = path;
        if (_saveButton != null) _saveButton.IsEnabled = true;

        BuildTree(game);

        var gameName = GetGameName(game.LoadedGameInfo?.LoadedGame ?? ScummGame.None);
        SetStatus($"Loaded {gameName} ({path})");
      }
      catch (Exception ex)
      {
        SetStatus($"Failed to load: {ex.Message}");
      }
    }

    private async Task OnSaveAsync()
    {
      if (_currentGame == null)
      {
        SetStatus("Nothing to save.");
        return;
      }

      try
      {
        SetStatus("Saving...");
        await Task.Run(() => _currentGame.SaveDataToDisk());
        SetStatus("Save complete.");
      }
      catch (Exception ex)
      {
        SetStatus($"Save failed: {ex.Message}");
      }
    }

    private void BuildTree(ScummV6GameData game)
    {
      _allNodes.Clear();
      if (game.IndexFile != null)
      {
        var indexRoot = new ResourceNode("Index File", null);
        indexRoot.Children.Add(CreateBlockNode(game.IndexFile.RNAM, "RNAM"));
        indexRoot.Children.Add(CreateBlockNode(game.IndexFile.MAXS, "MAXS"));
        indexRoot.Children.Add(CreateBlockNode(game.IndexFile.DROO, "DROO"));
        indexRoot.Children.Add(CreateBlockNode(game.IndexFile.DSCR, "DSCR"));
        indexRoot.Children.Add(CreateBlockNode(game.IndexFile.DSOU, "DSOU"));
        indexRoot.Children.Add(CreateBlockNode(game.IndexFile.DCOS, "DCOS"));
        indexRoot.Children.Add(CreateBlockNode(game.IndexFile.DCHR, "DCHR"));
        indexRoot.Children.Add(CreateBlockNode(game.IndexFile.DOBJ, "DOBJ"));
        _allNodes.Add(indexRoot);
      }

      if (game.DataFile != null)
      {
        _allNodes.Add(CreateBlockNode(game.DataFile, "Data File"));
      }

      ApplyFilter();
    }

    private ResourceNode CreateBlockNode(BlockBase block, string? labelOverride = null)
    {
      string name = labelOverride ?? block.BlockType;
      var node = new ResourceNode(name, block);

      int index = 0;
      foreach (var child in block.Childrens)
      {
        // Keep the indexing used in WinForms tree for repeated block types.
        string childLabel = child.BlockType;
        if (block.Childrens.Count(c => c.BlockType == child.BlockType) > 1)
        {
          childLabel = $"{child.BlockType} {index:D3}";
          index++;
        }
        node.Children.Add(CreateBlockNode(child, childLabel));
      }

      return node;
    }

    private void SetStatus(string text)
    {
      if (this.FindControl<TextBlock>("StatusText") is { } status) status.Text = text;
    }

    private void SetDetails(ResourceNode node)
    {
      Details.Clear();

      if (this.FindControl<TextBlock>("DetailsHeader") is { } header)
      {
        header.Text = node.Block == null ? node.Name : $"{node.Name} ({node.Block.BlockType})";
      }

      if (node.Block == null)
      {
        Details.Add(new DetailRow("Info", node.Name));
        SetViewer(null);
        return;
      }

      var block = node.Block;
      Details.Add(new DetailRow("Type", block.BlockType));
      Details.Add(new DetailRow("Offset", block.BlockOffSet.ToString()));
      Details.Add(new DetailRow("Size", block.BlockSize.ToString()));
      Details.Add(new DetailRow("Children", block.Childrens.Count.ToString()));
      Details.Add(new DetailRow("Id", block.UniqueId));

      foreach (var prop in block.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
      {
        if (!prop.CanRead) continue;
        if (InspectorHelpers.ShouldSkipProperty(prop.Name)) continue;
        var val = prop.GetValue(block);
        if (!InspectorHelpers.IsSimple(prop.PropertyType)) continue;
        Details.Add(new DetailRow(prop.Name, val?.ToString() ?? string.Empty));
      }

      SetViewer(block);
    }

    private void ApplyFilter()
    {
      Nodes.Clear();

      foreach (var node in FilterNodes(_allNodes, _currentFilter))
      {
        Nodes.Add(node);
      }
    }

    private static IEnumerable<ResourceNode> FilterNodes(IEnumerable<ResourceNode> source, string filter)
    {
      bool hasFilter = !string.IsNullOrWhiteSpace(filter);
      foreach (var node in source)
      {
        var filteredChildren = FilterNodes(node.Children, filter).ToList();
        bool matches = !hasFilter || node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

        if (matches || filteredChildren.Count > 0)
        {
          var clone = node.CloneShallow();
          foreach (var child in filteredChildren)
          {
            clone.Children.Add(child);
          }
          yield return clone;
        }
      }
    }

    private void SetViewer(BlockBase? block)
    {
      // Palette view
      if (block is PaletteData palette)
      {
        var view = new PaletteView();
        view.Load(palette);
        SetContent(view);
        return;
      }

      // Room headers
      if (block is RoomHeader)
      {
        SetContent(new RoomHeaderView { DataContext = block });
        return;
      }

      if (block is RoomImageHeader)
      {
        SetContent(new RoomImageHeaderView { DataContext = block });
        return;
      }

      if (block is DirectoryOfItems directory)
      {
        var view = new DirectoryTableView();
        view.Load(directory);
        SetContent(view);
        return;
      }

      // Images (BOMP)
      if (block is ImageBomp bomp)
      {
        var view = TryCreateBompView(bomp);
        if (view != null)
        {
          SetContent(view);
          return;
        }
      }

      // Z-planes
      if (block is ZPlane zPlane)
      {
        var view = TryCreateZPlaneView(zPlane);
        if (view != null)
        {
          SetContent(view);
          return;
        }
      }

      // Costumes placeholder
      if (block is Costume costume)
      {
        var placeholder = new PlaceholderView();
        placeholder.SetText("Costume", $"Animations: {costume.NumAnim}, Palette entries: {costume.Palette?.Count ?? 0}, Limbs: {costume.Limbs?.Count ?? 0}");
        SetContent(placeholder);
        return;
      }

      // Scripts / sounds placeholder by block type names
      if (block != null && (block.BlockType == "SCRP" || block.BlockType == "SOUN"))
      {
        var placeholder = new PlaceholderView();
        placeholder.SetText(block.BlockType, "Preview not implemented yet. Showing hex fallback if available.");
        var hex = TryHexFallback(block);
        SetContent(hex ?? placeholder);
        return;
      }

      // Not implemented blocks -> hex
      var hexView = TryHexFallback(block);
      if (hexView != null)
      {
        SetContent(hexView);
        return;
      }

      // Default inspector view.
      var defaultView = new DetailsListView { DataContext = this };
      SetContent(defaultView);
    }

    private void ShowPlaceholder(string title, string body)
    {
      var view = new PlaceholderView();
      view.SetText(title, body);
      SetContent(view);
    }

    private void SetContent(Control control)
    {
      if (this.FindControl<ContentControl>("ViewerHost") is { } host)
      {
        host.Content = control;
      }
    }

    private Control? TryCreateBompView(ImageBomp bomp)
    {
      if (!TryGetObjectImageContext(bomp, out var room, out var obj, out var objectIndex, out var imageIndex, out var imageData))
      {
        return TryHexFallback(bomp);
      }

      try
      {
        var decoder = new BompImageDecoder();
        var bmp = decoder.Decode(room, objectIndex, imageIndex);
        if (bmp == null) return TryHexFallback(bomp);

        using (bmp)
        {
          var avaloniaBmp = ConvertToAvaloniaBitmap(bmp);
          var view = new ImageBitmapView();
          view.SetBitmap(avaloniaBmp);
          return view;
        }
      }
      catch
      {
        return TryHexFallback(bomp);
      }
    }

    private Control? TryCreateZPlaneView(ZPlane zPlane)
    {
      var imageData = zPlane.FindAncestor<ImageData>();
      var room = zPlane.FindAncestor<RoomBlock>();
      if (room == null || imageData == null) return TryHexFallback(zPlane);

      try
      {
        var decoder = new ZPlaneDecoder();
        DrawingBitmap bmp;

        var objectImage = zPlane.FindAncestor<ObjectImage>();
        if (objectImage != null)
        {
          var objIndex = room.GetOBIMs().IndexOf(objectImage);
          var imgIndex = objectImage.GetIMxx().IndexOf(imageData);
          var zpIndex = imageData.GetZPlanes().IndexOf(zPlane);
          if (objIndex < 0 || imgIndex < 0 || zpIndex < 0) return TryHexFallback(zPlane);
          bmp = decoder.Decode(room, objIndex, imgIndex, zpIndex);
        }
        else
        {
          var zpIndex = imageData.GetZPlanes().IndexOf(zPlane);
          if (zpIndex < 0) return TryHexFallback(zPlane);
          bmp = decoder.Decode(room, zpIndex);
        }

        if (bmp == null) return TryHexFallback(zPlane);

        using (bmp)
        {
          var avaloniaBmp = ConvertToAvaloniaBitmap(bmp);
          var view = new ImageBitmapView();
          view.SetBitmap(avaloniaBmp);
          return view;
        }
      }
      catch
      {
        return TryHexFallback(zPlane);
      }
    }

    private Control? TryHexFallback(BlockBase? block)
    {
      if (block is NotImplementedDataBlock notImpl && notImpl.Contents != null)
      {
        var view = new HexView();
        view.Load(notImpl.Contents, notImpl.BlockType);
        return view;
      }

      if (block is ValuePaddingBlock valBlock)
      {
        var view = new HexView();
        view.Load(new[] { valBlock.Value, valBlock.Padding }.Select(b => (byte)b).ToArray(), valBlock.BlockType);
        return view;
      }

      if (block is ImageBomp bomp && bomp.Data != null)
      {
        var view = new HexView();
        view.Load(bomp.Data, bomp.BlockType);
        return view;
      }

      if (block is ZPlane zp && zp.Strips != null)
      {
        var combined = zp.Strips.SelectMany(s => s.ImageData ?? Array.Empty<byte>()).ToArray();
        var view = new HexView();
        view.Load(combined, zp.BlockType);
        return view;
      }

      return null;
    }

    #pragma warning disable CA1416
    private static AvaloniaBitmap ConvertToAvaloniaBitmap(DrawingBitmap source)
    {
      using var ms = new MemoryStream();
      source.Save(ms, ImageFormat.Png);
      ms.Position = 0;
      return new AvaloniaBitmap(ms);
    }
    #pragma warning restore CA1416

    private static bool TryGetObjectImageContext(BlockBase block, out RoomBlock? room, out ObjectImage? obj, out int objectIndex, out int imageIndex, out ImageData? imageData)
    {
      room = block.FindAncestor<RoomBlock>();
      obj = block.FindAncestor<ObjectImage>();
      imageData = block.FindAncestor<ImageData>();
      objectIndex = -1;
      imageIndex = -1;

      if (room == null || obj == null || imageData == null) return false;

      var obims = room.GetOBIMs();
      objectIndex = obims.IndexOf(obj);
      imageIndex = obj.GetIMxx().IndexOf(imageData);
      return objectIndex >= 0 && imageIndex >= 0;
    }

    private static string GetGameName(ScummGame game)
    {
      return game switch
      {
        ScummGame.DayOfTheTentacle => "Day of the Tentacle (Talkie)",
        ScummGame.SamAndMax => "Sam & Max Hit The Road (Talkie)",
        ScummGame.FateOfAtlantis => "Indiana Jones And The Fate of Atlantis (Talkie)",
        ScummGame.MonkeyIsland1VGA => "The Secret Of Monkey Island (CD)",
        ScummGame.MonkeyIsland1VGASpeech => "The Secret Of Monkey Island (CD) (Talkie)",
        ScummGame.MonkeyIsland2 => "Monkey Island 2: LeChuck's Revenge (CD)",
        _ => "None"
      };
    }
  }

  public class ResourceNode
  {
    public ResourceNode(string name, BlockBase? block)
    {
      Name = name;
      Block = block;
      Children = new ObservableCollection<ResourceNode>();
    }

    public string Name { get; }
    public BlockBase? Block { get; }
    public ObservableCollection<ResourceNode> Children { get; }

    public ResourceNode CloneShallow()
    {
      return new ResourceNode(Name, Block);
    }
  }

  public class DetailRow
  {
    public DetailRow(string name, string value)
    {
      Name = name;
      Value = value;
    }

    public string Name { get; }
    public string Value { get; }
  }

  internal static class InspectorHelpers
  {
    internal static bool IsSimple(Type type)
    {
      var underlying = Nullable.GetUnderlyingType(type) ?? type;
      return underlying.IsPrimitive
             || underlying.IsEnum
             || underlying == typeof(string)
             || underlying == typeof(decimal)
             || underlying == typeof(DateTime)
             || underlying == typeof(Guid)
             || underlying == typeof(uint)
             || underlying == typeof(long)
             || underlying == typeof(ulong)
             || underlying == typeof(short)
             || underlying == typeof(ushort);
    }

    internal static bool ShouldSkipProperty(string name)
    {
      // Avoid echoing child collections or duplicate metadata; keep this list tight.
      return name is "Childrens" or "Parent" or "UniqueId" or "BlockSize" or "BlockOffSet";
    }
  }
}
