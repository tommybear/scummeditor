using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using ScummEditor.AvaloniaApp.Views;
using ScummEditor.Core;
using ScummEditor.Encoders;
using ScummEditor.Exceptions;
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
    public ObservableCollection<ResourceNode> FlatNodes { get; } = new();
    public ObservableCollection<DetailRow> Details { get; } = new();

    private readonly List<ResourceNode> _allNodes = new();
    private string _currentFilter = string.Empty;
    private TextBox? _searchBox;
    private Button? _saveButton;
    private Button? _openButton;
    private Button? _exportButton;
    private Button? _importButton;
    private Grid? _rootGrid;
    private ProgressBar? _busyBar;
    private TextBlock? _errorText;
    private TreeView? _treeView;
    private ListBox? _filterList;
    private UserSettings _settings = new();
    private readonly string _settingsPath;
    private ScummV6GameData? _currentGame;
    private string? _currentPath;
    private bool _isBusy;

    public MainWindow()
    {
      _settingsPath = BuildSettingsPath();
      InitializeComponent();
      DataContext = this;
      CaptureLayoutControls();
      LoadSettings();
      HookEvents();
      SetViewer(null);
    }

    private void InitializeComponent()
    {
      AvaloniaXamlLoader.Load(this);
    }

    private void CaptureLayoutControls()
    {
      _openButton = this.FindControl<Button>("OpenButton");
      _saveButton = this.FindControl<Button>("SaveButton");
      _exportButton = this.FindControl<Button>("ExportButton");
      _importButton = this.FindControl<Button>("ImportButton");
      _searchBox = this.FindControl<TextBox>("SearchBox");
      _rootGrid = this.FindControl<Grid>("RootGrid");
      _busyBar = this.FindControl<ProgressBar>("BusyBar");
      _errorText = this.FindControl<TextBlock>("ErrorText");
      _treeView = this.FindControl<TreeView>("ResourceTree");
      _filterList = this.FindControl<ListBox>("FilterList");
    }

    private void HookEvents()
    {
      if (_openButton != null)
      {
        _openButton.Click += async (_, __) => await OnOpenFileAsync();
      }

      if (_saveButton != null)
      {
        _saveButton.Click += async (_, __) => await OnSaveAsync();
        _saveButton.IsEnabled = false;
      }

      if (_exportButton != null)
      {
        _exportButton.Click += async (_, __) => await OnExportAsync();
      }

      if (_importButton != null)
      {
        _importButton.Click += async (_, __) => await OnImportAsync();
      }

      if (_searchBox != null)
      {
        _searchBox.TextChanged += (_, __) =>
        {
          _currentFilter = _searchBox.Text ?? string.Empty;
          ApplyFilter();
        };
        _searchBox.KeyDown += (_, e) =>
        {
          if (e.Key == Key.Down)
          {
            FocusTreeFirstNode();
            e.Handled = true;
          }
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

      if (_filterList != null)
      {
        _filterList.SelectionChanged += (_, __) =>
        {
          if (_filterList.SelectedItem is ResourceNode node)
          {
            SetDetails(node);
          }
        };
      }
    }

    private string BuildSettingsPath()
    {
      var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScummEditor");
      Directory.CreateDirectory(folder);
      return Path.Combine(folder, "avalonia-settings.json");
    }

    private void LoadSettings()
    {
      try
      {
        if (File.Exists(_settingsPath))
        {
          _settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_settingsPath)) ?? new UserSettings();
        }
      }
      catch
      {
        _settings = new UserSettings();
      }

      ApplySettings();
    }

    private void ApplySettings()
    {
      if (_rootGrid != null && _rootGrid.ColumnDefinitions.Count > 0 && _settings.LeftPaneWidth > 0)
      {
        _rootGrid.ColumnDefinitions[0].Width = new GridLength(_settings.LeftPaneWidth, GridUnitType.Pixel);
      }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
      base.OnClosing(e);
      SaveSettings();
    }

    private void SaveSettings()
    {
      try
      {
        if (_rootGrid != null && _rootGrid.ColumnDefinitions.Count > 0)
        {
          _settings.LeftPaneWidth = _rootGrid.ColumnDefinitions[0].ActualWidth;
        }

        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
      }
      catch
      {
        // Ignore persistence errors; not critical for runtime.
      }
    }

    private void FocusTreeFirstNode()
    {
      if (this.FindControl<TreeView>("ResourceTree") is { } tree)
      {
        tree.Focus();
        if (tree.SelectedItem == null && Nodes.Count > 0)
        {
          tree.SelectedItem = Nodes[0];
        }
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
      await RunOperationAsync($"Loading {path}...", () => LoadGameAsync(path));
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
        if (_exportButton != null) _exportButton.IsEnabled = true;
        if (_importButton != null) _importButton.IsEnabled = true;

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
      if (await RunOperationAsync("Saving...", () => Task.Run(() => _currentGame.SaveDataToDisk())))
      {
        SetStatus("Save complete.");
      }
    }

    private async Task OnExportAsync()
    {
      if (_currentGame == null)
      {
        SetStatus("Open a file before exporting.");
        return;
      }

      if (StorageProvider == null)
      {
        SetStatus("Storage provider unavailable.");
        return;
      }

      var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
      {
        Title = "Select export folder",
        AllowMultiple = false
      });

      if (folders == null || folders.Count == 0)
      {
        SetStatus("No folder selected.");
        return;
      }

      var target = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;

      if (await RunOperationAsync($"Exporting to {target}...", () => Task.Run(() => ExportResources(target))))
      {
        SetStatus($"Export complete → {target}");
      }
    }

    private async Task OnImportAsync()
    {
      if (_currentGame == null)
      {
        SetStatus("Open a file before importing.");
        return;
      }

      if (StorageProvider == null)
      {
        SetStatus("Storage provider unavailable.");
        return;
      }

      var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
      {
        Title = "Select import folder",
        AllowMultiple = false
      });

      if (folders == null || folders.Count == 0)
      {
        SetStatus("No folder selected.");
        return;
      }

      var source = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;

      if (await RunOperationAsync($"Importing from {source}...", () => Task.Run(() => ImportResources(source))))
      {
        SetStatus($"Import complete → {source}");
      }
    }

    private void ExportResources(string targetFolder)
    {
      if (_currentGame?.DataFile == null)
      {
        throw new InvalidOperationException("No game is loaded.");
      }

      Directory.CreateDirectory(targetFolder);

      var diskBlocks = _currentGame.DataFile.GetLFLFs();

      var decoder = new ImageDecoder { UseTransparentColor = true };
      var bompDecoder = new BompImageDecoder { UseTransparentColor = true };
      var costumeDecoder = new CostumeImageDecoder { UseTransparentColor = true };
      var zplaneDecoder = new ZPlaneDecoder();
      var convert = new ImageDepthConversor();

      for (int i = 0; i < diskBlocks.Count; i++)
      {
        var currentRoom = (RoomBlock)diskBlocks[i].Childrens.Single(r => r is RoomBlock);

        var background = decoder.Decode(currentRoom);
        if (background != null)
        {
          using (background)
          {
            string backgroundName = Path.Combine(targetFolder, $"Room#{i}.png");
            background.Save(backgroundName, ImageFormat.Png);
            File.WriteAllText(backgroundName + ".idx", string.Join(";", decoder.UsedIndexes ?? new List<int>()));
          }
        }

        var roomZPlanes = currentRoom.GetRMIM().GetIM00().GetZPlanes();
        for (int j = 0; j < roomZPlanes.Count; j++)
        {
          var zplane = zplaneDecoder.Decode(currentRoom, j);
          if (zplane != null)
          {
            using (zplane)
            {
              var zName = Path.Combine(targetFolder, $"Room#{i} ZP#{j}.png");
              zplane.Save(zName, ImageFormat.Png);
            }
          }
        }

        var objects = currentRoom.GetOBIMs();
        for (int objIndex = 0; objIndex < objects.Count; objIndex++)
        {
          var objectImage = objects[objIndex];
          var images = objectImage.GetIMxx();
          for (int imgIndex = 0; imgIndex < images.Count; imgIndex++)
          {
            DrawingBitmap? img = null;
            int[] usedIndexes = Array.Empty<int>();

            if (images[imgIndex].GetSMAP() == null)
            {
              img = bompDecoder.Decode(currentRoom, objIndex, imgIndex);
              usedIndexes = bompDecoder.UsedIndexes.ToArray();
            }
            else
            {
              img = decoder.Decode(currentRoom, objIndex, imgIndex);
              usedIndexes = decoder.UsedIndexes.ToArray();
            }

            if (img != null)
            {
              using (img)
              {
                var objectFilename = Path.Combine(targetFolder, $"Room#{i} Obj#{objIndex} Img#{imgIndex}.png");
                img.Save(objectFilename, ImageFormat.Png);
                File.WriteAllText(objectFilename + ".idx", string.Join(";", usedIndexes));
              }
            }

            var zplanes = images[imgIndex].GetZPlanes();
            for (int zp = 0; zp < zplanes.Count; zp++)
            {
              var objZ = zplaneDecoder.Decode(currentRoom, objIndex, imgIndex, zp);
              if (objZ != null)
              {
                using (objZ)
                {
                  var zpName = Path.Combine(targetFolder, $"Room#{i} Obj#{objIndex} Img#{imgIndex} ZP#{zp}.png");
                  objZ.Save(zpName, ImageFormat.Png);
                }
              }
            }
          }
        }

        var costumes = diskBlocks[i].Childrens.OfType<Costume>().ToList();
        for (int costumeIndex = 0; costumeIndex < costumes.Count; costumeIndex++)
        {
          var costume = costumes[costumeIndex];
          for (int frameIndex = 0; frameIndex < costume.Pictures.Count; frameIndex++)
          {
            if (costume.Pictures[frameIndex].ImageData.Length == 0 || costume.Pictures[frameIndex].ImageData.Length == 1 && costume.Pictures[frameIndex].ImageData[0] == 0)
            {
              continue;
            }

            using var costumeBmp = costumeDecoder.Decode(currentRoom, costume, frameIndex);
            if (costumeBmp != null)
            {
              var c = new List<Color>();
              for (int z = 0; z < 256; z++) c.Add(Color.Black);

              PaletteData defaultPalette = currentRoom.GetDefaultPalette();
              for (int z = 0; z < costume.Palette.Count; z++)
              {
                c[z] = defaultPalette.Colors[costume.Palette[z]];
              }

              var converted = convert.CopyToBpp(costumeBmp, 8, c.ToArray());
              var costumeName = Path.Combine(targetFolder, $"Room#{i} Costume#{costumeIndex} FrameIndex#{frameIndex}.png");
              converted.Save(costumeName, ImageFormat.Png);
            }
          }
        }
      }
    }

    private void ImportResources(string sourceFolder)
    {
      if (_currentGame?.DataFile == null)
      {
        throw new InvalidOperationException("No game is loaded.");
      }

      var files = Directory.GetFiles(sourceFolder, "*.png").Select(f => new AvaloniaImageInfo(f)).ToList();
      var diskBlocks = _currentGame.DataFile.GetLFLFs();

      var encoder = new ImageEncoder();
      var bompEncoder = new BompImageEncoder();
      var costumeEncoder = new CostumeImageEncoder();
      var zplaneEncoder = new ZPlaneEncoder();

      foreach (var file in files)
      {
        if (file.RoomIndex < 0 || file.RoomIndex >= diskBlocks.Count) continue;

        var currentRoom = diskBlocks[file.RoomIndex].GetROOM();
        using var bitmapToEncode = (DrawingBitmap)DrawingBitmap.FromFile(file.Filename);

        var preferredIndexes = Array.Empty<int>();
        var indexFile = file.Filename + ".idx";
        if (File.Exists(indexFile))
        {
          preferredIndexes = File.ReadAllText(indexFile).Split(';', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        }

        try
        {
          switch (file.ImageType)
          {
            case ImageType.Background:
              encoder.PreferredIndexes = new List<int>(preferredIndexes);
              encoder.Encode(currentRoom, bitmapToEncode);
              break;
            case ImageType.ZPlane:
              if (file.ZPlaneIndex < 0) break;
              zplaneEncoder.Encode(currentRoom, bitmapToEncode, file.ZPlaneIndex);
              break;
            case ImageType.Object:
              if (file.ObjectIndex < 0 || file.ImageIndex < 0) break;
              if (currentRoom.GetOBIMs()[file.ObjectIndex].GetIMxx()[file.ImageIndex].GetSMAP() == null)
              {
                bompEncoder.PreferredIndexes = new List<int>(preferredIndexes);
                bompEncoder.Encode(currentRoom, file.ObjectIndex, file.ImageIndex, bitmapToEncode);
              }
              else
              {
                encoder.PreferredIndexes = new List<int>(preferredIndexes);
                encoder.Encode(currentRoom, file.ObjectIndex, file.ImageIndex, bitmapToEncode);
              }
              break;
            case ImageType.ObjectsZPlane:
              if (file.ObjectIndex < 0 || file.ImageIndex < 0 || file.ZPlaneIndex < 0) break;
              zplaneEncoder.Encode(currentRoom, file.ObjectIndex, file.ImageIndex, bitmapToEncode, file.ZPlaneIndex);
              break;
            case ImageType.Costume:
              if (file.CostumeIndex < 0 || file.FrameIndex < 0) break;
              var costume = diskBlocks[file.RoomIndex].GetCostumes()[file.CostumeIndex];
              costumeEncoder.Encode(currentRoom, costume, file.FrameIndex, bitmapToEncode);
              break;
          }
        }
        catch (ImageEncodeException ex)
        {
          throw new InvalidOperationException($"Failed importing {file.Filename}: {ex.Message}", ex);
        }
      }

      _currentGame.PostProcessChanges();
    }

    private sealed class AvaloniaImageInfo
    {
      public string Filename { get; }
      public ImageType ImageType { get; private set; }
      public int RoomIndex { get; private set; }
      public int ZPlaneIndex { get; private set; }
      public int ObjectIndex { get; private set; }
      public int ImageIndex { get; private set; }
      public int CostumeIndex { get; private set; }
      public int FrameIndex { get; private set; }

      public AvaloniaImageInfo(string filename)
      {
        Filename = filename;
        RoomIndex = -1;
        ZPlaneIndex = -1;
        ObjectIndex = -1;
        ImageIndex = -1;
        CostumeIndex = -1;
        FrameIndex = -1;
        ImageType = ImageType.Unknown;

        Parse();
      }

      private void Parse()
      {
        string[] fileParts = Filename.Split(' ');
        foreach (var filePart in fileParts)
        {
          string pName = Path.GetFileNameWithoutExtension(filePart);
          var pairValues = pName.Split('#');
          switch (pairValues[0])
          {
            case "Room":
              RoomIndex = int.Parse(pairValues[1]);
              break;
            case "Costume":
              CostumeIndex = int.Parse(pairValues[1]);
              break;
            case "FrameIndex":
              FrameIndex = int.Parse(pairValues[1]);
              break;
            case "Obj":
              ObjectIndex = int.Parse(pairValues[1]);
              break;
            case "Img":
              ImageIndex = int.Parse(pairValues[1]);
              break;
            case "ZP":
              ZPlaneIndex = int.Parse(pairValues[1]);
              break;
          }
        }

        if (RoomIndex < 0) return;

        if (CostumeIndex >= 0)
        {
          ImageType = ImageType.Costume;
        }
        else if (ObjectIndex >= 0)
        {
          ImageType = ZPlaneIndex >= 0 ? ImageType.ObjectsZPlane : ImageType.Object;
        }
        else
        {
          ImageType = ZPlaneIndex >= 0 ? ImageType.ZPlane : ImageType.Background;
        }
      }
    }

    private enum ImageType
    {
      Unknown = 0,
      Background = 1,
      ZPlane = 2,
      Object = 3,
      ObjectsZPlane = 4,
      Costume = 5
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

    private async Task<bool> RunOperationAsync(string status, Func<Task> action)
    {
      SetBusy(true, status);
      ClearError();
      try
      {
        await action();
        return true;
      }
      catch (Exception ex)
      {
        ShowError(ex.Message);
        return false;
      }
      finally
      {
        SetBusy(false);
      }
    }

    private void SetBusy(bool isBusy, string? status = null)
    {
      _isBusy = isBusy;
      if (_busyBar != null) _busyBar.IsVisible = isBusy;

      bool enable = !isBusy;
      if (_openButton != null) _openButton.IsEnabled = enable;
      if (_saveButton != null) _saveButton.IsEnabled = enable && _currentGame != null;
      if (_exportButton != null) _exportButton.IsEnabled = enable && _currentGame != null;
      if (_importButton != null) _importButton.IsEnabled = enable && _currentGame != null;

      if (!string.IsNullOrWhiteSpace(status))
      {
        SetStatus(status);
      }
    }

    private void ShowError(string message)
    {
      if (_errorText != null)
      {
        _errorText.Text = message;
        _errorText.IsVisible = true;
      }
      SetStatus(message);
    }

    private void ClearError()
    {
      if (_errorText != null)
      {
        _errorText.Text = string.Empty;
        _errorText.IsVisible = false;
      }
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
      bool hasFilter = !string.IsNullOrWhiteSpace(_currentFilter);
      Nodes.Clear();
      FlatNodes.Clear();

      if (hasFilter)
      {
        foreach (var node in FlattenMatches(_allNodes, _currentFilter))
        {
          FlatNodes.Add(node);
        }
      }
      else
      {
        foreach (var node in FilterNodes(_allNodes, _currentFilter))
        {
          Nodes.Add(node);
        }
      }

      UpdateListVisibility(hasFilter);
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

    private static IEnumerable<ResourceNode> FlattenMatches(IEnumerable<ResourceNode> source, string filter)
    {
      foreach (var node in source)
      {
        if (node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
          yield return node;
        }

        foreach (var child in FlattenMatches(node.Children, filter))
        {
          yield return child;
        }
      }
    }

    private void UpdateListVisibility(bool isFiltering)
    {
      if (_treeView != null) _treeView.IsVisible = !isFiltering;
      if (_filterList != null) _filterList.IsVisible = isFiltering;
    }

    private void SetViewer(BlockBase? block)
    {
      Control? preview = null;
      Control? hex = null;

      if (block == null)
      {
        SetContent(new DetailsListView { DataContext = this });
        return;
      }

      if (block is PaletteData palette)
      {
        var view = new PaletteView();
        view.Load(palette);
        preview = view;
      }
      else if (block is RoomHeader)
      {
        preview = new RoomHeaderView { DataContext = block };
      }
      else if (block is RoomImageHeader)
      {
        preview = new RoomImageHeaderView { DataContext = block };
      }
      else if (block is DirectoryOfItems directory)
      {
        var view = new DirectoryTableView();
        view.Load(directory);
        preview = view;
      }
      else if (block is ImageBomp bomp)
      {
        preview = TryCreateBompView(bomp);
        hex ??= TryHexFallback(block);
      }
      else if (block is ImageStripTable || block is ImageData)
      {
        preview = TryCreateSmapView(block);
        hex ??= TryHexFallback(block);
      }
      else if (block is ZPlane zPlane)
      {
        preview = TryCreateZPlaneView(zPlane);
        hex ??= TryHexFallback(block);
      }
      else if (block is Costume costume)
      {
        var placeholder = new PlaceholderView();
        placeholder.SetText("Costume", $"Animations: {costume.NumAnim}, Palette entries: {costume.Palette?.Count ?? 0}, Limbs: {costume.Limbs?.Count ?? 0}");
        preview = placeholder;
      }
      else if (block.BlockType == "SCRP" || block.BlockType == "SOUN")
      {
        var placeholder = new PlaceholderView();
        placeholder.SetText(block.BlockType, "Preview not implemented yet. Use the Hex tab to inspect raw bytes.");
        preview = placeholder;
        hex ??= TryHexFallback(block);
      }
      else if (block.BlockType == "BOXD" || block.BlockType == "BOXM")
      {
        var view = TryCreateBoxView(block);
        preview = view;
        hex ??= TryHexFallback(block);
      }

      hex ??= TryHexFallback(block);

      if (preview != null)
      {
        SetContent(preview, hex);
        return;
      }

      if (hex != null)
      {
        var placeholder = new PlaceholderView();
        placeholder.SetText(block.BlockType, "Preview not implemented yet.");
        SetContent(placeholder, hex);
        return;
      }

      SetContent(new DetailsListView { DataContext = this });
    }

    private void ShowPlaceholder(string title, string body)
    {
      var view = new PlaceholderView();
      view.SetText(title, body);
      SetContent(view);
    }

    private void SetContent(Control control, Control? hex = null)
    {
      if (this.FindControl<ContentControl>("ViewerHost") is { } host)
      {
        if (hex == null)
        {
          host.Content = control;
        }
        else
        {
          host.Content = new TabControl
          {
            SelectedIndex = 0,
            ItemsSource = new object[]
            {
              new TabItem { Header = "Preview", Content = control },
              new TabItem { Header = "Hex", Content = hex }
            }
          };
        }
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

    private Control? TryCreateSmapView(BlockBase block)
    {
      var imageData = block as ImageData ?? block.FindAncestor<ImageData>();
      var room = block.FindAncestor<RoomBlock>();
      if (room == null || imageData == null) return TryHexFallback(block);

      try
      {
        var decoder = new ImageDecoder();
        DrawingBitmap bmp;

        var objectImage = block.FindAncestor<ObjectImage>();
        if (objectImage != null)
        {
          var objIndex = room.GetOBIMs().IndexOf(objectImage);
          var imgIndex = objectImage.GetIMxx().IndexOf(imageData);
          if (objIndex < 0 || imgIndex < 0) return TryHexFallback(block);
          bmp = decoder.Decode(room, objIndex, imgIndex);
        }
        else
        {
          bmp = decoder.Decode(room);
        }

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
        return TryHexFallback(block);
      }
    }

    private Control? TryCreateBoxView(BlockBase block)
    {
      if (block is not NotImplementedDataBlock notImpl || notImpl.Contents == null) return null;

      var room = block.FindAncestor<RoomBlock>();

      if (block.BlockType == "BOXD" && room != null)
      {
        try
        {
          var decoder = new ImageDecoder();
          var bmp = decoder.Decode(room);
          if (bmp != null)
          {
            using (bmp)
            {
              var avaloniaBmp = ConvertToAvaloniaBitmap(bmp);
              var boxes = ParseBoxes(notImpl.Contents);
              var overlay = new BoxOverlayView();
              overlay.Load(avaloniaBmp, boxes);
              return overlay;
            }
          }
        }
        catch
        {
          // fall back to table view below
        }
      }

      var view = new BoxDataView();
      if (block.BlockType == "BOXD")
      {
        view.LoadBoxd(notImpl.Contents);
      }
      else
      {
        view.LoadBoxm(notImpl.Contents);
      }
      return view;
    }

    private static List<BoxRect> ParseBoxes(byte[] data)
    {
      var list = new List<BoxRect>();
      int count = data.Length / 8;
      for (int i = 0; i < count; i++)
      {
        short x1 = ReadInt16(data, i * 8 + 0);
        short y1 = ReadInt16(data, i * 8 + 2);
        short x2 = ReadInt16(data, i * 8 + 4);
        short y2 = ReadInt16(data, i * 8 + 6);
        if (x1 <= -32000 || y1 <= -32000 || x2 <= -32000 || y2 <= -32000)
        {
          continue; // skip sentinel entries
        }

        var x = Math.Min(x1, x2);
        var y = Math.Min(y1, y2);
        var width = Math.Abs(x2 - x1) + 1;
        var height = Math.Abs(y2 - y1) + 1;
        if (width <= 0 || height <= 0) continue;
        list.Add(new BoxRect(x, y, width, height));
      }
      return list;
    }

    private static short ReadInt16(byte[] data, int offset)
    {
      if (offset + 1 >= data.Length) return 0;
      return (short)(data[offset] | (data[offset + 1] << 8));
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

  internal sealed class UserSettings
  {
    public double LeftPaneWidth { get; set; } = 280;
  }
}
