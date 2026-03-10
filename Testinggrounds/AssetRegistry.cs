using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IsometricWPF;

public static class AssetRegistry {
    private const string ManifestFile = "assets_manifest.json";
    private static readonly Dictionary<string, ImageSource> _textures = new();

    public static IEnumerable<string> TextureNames => _textures.Keys;


    public static void Initialize() {
        if (!File.Exists(ManifestFile)) return;
        try {
            var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ManifestFile));
            if (paths != null)
                foreach (var p in paths)
                    AddTexture(p, false);
        }
        catch { }
    }


    public static bool AddTexture(string path, bool save = true) {
        try {
            var name = Path.GetFileName(path);
            if (_textures.ContainsKey(name)) return true;

            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();

            _textures[name] = img;
            if (save) SaveManifest();
            return true;
        }
        catch {
            return false;
        }
    }

    public static void RemoveTexture(string name) {
        if (_textures.Remove(name)) SaveManifest();
    }


    public static ImageSource? GetTexture(string? name) {
        if (string.IsNullOrEmpty(name)) return null;

        if (_textures.TryGetValue(name, out var img)) return img;


        if (File.Exists(name) && AddTexture(name, false))
            return _textures.GetValueOrDefault(Path.GetFileName(name));

        return null;
    }


    private static void SaveManifest() {
        try {
            var paths = _textures.Values
                .OfType<BitmapImage>()
                .Select(b => b.UriSource.IsAbsoluteUri ? b.UriSource.LocalPath : b.UriSource.OriginalString)
                .ToList();
            File.WriteAllText(ManifestFile, JsonSerializer.Serialize(paths));
        }
        catch { }
    }
}