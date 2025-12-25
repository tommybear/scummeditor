#nullable enable
using System.Linq;
using ScummEditor.Structures;

namespace ScummEditor.Core
{
  public static class BlockNavigatorExtensions
  {
    public static T? FindAncestor<T>(this BlockBase? block) where T : BlockBase
    {
      var current = block?.Parent;
      while (current != null)
      {
        if (current is T match) return match;
        current = current.Parent;
      }
      return null;
    }

    public static int IndexInParent(this BlockBase block)
    {
      if (block.Parent == null || block.Parent.Childrens == null) return -1;
      return block.Parent.Childrens.IndexOf(block);
    }

    public static int IndexAmongSiblings<T>(this BlockBase block) where T : BlockBase
    {
      if (block.Parent == null) return -1;
      var list = block.Parent.Childrens.OfType<T>().ToList();
      return list.IndexOf((T)block);
    }
  }
}
