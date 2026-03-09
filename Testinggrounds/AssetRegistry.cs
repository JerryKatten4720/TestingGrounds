using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IsometricWPF
{
    /// <summary>
    /// Central registry for runtime texture assets loaded from the filesystem.
    /// Textures are keyed by filename (not full path) so the same file referenced
    /// from different paths resolves to one cached entry.
    /// </summary>
    public static class AssetRegistry
    {
        private static readonly Dictionary<string, ImageSource> _textures = new();
        private const string ManifestFile = "assets_manifest.json";

        public static IEnumerable<string> TextureNames => _textures.Keys;

        // ── Initialization ────────────────────────────────────────────

        /// <summary>Loads textures listed in the saved manifest (if any).</summary>
        public static void Initialize()
        {
            if (!File.Exists(ManifestFile)) return;
            try
            {
                var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ManifestFile));
                if (paths != null)
                    foreach (var p in paths) AddTexture(p, save: false);
            }
            catch { /* Non-fatal — start with an empty registry */ }
        }

        // ── Add / Remove ──────────────────────────────────────────────

        public static bool AddTexture(string path, bool save = true)
        {
            try
            {
                string name = Path.GetFileName(path);
                if (_textures.ContainsKey(name)) return true;

                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource   = new Uri(path, UriKind.RelativeOrAbsolute);
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

        // ── Lookup ────────────────────────────────────────────────────

        public static ImageSource? GetTexture(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (_textures.TryGetValue(name, out var img)) return img;

            // Lazy-load from disk if the full path was stored
            if (File.Exists(name) && AddTexture(name, save: false))
                return _textures.GetValueOrDefault(Path.GetFileName(name));

            return null;
        }

        // ── Persistence ───────────────────────────────────────────────

        private static void SaveManifest()
        {
            try
            {
                // Bug #7 fix: use OfType<BitmapImage> instead of Cast<BitmapImage>
                // so any non-bitmap ImageSource (e.g. DrawingImage) is silently skipped
                // rather than causing an InvalidCastException.
                var paths = _textures.Values
                    .OfType<BitmapImage>()
                    .Select(b => b.UriSource.IsAbsoluteUri ? b.UriSource.LocalPath : b.UriSource.OriginalString)
                    .ToList();
                File.WriteAllText(ManifestFile, JsonSerializer.Serialize(paths));
            }
            catch { /* Non-fatal */ }
        }
    }
}
