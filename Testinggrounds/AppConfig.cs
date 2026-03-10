using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace IsometricWPF;

public class AppConfig {
    private const string ConfigFile = "app_config.json";


    private static DispatcherTimer? _debounceTimer;

    private static readonly JsonSerializerOptions _writeOpts = new() { WriteIndented = true };


    public bool ShowGrid { get; set; } = true;
    public bool ShowHeights { get; set; } = true;
    public bool EditorEnabled { get; set; } = true;
    public bool LimitCamera { get; set; } = true;


    public double TileWidth { get; set; } = 64.0;
    public double TileHeight { get; set; } = 32.0;
    public double BlockStackStep { get; set; } = 32.0;
    public int MaxStackHeight { get; set; } = 20;


    public double DefaultZoom { get; set; } = 1.0;
    public int DefaultMapCols { get; set; } = 40;
    public int DefaultMapRows { get; set; } = 40;
    public double CameraMargin { get; set; } = 200.0;


    public static AppConfig Instance { get; private set; } = new();

    public static void Load() {
        if (!File.Exists(ConfigFile)) return;
        try {
            Instance = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigFile)) ?? new AppConfig();
        }
        catch { }
    }

    public static void Save() {
        try {
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(Instance, _writeOpts));
        }
        catch { }
    }


    public static void SaveDebounced() {
        _debounceTimer ??= CreateDebounceTimer();
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private static DispatcherTimer CreateDebounceTimer() {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        t.Tick += (_, _) => {
            t.Stop();
            Save();
        };
        return t;
    }
}