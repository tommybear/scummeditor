using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ScummEditor.Structures;
using ScummEditor.Structures.DataFile;
using ScummEditor.Structures.IndexFile;
using System.Reflection;

namespace ScummEditor.AvaloniaApp
{
  public partial class MainWindow : Window
  {
    public ObservableCollection<ResourceNode> Nodes { get; } = new();
    public ObservableCollection<DetailRow> Details { get; } = new();

    public MainWindow()
    {
      InitializeComponent();
      DataContext = this;
      HookEvents();
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

        BuildTree(game);

        var gameName = GetGameName(game.LoadedGameInfo?.LoadedGame ?? ScummGame.None);
        SetStatus($"Loaded {gameName} ({path})");
      }
      catch (Exception ex)
      {
        SetStatus($"Failed to load: {ex.Message}");
      }
    }

    private void BuildTree(ScummV6GameData game)
    {
      Nodes.Clear();

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
        Nodes.Add(indexRoot);
      }

      if (game.DataFile != null)
      {
        Nodes.Add(CreateBlockNode(game.DataFile, "Data File"));
      }
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
