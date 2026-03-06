using System;
using System.IO;
using System.Text.Json;

namespace IsometricWPF
{
    /// <summary>
    /// Application-wide configuration for features and settings.
    /// Allows easy modification of behavior via app_config.json.
    /// </summary>
    public class AppConfig
    {
        // Feature Flags
        public bool ShowGrid        { get; set; } = true;
        public bool ShowHeights     { get; set; } = true;
        public bool EditorEnabled   { get; set; } = true;
        public bool FastRendering   { get; set; } = true;
        public bool LimitCamera     { get; set; } = true;

        // Visual constants (Global editable variables)
        public double TileWidth      { get; set; } = 64.0;
        public double TileHeight     { get; set; } = 32.0;
        public double BlockStackStep { get; set; } = 32.0;
        public int    MaxStackHeight { get; set; } = 20;

        // Camera and Viewport
        public double DefaultZoom   { get; set; } = 1.0;
        public int DefaultMapCols   { get; set; } = 40;
        public int DefaultMapRows   { get; set; } = 40;
        public double CameraMargin  { get; set; } = 200.0; // How far camera can go off-map
        
        private const string ConfigFile = "app_config.json";

        public static AppConfig Instance { get; private set; } = new();

        public static void Load()
        {
            if (File.Exists(ConfigFile))
            {
                try
                {
                    Instance = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigFile)) ?? new();
                }
                catch { /* Ignore */ }
            }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(ConfigFile, JsonSerializer.Serialize(Instance, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* Ignore */ }
        }
    }
}
