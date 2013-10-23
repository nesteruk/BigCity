using System.Collections.Generic;

namespace BigCity
{
  public static class ExtensionMethods
  {
    public static SortedSet<T> ToSortedSet<T>(this IEnumerable<T> self)
    {
      var result = new SortedSet<T>();
      foreach (var i in self)
        result.Add(i);
      return result;
    }

    public static HashSet<T> ToSet<T>(this IEnumerable<T> self)
    {
      var result = new HashSet<T>();
      foreach (var i in self)
        result.Add(i);
      return result;
    }
  }
}