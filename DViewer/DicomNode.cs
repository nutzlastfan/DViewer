// AppSettings.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace DViewer
{
    /// <summary>
    /// Remote DICOM-Knoten (für Send / MWL / Q/R).
    /// Jetzt mit INotifyPropertyChanged, damit UI-Listen bei Änderungen sofort aktualisieren.
    /// </summary>
    public sealed class DicomNode : INotifyPropertyChanged
    {
        string _aeTitle = "";
        string _host = "";
        int _port = 104;
        string _calledAe = "";
        bool _useTls;

        public string AeTitle { get => _aeTitle; set => Set(ref _aeTitle, value); }
        public string Host { get => _host; set => Set(ref _host, value); }
        public int Port { get => _port; set => Set(ref _port, value); }
        public string CalledAe { get => _calledAe; set => Set(ref _calledAe, value); }
        public bool UseTls { get => _useTls; set => Set(ref _useTls, value); }

        public DicomNode Clone() => new()
        {
            AeTitle = AeTitle,
            Host = Host,
            Port = Port,
            CalledAe = CalledAe,
            UseTls = UseTls
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }
    }

    /// <summary>
    /// Persistente App-Einstellungen. JSON-Speicher: AppData/DViewer/settings.json (Fallback: Temp).
    /// </summary>
    public sealed class AppSettings
    {
        // --- Lokaler DICOM-Knoten ---
        public string LocalAeTitle { get; set; } = "DVIEWER";
        public int LocalPort { get; set; } = 104;
        public string LocalStorageFolder { get; set; } = "/data/dicom";
        public int LocalMaxPdu { get; set; } = 16384;
        public bool LocalAcceptIncoming { get; set; } = false;
        public bool LocalUseTls { get; set; } = false;

        // --- Ziel-Listen ---
        public List<DicomNode> SendNodes { get; set; } = new();
        public List<DicomNode> WorklistNodes { get; set; } = new();
        public List<DicomNode> QueryRetrieveNodes { get; set; } = new();

        // interner Pfad (nicht serialisieren)
        [JsonIgnore]
        public string SettingsPath { get; private set; } = GetDefaultPath();

        // ===== Speicherort bestimmen =====
        public static string GetDefaultPath()
        {
            try
            {
                var baseDir = FileSystem.AppDataDirectory;
                if (string.IsNullOrWhiteSpace(baseDir))
                    throw new InvalidOperationException();

                var dir = Path.Combine(baseDir, "DViewer");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.json");
            }
            catch
            {
                var dir = Path.Combine(Path.GetTempPath(), "DViewer");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.json");
            }
        }

        // ===== Laden / Speichern =====
        public static async Task<AppSettings> LoadAsync(string? path = null)
        {
            var p = path ?? GetDefaultPath();

            if (!File.Exists(p))
            {
                var fresh = new AppSettings { SettingsPath = p };
                await fresh.SaveAsync(p);
                return fresh;
            }

            try
            {
                await using var fs = File.OpenRead(p);
                var cfg = await JsonSerializer.DeserializeAsync<AppSettings>(fs, JsonOptions)
                          ?? new AppSettings();
                cfg.SettingsPath = p;
                return cfg;
            }
            catch
            {
                // Bei defekter Datei mit Defaults weiterarbeiten
                return new AppSettings { SettingsPath = p };
            }
        }

        public async Task SaveAsync(string? path = null)
        {
            var p = path ?? SettingsPath ?? GetDefaultPath();
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);

            // optional: atomisches Schreiben (Tempdatei -> Replace)
            var tmp = p + ".tmp";
            await using (var fs = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(fs, this, JsonOptions);
            }
            File.Copy(tmp, p, overwrite: true);
            File.Delete(tmp);

            SettingsPath = p;
        }

        // optionale Sync-Wrapper
        public static AppSettings Load(string? path = null)
            => LoadAsync(path).GetAwaiter().GetResult();

        public void Save(string? path = null)
            => SaveAsync(path).GetAwaiter().GetResult();

        // JSON-Optionen
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
