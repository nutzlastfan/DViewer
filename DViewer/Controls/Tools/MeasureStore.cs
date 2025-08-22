using System;
using System.Collections.Generic;

namespace DViewer.Controls.Overlays
{
    public sealed class MeasureStore
    {
        public static MeasureStore Shared { get; } = new MeasureStore();

        private readonly object _gate = new();
        private readonly Dictionary<string, List<MeasureShape>> _byKey = new();

        /// <summary>Event, wenn sich die Shapes zu einem Key geändert haben.</summary>
        public event Action<string>? Changed;

        /// <summary>Snapshot (Kopie) der Shapes für Key holen (thread-safe).</summary>
        public IReadOnlyList<MeasureShape> Snapshot(string key)
        {
            lock (_gate)
            {
                if (!_byKey.TryGetValue(key, out var list)) return Array.Empty<MeasureShape>();
                return list.ToArray();
            }
        }

        /// <summary>Shape hinzufügen (thread-safe) und Änderung signalisieren.</summary>
        public void Add(string key, MeasureShape shape)
        {
            lock (_gate)
            {
                if (!_byKey.TryGetValue(key, out var list))
                {
                    list = new List<MeasureShape>();
                    _byKey[key] = list;
                }
                list.Add(shape);
            }
            Changed?.Invoke(key);
        }

        /// <summary>Optional: Alles zu einem Key löschen.</summary>
        public void Clear(string key)
        {
            lock (_gate) { _byKey.Remove(key); }
            Changed?.Invoke(key);
        }
    }
}
