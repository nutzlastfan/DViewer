using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DViewer
{
    /// <summary>
    /// Zentraler Settings-Store (Singleton).
    /// Lädt einmal beim Start, hält eine referenzierte Instanz von AppSettings,
    /// bietet Save/Reload und ein Changed-Event.
    /// </summary>
    public sealed class AppSettingsStore
    {
        // Singleton
        public static AppSettingsStore Instance { get; } = new AppSettingsStore();

        private readonly SemaphoreSlim _gate = new(1, 1);

        // Aktuelle Settings (nach Initialize/LoadAsync gesetzt)
        public AppSettings Settings { get; private set; } = new AppSettings();

        /// <summary>Wird ausgelöst, nachdem Settings neu geladen oder gespeichert wurden.</summary>
        public event Action<AppSettings>? Changed;

        private AppSettingsStore() { }

        /// <summary>
        /// Einmalig zu Beginn aufrufen (z.B. in App.xaml.cs oder im ViewModel-Konstruktor).
        /// </summary>
        public async Task InitializeAsync(string? path = null)
        {
            await LoadAsync(path).ConfigureAwait(false);
        }

        /// <summary>
        /// Settings von Datenträger laden (oder Defaults erstellen).
        /// </summary>
        public async Task LoadAsync(string? path = null)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                Settings = await AppSettings.LoadAsync(path).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
            Changed?.Invoke(Settings);
        }

        /// <summary>
        /// Aktuelle Settings speichern.
        /// </summary>
        public async Task SaveAsync(string? path = null)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await Settings.SaveAsync(path).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
            Changed?.Invoke(Settings);
        }

        /// <summary>
        /// Bequeme Update-API: Änderungen anwenden und direkt persistieren.
        /// </summary>
        public async Task UpdateAsync(Action<AppSettings> apply, string? path = null)
        {
            if (apply is null) return;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                apply(Settings);
                await Settings.SaveAsync(path).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
            Changed?.Invoke(Settings);
        }

        /// <summary>Pfad der Settings-Datei.</summary>
        public string SettingsPath => Settings.SettingsPath;

        /// <summary>Gibt das Verzeichnis zurück, in dem die Settings-Datei liegt.</summary>
        public string SettingsDirectory => Path.GetDirectoryName(Settings.SettingsPath) ?? AppSettings.GetDefaultPath();
    }
}
