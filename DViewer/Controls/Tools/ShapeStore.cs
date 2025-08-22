using System;
using System.Collections.Generic;

namespace DViewer.Controls.Overlays
{
    /// <summary>
    /// Hält ALLE Shapes (Linie, Kreis, Rechteck) pro Bild-Key (SOP|FrameIndex).
    /// Wir speichern heterogene Typen als object:
    /// - MeasureShape (bestehende Klasse)
    /// - CircleShape
    /// - RectShape
    /// </summary>
    public sealed class ShapeStore
    {
        public static ShapeStore Shared { get; } = new ShapeStore();

        private readonly object _gate = new();
        private readonly Dictionary<string, List<object>> _byKey = new();

        public event Action<string>? Changed;

        public IReadOnlyList<object> Snapshot(string key)
        {
            lock (_gate)
            {
                if (!_byKey.TryGetValue(key, out var list)) return Array.Empty<object>();
                // Copy als Snapshot
                return list.ToArray();
            }
        }

        public void Add(string key, object shape)
        {
            lock (_gate)
            {
                if (!_byKey.TryGetValue(key, out var list))
                {
                    list = new List<object>();
                    _byKey[key] = list;
                }
                list.Add(shape);
            }
            Changed?.Invoke(key);
        }

        public void Remove(string key, object shape)
        {
            lock (_gate)
            {
                if (_byKey.TryGetValue(key, out var list))
                    list.Remove(shape);
            }
            Changed?.Invoke(key);
        }

        public void Clear(string key)
        {
            lock (_gate) { _byKey.Remove(key); }
            Changed?.Invoke(key);
        }
    }
}
