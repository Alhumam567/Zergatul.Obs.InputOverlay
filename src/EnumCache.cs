using System;
using System.Collections.Generic;

namespace Zergatul.Obs.InputOverlay
{
    public class EnumCache<T, V>
        where T : struct, Enum
    {
        private Dictionary<T, V> dictionary;

        public EnumCache(List<V> values)
        {
            T[] ts = Enum.GetValues<T>();

            if (values.Count != ts.Length) throw new ArgumentOutOfRangeException(nameof(values));

            dictionary = new Dictionary<T, V>();
            
            for (int i = 0; i < ts.Length; i++)
            {
                dictionary.Add(ts[i], values[i]);
            }
        }

        public V this[T value] => dictionary[value];
    }
}