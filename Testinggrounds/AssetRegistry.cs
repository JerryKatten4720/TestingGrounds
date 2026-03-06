using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;

namespace IsometricWPF
{
    public static class AssetRegistry
    {
        private static readonly Dictionary<string, ImageSource> _textures = new();
        private const string ManifestFile = "assets_manifest.json";

        public static IEnumerable<string> TextureNames => _textures.Keys;

        public static void Initialize()
        {
            if (File.Exists(ManifestFile))
            {
                try
                {
                    var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ManifestFile));
                    if (paths != null)
                    {
                        foreach (var path in paths) AddTexture(path, save: false);
                    }
                }
                catch { /* Ignore */ }
            }
        }

        public static bool AddTexture(string path, bool save = true)
        {
            try
            {
                string name = Path.GetFileName(path);
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
            catch { return false; }
        }

        public static void RemoveTexture(string name)
        {
            if (_textures.Remove(name)) SaveManifest();
        }

        public static ImageSource GetTexture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_textures.TryGetValue(name, out var img)) return img;
            
            // Try loading from file if not in registry
            if (File.Exists(name))
            {
                if (AddTexture(name, save: false)) return _textures[Path.GetFileName(name)];
            }
            return null;
        }

        private static void SaveManifest()
        {
            try
            {
                var paths = _textures.Values.Cast<BitmapImage>()
                    .Select(img => img.UriSource.IsAbsoluteUri ? img.UriSource.LocalPath : img.UriSource.OriginalString)
                    .ToList();
                File.WriteAllText(ManifestFile, JsonSerializer.Serialize(paths));
            }
            catch { /* Ignore */ }
        }
    }
}
