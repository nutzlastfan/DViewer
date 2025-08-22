// AppSettings.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace DViewer
{
    /// <summary>
    /// Remote DICOM-Knoten (für Send / MWL / Q/R).
    /// </summary>
    public sealed class DicomNode
    {
        public string AeTitle { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 104;
        public string CalledAe { get; set; } = "";
        public bool UseTls { get; set; }
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

            await using var fs = File.Create(p);
            await JsonSerializer.SerializeAsync(fs, this, JsonOptions);
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
