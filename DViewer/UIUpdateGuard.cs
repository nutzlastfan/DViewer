// DViewer/Infrastructure/UIUpdateGuard.cs
// Zweck: Während programmatischer UI-/VM-Updates keine Event-Handler / ConvertBacks / Setters zurückschreiben.
// Verwendung:
//   using (UIUpdateGuard.Begin()) {
//       // ViewModel befüllen, ItemsSource/Selection setzen, Filtertexte initialisieren usw.
//   }
// In dieser Zeit sollten Event-Handler/Converter/Setter "still" sein.

using System;
using System.Threading;

namespace DViewer.Infrastructure
{
    /// <summary>
    /// Reentrante, async-sichere Sperre für programmgesteuerte Updates.
    /// </summary>
    public static class UIUpdateGuard
    {
        private static readonly AsyncLocal<int> _depth = new();

        /// <summary>Aktiv, wenn gerade ein programmatisches Update läuft.</summary>
        public static bool IsActive => _depth.Value > 0;

        /// <summary>Beginnt einen Guard-Block. Nutze "using (UIUpdateGuard.Begin()) { ... }".</summary>
        public static IDisposable Begin()
        {
            _depth.Value = _depth.Value + 1;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _depth.Value = Math.Max(0, _depth.Value - 1);
            }
        }
    }
}
