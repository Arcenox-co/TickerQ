using System;
using System.Collections.Generic;
using System.Linq;

namespace TickerQ.Utilities.Extensions
{
    internal static class EnumerableExtension
    {
        public static IEnumerable<List<T>> Batch<T>(this IEnumerable<T> source, int batchSize)
        {
            return source.Select((value, index) => (index, value))
              .GroupBy(x => x.index / batchSize, x => x.value)
              .Select(x => x.ToList());
        }


        public static IEnumerable<TSource> DistinctBy<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector)
        {
            HashSet<TResult> set = new HashSet<TResult>();

            foreach (var item in source)
            {
                var selectedValue = selector(item);

                if (set.Add(selectedValue))
                    yield return item;
            }
        }
    }
}
