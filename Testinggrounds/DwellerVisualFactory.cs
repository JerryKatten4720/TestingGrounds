using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IsometricWPF.Dwellers
{
    /// <summary>
    /// Builds and caches frozen <see cref="DrawingGroup"/> sprites for dweller instances.
    /// Cache key includes all visible state (texture, team, selection, AP) so stale visuals are never served.
    /// </summary>
    public static class DwellerVisualFactory
    {
        private static readonly Dictionary<string, Drawing> _cache = new();

        public static string TextureBasePath = "pack://application:,,,/Assets/dwellers/";

        private static readonly Color[] TeamColors =
        {
            Color.FromRgb(0,   182, 255), // Team 0 – blue
            Color.FromRgb(255, 60,  60),  // Team 1 – red
            Color.FromRgb(80,  220, 80),  // Team 2 – green
            Color.FromRgb(255, 200, 0),   // Team 3 – gold
        };

        public static void InvalidateCache() => _cache.Clear();

        public static Drawing? Create(DwellerInstance dweller)
        {
            // Bug #6 fix: ActionPoints is part of the key so AP changes invalidate the cached sprite
            string key = $"{dweller.Data.Texture}|{dweller.TeamId}|{dweller.State}|{dweller.ActionPoints}|{dweller.Data.DisplayName}";
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var group  = new DrawingGroup();
            var source = LoadImage(dweller.Data.Texture);
            if (source == null) return group;

            // Shadow ellipse
            var shadowBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
            shadowBrush.Freeze();
            group.Children.Add(new GeometryDrawing(shadowBrush, null,
                new EllipseGeometry(new Point(0, 0), 16, 6)));

            // Sprite rect – scale based on image aspect ratio
            double ratio = source.Height / source.Width;
            double w = ratio >= 2.2 ? 32 : 40;
            double h = ratio >= 2.2 ? 78 : 82;
            var spriteRect = new Rect(-w / 2.0, -h, w, h);
            group.Children.Add(new ImageDrawing(source, spriteRect));

            // Selection ring + name tag
            if (dweller.State == DwellerState.Selected)
            {
                var selPen = new Pen(Brushes.White, 2);
                selPen.Freeze();
                group.Children.Add(new GeometryDrawing(null, selPen,
                    new EllipseGeometry(new Point(0, 0), 20, 8)));

                AddNameTag(group, dweller.Data.DisplayName, h);
            }

            // AP indicator dots (always visible so the player knows remaining AP at a glance)
            AddApDots(group, dweller.ActionPoints, dweller.MaxActionPoints, h);

            group.Freeze();
            _cache[key] = group;
            return group;
        }

        // ── Private helpers ───────────────────────────────────────────

        private static void AddNameTag(DrawingGroup group, string name, double spriteH)
        {
            var formatted = new FormattedText(name,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 10, Brushes.White, 1.0);

            double tw = formatted.Width, th = formatted.Height;
            var tagRect = new Rect(-tw / 2 - 4, -spriteH - th - 10, tw + 8, th + 4);

            var tagBg = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
            tagBg.Freeze();

            group.Children.Add(new GeometryDrawing(tagBg, null, new RectangleGeometry(tagRect, 3, 3)));
            group.Children.Add(new GeometryDrawing(Brushes.White, null,
                formatted.BuildGeometry(new Point(-tw / 2, -spriteH - th - 8))));
        }

        private static void AddApDots(DrawingGroup group, int current, int max, double spriteH)
        {
            if (max <= 0) return;
            const double dotR = 3.5, gap = 9.0;
            double totalW = max * gap - (gap - dotR * 2);
            double startX = -totalW / 2 + dotR;
            double y = -spriteH - 4;

            for (int i = 0; i < max; i++)
            {
                bool full  = i < current;
                var fill   = new SolidColorBrush(full ? Color.FromRgb(80, 220, 80) : Color.FromArgb(80, 255, 255, 255));
                fill.Freeze();
                group.Children.Add(new GeometryDrawing(fill, null,
                    new EllipseGeometry(new Point(startX + i * gap, y), dotR, dotR)));
            }
        }

        private static BitmapImage? LoadImage(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName)) return null;
            try
            {
                string sep  = TextureBasePath.EndsWith('/') || TextureBasePath.EndsWith('\\') ? "" : "/";
                var    img  = new BitmapImage(new Uri(TextureBasePath + sep + textureName, UriKind.RelativeOrAbsolute));
                img.Freeze();
                return img;
            }
            catch { return null; }
        }
    }
}
