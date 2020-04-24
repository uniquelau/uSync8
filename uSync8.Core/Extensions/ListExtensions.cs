using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core;

namespace uSync8.Core.Extensions
{
    public static class ListExtensions
    {
        /// <summary>
        /// Add item to list if the item is not null
        /// </summary>
        public static void AddNotNull<TObject>(this List<TObject> list, TObject item)
        {
            if (item == null) return;
            list.Add(item);
        }

        public static TResult ValueOrDefault<TResult>(this IDictionary<string, string> items, string key, TResult defaultValue)
        {
            if (items == null || !items.ContainsKey(key)) return defaultValue;

            var value = items[key];
            var attempt = value.TryConvertTo<TResult>();
            if (attempt.Success) return attempt.Result;

            return defaultValue;
        }
    }
}
