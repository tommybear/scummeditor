using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ScummEditor.Structures.IndexFile;

namespace ScummEditor.AvaloniaApp.Views
{
  public partial class DirectoryTableView : UserControl
  {
    public ObservableCollection<DirectoryItem> Items { get; } = new();

    public DirectoryTableView()
    {
      InitializeComponent();
      DataContext = this;
    }

    private void InitializeComponent()
    {
      AvaloniaXamlLoader.Load(this);
    }

    public void Load(DirectoryOfItems directory)
    {
      Items.Clear();
      if (directory?.Rooms == null) return;
      foreach (var item in directory.Rooms)
      {
        Items.Add(item);
      }
    }
  }
}
