using System;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace IsometricWPF
{
    /// <summary>
    /// Application-wide configuration, persisted as app_config.json.
    /// Use <see cref="Save"/> for immediate writes, <see cref="SaveDebounced"/> for high-frequency callers (e.g. mouse wheel).
    /// </summary>
    public class AppConfig
    {
        // ── Feature flags ─────────────────────────────────────────────
        public bool ShowGrid      { get; set; } = true;
        public bool ShowHeights   { get; set; } = true;
        public bool EditorEnabled { get; set; } = true;
        public bool LimitCamera   { get; set; } = true;

        // ── Visual constants ──────────────────────────────────────────
        public double TileWidth      { get; set; } = 64.0;
        public double TileHeight     { get; set; } = 32.0;
        public double BlockStackStep { get; set; } = 32.0;
        public int    MaxStackHeight { get; set; } = 20;

        // ── Camera / viewport ─────────────────────────────────────────
        public double DefaultZoom  { get; set; } = 1.0;
        public int    DefaultMapCols { get; set; } = 40;
        public int    DefaultMapRows { get; set; } = 40;
        public double CameraMargin   { get; set; } = 200.0;

        // ── Singleton ─────────────────────────────────────────────────
        public static AppConfig Instance { get; private set; } = new();

        private const string ConfigFile = "app_config.json";

        public static void Load()
        {
            if (!File.Exists(ConfigFile)) return;
            try { Instance = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigFile)) ?? new(); }
            catch { /* Leave defaults in place */ }
        }

        public static void Save()
        {
            try { File.WriteAllText(ConfigFile, JsonSerializer.Serialize(Instance, _writeOpts)); }
            catch { /* Non-fatal */ }
        }

        /// <summary>
        /// Coalesces rapid calls into a single disk write after a 500 ms idle period.
        /// Safe to call from the UI thread on every scroll/toggle event.
        /// </summary>
        public static void SaveDebounced()
        {
            _debounceTimer ??= CreateDebounceTimer();
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        // ── Internals ─────────────────────────────────────────────────
        private static DispatcherTimer? _debounceTimer;
        private static readonly JsonSerializerOptions _writeOpts = new() { WriteIndented = true };

        private static DispatcherTimer CreateDebounceTimer()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            t.Tick += (_, _) => { t.Stop(); Save(); };
            return t;
        }
    }
}
