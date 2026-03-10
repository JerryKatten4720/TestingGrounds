using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IsometricWPF.Dwellers;
using IsometricWPF.World;

namespace IsometricWPF
{
    // ── WPF visual host ───────────────────────────────────────────────────────

    public sealed class DrawingVisualHost : UIElement
    {
        private readonly VisualCollection _visuals;
        public DrawingVisualHost() => _visuals = new VisualCollection(this);
        public DrawingVisual AddVisual() { var v = new DrawingVisual(); _visuals.Add(v); return v; }
        public void Clear() => _visuals.Clear();
        protected override int    VisualChildrenCount       => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];
    }

    // ── Renderer ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Isometric tile renderer — painter's-algorithm diagonal-sum traversal.
    /// Phase 2: adds fog-of-war shading, day/night global tint, weather vignette,
    /// radiation-zone marker, and resource-node icon overlays.
    /// </summary>
    public sealed class IsometricRenderer
    {
        // ── Tile state ────────────────────────────────────────────────
        private WorldMap?             _map;
        private List<DwellerInstance> _dwellers = new();
        private bool _showGrid    = true;
        private bool _showHeights = true;
        private Rect _viewport    = Rect.Empty;

        // ── Combat overlays ───────────────────────────────────────────
        private IReadOnlySet<(int,int)>?  _moveHighlight;
        private IReadOnlyList<(int,int)>? _pathPreview;

        // ── Phase 2 world state ───────────────────────────────────────
        private FogOfWarMap?   _fog;
        private int            _viewerTeamId = 0;
        private bool           _isNight      = false;
        private WeatherType    _weather      = WeatherType.Clear;

        // ── Phase 3 ───────────────────────────────────────────────────
        private bool _showHpBars = true;
        public  void SetShowHpBars(bool on) { _showHpBars = on; Redraw(); }

        // ── Caches ────────────────────────────────────────────────────
        private readonly Dictionary<string, Brush[]>       _brushCache = new();
        private readonly Dictionary<int, StreamGeometry[]> _geoCache   = new();

        // ── Frozen pens / brushes ─────────────────────────────────────
        private readonly DrawingVisualHost _host;
        private readonly DrawingVisual     _tileVisual;
        private readonly Pen               _gridPen;
        private readonly Pen               _highlightPen;
        private readonly Brush             _highlightFill;
        private readonly Pen               _pathPen;

        // Fog brushes (created once, frozen)
        private readonly Brush _fogDarkBrush;   // never-seen: solid dark
        private readonly Brush _fogSeenBrush;   // seen-but-not-visible: 80% dark overlay
        private readonly Brush _radBrush;       // radiation tint
        private readonly Typeface _iconTypeface = new("Segoe UI Symbol");

        // ── Hover tracking ────────────────────────────────────────────
        private int _lastHoverX = -1, _lastHoverY = -1;

        // ── Convenience ───────────────────────────────────────────────
        private double TileW     => AppConfig.Instance.TileWidth;
        private double TileH     => AppConfig.Instance.TileHeight;
        private double StackStep => AppConfig.Instance.BlockStackStep;

        // ── Events ────────────────────────────────────────────────────
        public delegate void TileHoveredHandler(int gx, int gy);
        public event TileHoveredHandler? TileHovered;
        public event TileHoveredHandler? TileHoverLeft;

        public DrawingVisualHost Host => _host;
        public bool ShowGrid    { get => _showGrid;    set { _showGrid    = value; Redraw(); } }
        public bool ShowHeights { get => _showHeights; set { _showHeights = value; Redraw(); } }

        // ── Constructor ───────────────────────────────────────────────

        public IsometricRenderer()
        {
            _host       = new DrawingVisualHost();
            _tileVisual = _host.AddVisual();

            _gridPen       = new Pen(new SolidColorBrush(Color.FromArgb(55,  0,   0,   0)),   0.5); _gridPen.Freeze();
            _highlightFill = new SolidColorBrush(Color.FromArgb(50,  80, 220,  80));  ((Brush)_highlightFill).Freeze();
            _highlightPen  = new Pen(new SolidColorBrush(Color.FromArgb(140, 80, 255, 80)),   1.0); _highlightPen.Freeze();
            _pathPen       = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 220,  0)),  2.0); _pathPen.Freeze();

            _fogDarkBrush = new SolidColorBrush(Color.FromRgb(18, 18, 28));   ((Brush)_fogDarkBrush).Freeze();
            _fogSeenBrush = new SolidColorBrush(Color.FromArgb(205, 0, 0, 0)); ((Brush)_fogSeenBrush).Freeze();
            _radBrush     = new SolidColorBrush(Color.FromArgb(60, 80, 220,  0)); ((Brush)_radBrush).Freeze();
        }

        // ── Data loading ──────────────────────────────────────────────

        public void LoadMap(WorldMap map)
        {
            _map = map;
            _brushCache.Clear();
            _geoCache.Clear();
            Redraw();
        }

        public void LoadDwellers(List<DwellerInstance> dwellers)
        {
            _dwellers = dwellers ?? new();
            Redraw();
        }

        public void InvalidateBrushCache() => _brushCache.Clear();

        // ── Phase 2: world-state setters ──────────────────────────────

        public void SetFog(FogOfWarMap? fog, int viewerTeamId)
        {
            _fog          = fog;
            _viewerTeamId = viewerTeamId;
        }

        public void SetNight(bool isNight)
        {
            _isNight = isNight;
            Redraw();
        }

        public void SetWeather(WeatherType weather)
        {
            _weather = weather;
            Redraw();
        }

        // ── Combat overlays ───────────────────────────────────────────

        public void SetMovementHighlight(IReadOnlySet<(int,int)>? tiles) => _moveHighlight = tiles;
        public void SetPathPreview(IReadOnlyList<(int,int)>? path)       => _pathPreview   = path;

        // ── Viewport ──────────────────────────────────────────────────

        public void SetViewport(double x, double y, double width, double height)
        {
            _viewport = new Rect(x, y, width, height);
            Redraw();
        }

        // ── Main draw loop ────────────────────────────────────────────

        public void Redraw()
        {
            if (_map == null) return;

            var dwellerLookup = BuildDwellerLookup();
            using var dc = _tileVisual.RenderOpen();

            int maxSum = _map.Columns + _map.Rows - 2;
            int minSum = 0, maxSumV = maxSum;

            if (!_viewport.IsEmpty)
            {
                double hBudget = AppConfig.Instance.MaxStackHeight * StackStep;
                minSum  = Math.Max(0,      (int)((_viewport.Top  - hBudget) / (TileH / 2.0)) - 1);
                maxSumV = Math.Min(maxSum, (int)( _viewport.Bottom           / (TileH / 2.0)) + 1);
            }

            for (int sum = minSum; sum <= maxSumV; sum++)
            {
                int x0 = 0, x1 = sum;
                if (!_viewport.IsEmpty)
                {
                    x0 = Math.Max(0,   (int)(((_viewport.Left  - TileW) / (TileW / 2.0) + sum) / 2.0));
                    x1 = Math.Min(sum, (int)(( _viewport.Right           / (TileW / 2.0) + sum) / 2.0) + 1);
                }

                for (int x = x0; x <= x1; x++)
                {
                    int y = sum - x;
                    if (y < 0 || y >= _map.Rows || x >= _map.Columns) continue;

                    var vis = _fog?.Get(_viewerTeamId, x, y) ?? TileVisibility.Visible;

                    if (vis == TileVisibility.Dark)
                    {
                        DrawDarkTile(dc, x, y);
                        continue;
                    }

                    DrawCell(dc, x, y);
                    DrawWorldOverlays(dc, x, y);
                    DrawCombatOverlays(dc, x, y);

                    if (vis == TileVisibility.Visible)
                    {
                        long k = (long)x << 32 | (uint)y;
                        if (dwellerLookup.TryGetValue(k, out var list))
                            foreach (var d in list) DrawDweller(dc, d);
                    }

                    // "Seen but not currently visible" — draw dark overlay on top of tile
                    if (vis == TileVisibility.Seen)
                        DrawFogOverlay(dc, x, y, _fogSeenBrush);
                }
            }

            DrawPathPreview(dc);
            DrawDayNightTint(dc);
            DrawWeatherVignette(dc);
        }

        // ── Dark (never-seen) tile ────────────────────────────────────

        private void DrawDarkTile(DrawingContext dc, int gx, int gy)
        {
            TileToScreen(gx, gy, out double sx, out double sy);
            var geo = GetTopGeo(0);
            dc.PushTransform(new TranslateTransform(sx, sy));
            dc.DrawGeometry(_fogDarkBrush, null, geo);
            dc.Pop();
        }

        // ── Fog overlay (seen but dim) ────────────────────────────────

        private void DrawFogOverlay(DrawingContext dc, int gx, int gy, Brush brush)
        {
            TileToScreen(gx, gy, out double sx, out double sy);
            int    top      = _map![gx, gy].MaxBlockHeight;
            double heightPx = (_showHeights && top >= 0) ? (top + 1) * StackStep : 0;
            double t        = sy - heightPx;

            var geo = BuildDiamond(sx, t);
            dc.DrawGeometry(brush, null, geo);
        }

        // ── World overlays (radiation, resources) ─────────────────────

        private void DrawWorldOverlays(DrawingContext dc, int gx, int gy)
        {
            var cell = _map![gx, gy];

            // Radiation tint
            if (cell.IsRadiationZone)
                DrawFogOverlay(dc, gx, gy, _radBrush);

            // Resource node icon
            if (cell.Resource != null && !cell.Resource.IsDepleted)
            {
                var center = GetTileCenter(gx, gy);
                var ft = new FormattedText(
                    cell.Resource.Icon,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    _iconTypeface, 14, Brushes.White,
                    VisualTreeHelper.GetDpi(_host).PixelsPerDip);
                dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height - 2));
            }
        }

        // ── Day/night global tint ─────────────────────────────────────

        private void DrawDayNightTint(DrawingContext dc)
        {
            if (!_isNight) return;
            if (_viewport.IsEmpty) return;

            // Semi-transparent blue-black overlay over the entire visible area
            var nightBrush = new SolidColorBrush(Color.FromArgb(90, 10, 10, 50));
            nightBrush.Freeze();
            dc.DrawRectangle(nightBrush, null,
                new Rect(_viewport.X, _viewport.Y, _viewport.Width, _viewport.Height));
        }

        // ── Weather vignette ─────────────────────────────────────────

        private void DrawWeatherVignette(DrawingContext dc)
        {
            if (_weather == WeatherType.Clear || _viewport.IsEmpty) return;

            var (color, alpha) = _weather switch
            {
                WeatherType.Rain      => (Color.FromRgb(80,  100, 140), (byte)30),
                WeatherType.Sandstorm => (Color.FromRgb(180, 140,  60), (byte)45),
                WeatherType.Blizzard  => (Color.FromRgb(200, 220, 255), (byte)50),
                WeatherType.AcidRain  => (Color.FromRgb(60,  180,  60), (byte)30),
                WeatherType.RadStorm  => (Color.FromRgb(80,  220,   0), (byte)40),
                _                     => (Colors.Transparent,            (byte)0),
            };

            if (alpha == 0) return;
            var b = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            b.Freeze();
            dc.DrawRectangle(b, null,
                new Rect(_viewport.X, _viewport.Y, _viewport.Width, _viewport.Height));
        }

        // ── Cell drawing ─────────────────────────────────────────────

        private void DrawCell(DrawingContext dc, int gx, int gy)
        {
            var  cell = _map![gx, gy];
            TileToScreen(gx, gy, out double sx, out double sy);
            Pen? pen = _showGrid ? _gridPen : null;

            dc.PushTransform(new TranslateTransform(sx, sy));

            if (_showHeights && cell.Blocks.Count > 0)
            {
                foreach (var kvp in cell.Blocks.OrderBy(b => b.Key))
                {
                    int    hi   = kvp.Key;
                    var    def  = TileRegistry.Get(kvp.Value);
                    var    brs  = ResolveBrushes(kvp.Value, def);
                    double topY = -(hi + 1) * StackStep;

                    dc.DrawGeometry(brs[1], pen, GetSideGeo(topY, StackStep + 0.5, isLeft: true));
                    dc.DrawGeometry(brs[2], pen, GetSideGeo(topY, StackStep + 0.5, isLeft: false));
                    if (!cell.Blocks.ContainsKey(hi + 1))
                        dc.DrawGeometry(brs[0], pen, GetTopGeo(topY));
                }
            }
            else if (cell.Blocks.Count > 0)
            {
                string? top = cell.TopBlockName;
                if (top != null)
                {
                    var brs = ResolveBrushes(top, TileRegistry.Get(top));
                    dc.DrawGeometry(brs[0], pen, GetTopGeo(0));
                }
            }

            if (cell.Decors.Count > 0)
            {
                double sh = _showHeights ? cell.Blocks.Count * StackStep : 0;
                foreach (var _ in cell.Decors) DrawDecor(dc, sh);
            }

            dc.Pop();
        }

        // ── Combat overlays (move highlight, path) ────────────────────

        private void DrawCombatOverlays(DrawingContext dc, int gx, int gy)
        {
            bool hl   = _moveHighlight?.Contains((gx, gy)) == true;
            bool path = _pathPreview?.Contains((gx, gy))   == true;
            if (!hl && !path) return;

            TileToScreen(gx, gy, out double sx, out double sy);
            int    top      = _map?[gx, gy].MaxBlockHeight ?? -1;
            double heightPx = (_showHeights && top >= 0) ? (top + 1) * StackStep : 0;
            double t        = sy - heightPx;

            var diamond = BuildDiamond(sx, t);

            if (path)
            {
                var pf = new SolidColorBrush(Color.FromArgb(80, 255, 220, 0)); pf.Freeze();
                dc.DrawGeometry(pf, _pathPen, diamond);
            }
            else
            {
                dc.DrawGeometry(_highlightFill, _highlightPen, diamond);
            }
        }

        private void DrawPathPreview(DrawingContext dc)
        {
            if (_pathPreview == null || _pathPreview.Count < 2) return;
            for (int i = 0; i < _pathPreview.Count - 1; i++)
            {
                var ca = GetTileCenter(_pathPreview[i].Item1, _pathPreview[i].Item2);
                var cb = GetTileCenter(_pathPreview[i + 1].Item1, _pathPreview[i + 1].Item2);
                dc.DrawLine(_pathPen, ca, cb);
            }
        }

        // ── Dweller drawing ───────────────────────────────────────────

        private static void DrawDecor(DrawingContext dc, double heightOffset)
            => dc.DrawEllipse(Brushes.LimeGreen, null,
                new Point(AppConfig.Instance.TileWidth / 2, -heightOffset + AppConfig.Instance.TileHeight / 2),
                8, 8);

        private void DrawDweller(DrawingContext dc, DwellerInstance d)
        {
            if (d.IsDead)
            {
                var pos = GetTileCenter(d.TileX, d.TileY);
                var ft  = new FormattedText("✝",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    _iconTypeface, 20, Brushes.Gray,
                    VisualTreeHelper.GetDpi(_host).PixelsPerDip);
                dc.DrawText(ft, new Point(pos.X - ft.Width / 2, pos.Y - ft.Height));
                return;
            }

            var drawing = DwellerVisualFactory.Create(d);
            if (drawing == null) return;
            var center = GetTileCenter(d.TileX, d.TileY);
            dc.PushTransform(new TranslateTransform(center.X, center.Y));
            dc.DrawDrawing(drawing);
            dc.Pop();

            // HP bar drawn outside the frozen sprite (changes every hit)
            if (_showHpBars)
                DwellerVisualFactory.DrawHpBar(dc, d, center);
        }

        // ── Geometry helpers ──────────────────────────────────────────

        private StreamGeometry BuildDiamond(double sx, double t)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(sx + TileW / 2, t), true, true);
                ctx.LineTo(new Point(sx + TileW, t + TileH / 2), true, false);
                ctx.LineTo(new Point(sx + TileW / 2, t + TileH), true, false);
                ctx.LineTo(new Point(sx, t + TileH / 2), true, false);
            }
            g.Freeze();
            return g;
        }

        private StreamGeometry GetTopGeo(double t)
        {
            int key = (int)t;
            if (_geoCache.TryGetValue(key, out var arr) && arr[0] != null) return arr[0];
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(TileW / 2, t), true, true);
                ctx.LineTo(new Point(TileW,     t + TileH / 2), true, false);
                ctx.LineTo(new Point(TileW / 2, t + TileH),     true, false);
                ctx.LineTo(new Point(0,          t + TileH / 2), true, false);
            }
            g.Freeze();
            EnsureGeoSlot(key)[0] = g;
            return g;
        }

        private StreamGeometry GetSideGeo(double t, double h, bool isLeft)
        {
            int key  = (int)(t * 1000 + h);
            int slot = isLeft ? 1 : 2;
            if (_geoCache.TryGetValue(key, out var arr) && arr[slot] != null) return arr[slot];
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                if (isLeft)
                {
                    ctx.BeginFigure(new Point(0, t + TileH / 2), true, true);
                    ctx.LineTo(new Point(TileW / 2, t + TileH),      true, false);
                    ctx.LineTo(new Point(TileW / 2, t + TileH + h),  true, false);
                    ctx.LineTo(new Point(0,          t + TileH / 2 + h), true, false);
                }
                else
                {
                    ctx.BeginFigure(new Point(TileW / 2, t + TileH),       true, true);
                    ctx.LineTo(new Point(TileW,     t + TileH / 2),    true, false);
                    ctx.LineTo(new Point(TileW,     t + TileH / 2 + h), true, false);
                    ctx.LineTo(new Point(TileW / 2, t + TileH + h),    true, false);
                }
            }
            g.Freeze();
            EnsureGeoSlot(key)[slot] = g;
            return g;
        }

        private StreamGeometry[] EnsureGeoSlot(int key)
        {
            if (!_geoCache.TryGetValue(key, out var arr))
                _geoCache[key] = arr = new StreamGeometry[3];
            return arr;
        }

        // ── Brush cache ───────────────────────────────────────────────

        private Brush[] ResolveBrushes(string name, TileDefinition def)
        {
            if (_brushCache.TryGetValue(name, out var cached)) return cached;
            var brushes = new[]
            {
                ProjectBrush(def.TopBrush,   FaceType.Top),
                ProjectBrush(def.LeftBrush,  FaceType.Left),
                ProjectBrush(def.RightBrush, FaceType.Right),
            };
            _brushCache[name] = brushes;
            return brushes;
        }

        private enum FaceType { Top, Left, Right }

        private Brush ProjectBrush(Brush b, FaceType face)
        {
            float factor = face switch { FaceType.Top => 1.0f, FaceType.Left => 0.8f, _ => 0.6f };

            if (b is SolidColorBrush scb)
            {
                var c  = scb.Color;
                var nb = new SolidColorBrush(Color.FromRgb(
                    (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor)));
                nb.Freeze(); return nb;
            }

            if (b is not ImageBrush img) return b;

            var group = new TransformGroup();
            if (face == FaceType.Top)
            {
                group.Children.Add(new MatrixTransform(1, 0.5, -1, 0.5, 0, 0));
                group.Children.Add(new TranslateTransform(TileW / 2.0, 0));
            }
            else if (face == FaceType.Left)
            {
                group.Children.Add(new MatrixTransform(1, 0.5, 0, 1, 0, 0));
            }
            else
            {
                group.Children.Add(new MatrixTransform(1, -0.5, 0, 1, 0, 0));
                group.Children.Add(new TranslateTransform(TileW / 2.0, TileH / 2.0));
            }

            var vp = new Rect(0, 0, 32, 32);
            Brush fin;

            if (factor < 1.0f)
            {
                var dg    = new DrawingGroup();
                dg.Children.Add(new ImageDrawing(img.ImageSource, new Rect(0, 0, 32, 32)));
                var shade = new SolidColorBrush(Color.FromArgb((byte)(255 * (1f - factor)), 0, 0, 0));
                shade.Freeze();
                dg.Children.Add(new GeometryDrawing(shade, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
                fin = new DrawingBrush(dg) { ViewportUnits = BrushMappingMode.Absolute, Viewport = vp, TileMode = TileMode.Tile, Stretch = Stretch.Fill, Transform = group };
            }
            else
            {
                fin = new ImageBrush(img.ImageSource) { ViewportUnits = BrushMappingMode.Absolute, Viewport = vp, TileMode = TileMode.Tile, Stretch = Stretch.Fill, Transform = group };
            }

            fin.Freeze(); return fin;
        }

        // ── Mouse ─────────────────────────────────────────────────────

        public void OnMouseMove(Point worldPos)
        {
            ScreenToTile(_map, worldPos.X, worldPos.Y, out int gx, out int gy);
            if (_map == null || !_map.IsInBounds(gx, gy))
            {
                if (_lastHoverX >= 0) { TileHoverLeft?.Invoke(_lastHoverX, _lastHoverY); _lastHoverX = -1; }
                return;
            }
            if (gx == _lastHoverX && gy == _lastHoverY) return;
            if (_lastHoverX >= 0) TileHoverLeft?.Invoke(_lastHoverX, _lastHoverY);
            _lastHoverX = gx; _lastHoverY = gy;
            TileHovered?.Invoke(gx, gy);
        }

        public void OnMouseLeave()
        {
            if (_lastHoverX >= 0) TileHoverLeft?.Invoke(_lastHoverX, _lastHoverY);
            _lastHoverX = _lastHoverY = -1;
        }

        // ── Coordinate conversion ─────────────────────────────────────

        public void TileToScreen(int gx, int gy, out double sx, out double sy)
        {
            sx = (gx - gy) * (TileW / 2.0);
            sy = (gx + gy) * (TileH / 2.0);
        }

        public void ScreenToTile(double wx, double wy, out int gx, out int gy)
        {
            double ax = wx - TileW / 2.0;
            gx = (int)Math.Floor((ax / (TileW / 2.0) + wy / (TileH / 2.0)) / 2.0);
            gy = (int)Math.Floor((wy / (TileH / 2.0) - ax / (TileW / 2.0)) / 2.0);
        }

        public void ScreenToTile(WorldMap? map, double wx, double wy, out int gx, out int gy)
        {
            map ??= _map;
            if (map == null) { ScreenToTile(wx, wy, out gx, out gy); return; }
            for (int h = AppConfig.Instance.MaxStackHeight; h >= 1; h--)
            {
                double curWy = wy + h * StackStep;
                double ax    = wx - TileW / 2.0;
                int tx = (int)Math.Floor((ax / (TileW / 2.0) + curWy / (TileH / 2.0)) / 2.0);
                int ty = (int)Math.Floor((curWy / (TileH / 2.0) - ax / (TileW / 2.0)) / 2.0);
                if (map.IsInBounds(tx, ty) && map[tx, ty].Blocks.ContainsKey(h - 1))
                { gx = tx; gy = ty; return; }
            }
            ScreenToTile(wx, wy, out gx, out gy);
        }

        // ── Cursor helpers ────────────────────────────────────────────

        public PointCollection GetDiamondPoints(int gx, int gy)
        {
            TileToScreen(gx, gy, out double sx, out double sy);
            int    top      = _map?[gx, gy].MaxBlockHeight ?? -1;
            double heightPx = (_showHeights && top >= 0) ? (top + 1) * StackStep : 0;
            double t        = sy - heightPx;
            return new PointCollection
            {
                new(sx + TileW / 2, t),
                new(sx + TileW,     t + TileH / 2),
                new(sx + TileW / 2, t + TileH),
                new(sx,             t + TileH / 2),
            };
        }

        public Point GetTileCenter(int gx, int gy)
        {
            TileToScreen(gx, gy, out double sx, out double sy);
            int    top      = _map?[gx, gy].MaxBlockHeight ?? -1;
            double heightPx = (_showHeights && top >= 0) ? (top + 1) * StackStep : 0;
            return new Point(sx + TileW / 2, sy - heightPx + TileH / 2);
        }

        // ── Utilities ─────────────────────────────────────────────────

        private Dictionary<long, List<DwellerInstance>> BuildDwellerLookup()
        {
            var lookup = new Dictionary<long, List<DwellerInstance>>();
            foreach (var d in _dwellers)
            {
                long k = (long)d.TileX << 32 | (uint)d.TileY;
                if (!lookup.TryGetValue(k, out var list)) lookup[k] = list = new();
                list.Add(d);
            }
            return lookup;
        }
    }
}
