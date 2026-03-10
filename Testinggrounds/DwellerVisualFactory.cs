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
    /// Phase 3: HP bar is intentionally NOT in the frozen DrawingGroup (it changes every hit).
    ///          It is drawn directly by the renderer using DrawHpBar().
    ///          Cache key no longer includes HP so the sprite isn't thrashed on every damage tick.
    /// </summary>
    public static class DwellerVisualFactory
    {
        private static readonly Dictionary<string, Drawing> _cache = new();

        public static string TextureBasePath = "pack://application:,,,/Assets/dwellers/";

        private static readonly Color[] TeamColors =
        {
            Color.FromRgb(0,   182, 255),  // Team 0 – blue
            Color.FromRgb(255, 60,  60),   // Team 1 – red
            Color.FromRgb(80,  220, 80),   // Team 2 – green
            Color.FromRgb(255, 200, 0),    // Team 3 – gold
            Color.FromRgb(220, 80,  220),  // Team 4 – purple
            Color.FromRgb(255, 140, 0),    // Team 5 – orange
            Color.FromRgb(0,   220, 200),  // Team 6 – teal
            Color.FromRgb(200, 200, 200),  // Team 7 – silver
        };

        public static Color TeamColor(int teamId) =>
            TeamColors[Math.Clamp(teamId, 0, TeamColors.Length - 1)];

        public static void InvalidateCache() => _cache.Clear();

        // ── Sprite creation ───────────────────────────────────────────

        /// <summary>
        /// Returns a frozen DrawingGroup for the dweller's sprite, name tag, and PM dots.
        /// HP bar is excluded so the sprite cache isn't busted on every damage event.
        /// </summary>
        public static Drawing? Create(DwellerInstance dweller)
        {
            // Key: texture | team | state | PM | name  (NOT HP)
            string key = $"{dweller.Data.Texture}|{dweller.TeamId}|{dweller.State}|{dweller.MovementPoints}|{dweller.Data.DisplayName}";
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var group  = new DrawingGroup();
            var source = LoadImage(dweller.Data.Texture);
            if (source == null) return group;

            // Shadow
            var shadowBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
            shadowBrush.Freeze();
            group.Children.Add(new GeometryDrawing(shadowBrush, null,
                new EllipseGeometry(new Point(0, 0), 16, 6)));

            // Sprite
            double ratio = (double)source.PixelHeight / source.PixelWidth;
            double w = ratio >= 2.2 ? 32 : 40;
            double h = ratio >= 2.2 ? 78 : 82;
            group.Children.Add(new ImageDrawing(source, new Rect(-w / 2.0, -h, w, h)));

            // Team-coloured selection ring
            if (dweller.State == DwellerState.Selected)
            {
                var teamColor = TeamColor(dweller.TeamId);
                var selPen    = new Pen(new SolidColorBrush(teamColor), 2.2);
                selPen.Freeze();
                group.Children.Add(new GeometryDrawing(null, selPen,
                    new EllipseGeometry(new Point(0, 0), 20, 8)));
                AddNameTag(group, dweller.Data.DisplayName, h, teamColor);
            }

            // PM dots (always visible)
            AddPmDots(group, dweller.MovementPoints, dweller.MaxMovementPoints, h);

            group.Freeze();
            _cache[key] = group;
            return group;
        }

        // ── HP bar (drawn live by renderer, NOT cached) ───────────────

        /// <summary>
        /// Draws an HP bar above the dweller's tile center into an open DrawingContext.
        /// Call this AFTER drawing the sprite, OUTSIDE the frozen DrawingGroup.
        /// </summary>
        public static void DrawHpBar(DrawingContext dc, DwellerInstance d, Point tileCenter)
        {
            if (!d.ShowHpBar || d.IsDead || d.MaxHP <= 0) return;

            const double barW  = 36.0;
            const double barH  = 4.0;
            const double yOff  = 88.0;     // pixels above tile center
            double ratio = (double)d.HP / d.MaxHP;

            double x0 = tileCenter.X - barW / 2.0;
            double y0 = tileCenter.Y - yOff;

            // Background track
            var trackBrush = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
            trackBrush.Freeze();
            dc.DrawRectangle(trackBrush, null, new Rect(x0, y0, barW, barH));

            // Filled portion: green → yellow → red
            Color fill = ratio > 0.6 ? Color.FromRgb(80, 220, 80)
                       : ratio > 0.3 ? Color.FromRgb(240, 200, 0)
                       :               Color.FromRgb(255, 60, 60);
            var fillBrush = new SolidColorBrush(fill);
            fillBrush.Freeze();
            dc.DrawRectangle(fillBrush, null, new Rect(x0, y0, barW * ratio, barH));
        }

        // ── Private helpers ───────────────────────────────────────────

        private static void AddNameTag(DrawingGroup group, string name, double spriteH, Color teamColor)
        {
            var formatted = new FormattedText(name,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 10, Brushes.White, 1.0);

            double tw = formatted.Width, th = formatted.Height;
            double tagY = -spriteH - th - 10;

            var tagBrush = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
            tagBrush.Freeze();
            group.Children.Add(new GeometryDrawing(tagBrush, null,
                new RectangleGeometry(new Rect(-tw / 2 - 4, tagY - 2, tw + 8, th + 4), 3, 3)));

            // Coloured border under the tag matching team colour
            var borderBrush = new SolidColorBrush(Color.FromArgb(180, teamColor.R, teamColor.G, teamColor.B));
            borderBrush.Freeze();
            var borderPen = new Pen(borderBrush, 1.0);
            borderPen.Freeze();
            group.Children.Add(new GeometryDrawing(null, borderPen,
                new RectangleGeometry(new Rect(-tw / 2 - 4, tagY - 2, tw + 8, th + 4), 3, 3)));

            group.Children.Add(new GeometryDrawing(Brushes.White, null,
                formatted.BuildGeometry(new Point(-tw / 2, tagY))));
        }

        private static void AddPmDots(DrawingGroup group, int current, int max, double spriteH)
        {
            if (max <= 0) return;
            const double dotR = 3.0, gap = 8.0;
            double totalW = max * gap - (gap - dotR * 2);
            double startX = -totalW / 2 + dotR;
            double y = -spriteH - 4;

            for (int i = 0; i < max; i++)
            {
                bool full  = i < current;
                var fill   = new SolidColorBrush(
                    full ? Color.FromRgb(80, 220, 80)
                         : Color.FromArgb(80, 255, 255, 255));
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
                string sep = TextureBasePath.EndsWith('/') || TextureBasePath.EndsWith('\\') ? "" : "/";
                var    img = new BitmapImage(new Uri(TextureBasePath + sep + textureName, UriKind.RelativeOrAbsolute));
                img.Freeze();
                return img;
            }
            catch { return null; }
        }
    }
}
