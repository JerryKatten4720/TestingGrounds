using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IsometricWPF.Dwellers;

namespace IsometricWPF
{
    // ── WPF visual host ───────────────────────────────────────────────────────

    /// <summary>Lightweight UIElement that owns a collection of DrawingVisuals.</summary>
    public sealed class DrawingVisualHost : UIElement
    {
        private readonly VisualCollection _visuals;

        public DrawingVisualHost() => _visuals = new VisualCollection(this);

        public DrawingVisual AddVisual()
        {
            var v = new DrawingVisual();
            _visuals.Add(v);
            return v;
        }

        public void Clear() => _visuals.Clear();

        protected override int    VisualChildrenCount          => _visuals.Count;
        protected override Visual GetVisualChild(int index)    => _visuals[index];
    }


    // ── Renderer ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Isometric tile renderer using a painter's-algorithm diagonal-sum traversal.
    /// Viewport culling keeps large maps performant; geometry and brush results are cached.
    /// </summary>
    public sealed class IsometricRenderer
    {
        // ── State ─────────────────────────────────────────────────────
        private WorldMap?                  _map;
        private List<DwellerInstance>      _dwellers = new();
        private bool                       _showGrid    = true;
        private bool                       _showHeights = true;
        private Rect                       _viewport    = Rect.Empty;

        // ── Caches ────────────────────────────────────────────────────
        private readonly Dictionary<string, Brush[]>         _brushCache = new();
        private readonly Dictionary<int, StreamGeometry[]>   _geoCache   = new();

        // ── WPF objects ───────────────────────────────────────────────
        private readonly DrawingVisualHost _host;
        private readonly DrawingVisual     _tileVisual;
        private readonly Pen               _gridPen;

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

        // ── Public surface ────────────────────────────────────────────
        public DrawingVisualHost Host => _host;

        public bool ShowGrid
        {
            get => _showGrid;
            set { _showGrid = value; Redraw(); }
        }

        public bool ShowHeights
        {
            get => _showHeights;
            set { _showHeights = value; Redraw(); }
        }

        // ── Constructor ───────────────────────────────────────────────

        public IsometricRenderer()
        {
            _host       = new DrawingVisualHost();
            _tileVisual = _host.AddVisual();

            _gridPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)), 0.5);
            _gridPen.Freeze();
        }

        // ── Map / dweller loading ─────────────────────────────────────

        public void LoadMap(WorldMap map)
        {
            _map = map;
            _brushCache.Clear();
            _geoCache.Clear();
            Redraw();
        }

        public void LoadDwellers(List<DwellerInstance> dwellers)
        {
            _dwellers = dwellers ?? new List<DwellerInstance>();
            Redraw();
        }

        public void InvalidateBrushCache()
        {
            _brushCache.Clear();
        }

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

            // Build a fast lookup from tile coords to dweller list
            var dwellerLookup = BuildDwellerLookup();

            using var dc = _tileVisual.RenderOpen();

            int maxSum = _map.Columns + _map.Rows - 2;

            // Bug #1 fix: culling offset must account for the full configured stack height,
            // not a hardcoded magic number of 10.
            int minSum = 0, maxSumV = maxSum;
            if (!_viewport.IsEmpty)
            {
                double heightBudget = AppConfig.Instance.MaxStackHeight * StackStep;
                minSum  = Math.Max(0,      (int)((_viewport.Top  - heightBudget) / (TileH / 2.0)) - 1);
                maxSumV = Math.Min(maxSum, (int)((_viewport.Bottom)               / (TileH / 2.0)) + 1);
            }

            for (int sum = minSum; sum <= maxSumV; sum++)
            {
                int x0 = 0, x1 = sum;
                if (!_viewport.IsEmpty)
                {
                    x0 = Math.Max(0,   (int)(((_viewport.Left  - TileW) / (TileW / 2.0) + sum) / 2.0));
                    x1 = Math.Min(sum, (int)(((_viewport.Right)          / (TileW / 2.0) + sum) / 2.0) + 1);
                }

                for (int x = x0; x <= x1; x++)
                {
                    int y = sum - x;
                    if (y < 0 || y >= _map.Rows || x >= _map.Columns) continue;

                    DrawCell(dc, x, y);

                    long k = (long)x << 32 | (uint)y;
                    if (dwellerLookup.TryGetValue(k, out var list))
                        foreach (var d in list) DrawDweller(dc, d);
                }
            }
        }

        // ── Cell drawing ─────────────────────────────────────────────

        private void DrawCell(DrawingContext dc, int gx, int gy)
        {
            var cell = _map![gx, gy];
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

                    // Only draw the top face if no block sits directly above
                    if (!cell.Blocks.ContainsKey(hi + 1))
                        dc.DrawGeometry(brs[0], pen, GetTopGeo(topY));
                }
            }
            else if (cell.Blocks.Count > 0)
            {
                // Heights hidden: draw only the topmost face flat
                string? top = cell.TopBlockName;
                if (top != null)
                {
                    var brs = ResolveBrushes(top, TileRegistry.Get(top));
                    dc.DrawGeometry(brs[0], pen, GetTopGeo(0));
                }
            }

            // Decors
            if (cell.Decors.Count > 0)
            {
                double stackH = _showHeights ? cell.Blocks.Count * StackStep : 0;
                foreach (var name in cell.Decors)
                    DrawDecor(dc, name, stackH);
            }

            dc.Pop();
        }

        private static void DrawDecor(DrawingContext dc, string decorName, double heightOffset)
        {
            // TODO: replace with real decor sprites; green dot is a placeholder
            dc.DrawEllipse(Brushes.Green, null,
                new Point(AppConfig.Instance.TileWidth / 2, -heightOffset + AppConfig.Instance.TileHeight / 2),
                10, 10);
        }

        private void DrawDweller(DrawingContext dc, DwellerInstance d)
        {
            var drawing = DwellerVisualFactory.Create(d);
            if (drawing == null) return;
            var pos = GetTileCenter(d.TileX, d.TileY);
            dc.PushTransform(new TranslateTransform(pos.X, pos.Y));
            dc.DrawDrawing(drawing);
            dc.Pop();
        }

        // ── Geometry cache ────────────────────────────────────────────

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
                    ctx.BeginFigure(new Point(0,          t + TileH / 2),     true, true);
                    ctx.LineTo(new Point(TileW / 2, t + TileH),          true, false);
                    ctx.LineTo(new Point(TileW / 2, t + TileH + h),      true, false);
                    ctx.LineTo(new Point(0,          t + TileH / 2 + h), true, false);
                }
                else
                {
                    ctx.BeginFigure(new Point(TileW / 2, t + TileH),          true, true);
                    ctx.LineTo(new Point(TileW,     t + TileH / 2),      true, false);
                    ctx.LineTo(new Point(TileW,     t + TileH / 2 + h),  true, false);
                    ctx.LineTo(new Point(TileW / 2, t + TileH + h),      true, false);
                }
            }
            g.Freeze();
            EnsureGeoSlot(key)[slot] = g;
            return g;
        }

        private StreamGeometry[] EnsureGeoSlot(int key)
        {
            if (!_geoCache.TryGetValue(key, out var arr))
            {
                arr = new StreamGeometry[3];
                _geoCache[key] = arr;
            }
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
                var c    = scb.Color;
                var newB = new SolidColorBrush(Color.FromRgb(
                    (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor)));
                newB.Freeze();
                return newB;
            }

            if (b is not ImageBrush img) return b;

            // Build an affine transform that maps the square image onto the isometric face
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

            var viewport = new Rect(0, 0, 32, 32);
            Brush final;

            if (factor < 1.0f)
            {
                // Composite: image + darkening overlay for left/right faces
                var dg = new DrawingGroup();
                dg.Children.Add(new ImageDrawing(img.ImageSource, new Rect(0, 0, 32, 32)));
                var shade = new SolidColorBrush(Color.FromArgb((byte)(255 * (1f - factor)), 0, 0, 0));
                shade.Freeze();
                dg.Children.Add(new GeometryDrawing(shade, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
                final = new DrawingBrush(dg)
                    { ViewportUnits = BrushMappingMode.Absolute, Viewport = viewport, TileMode = TileMode.Tile, Stretch = Stretch.Fill, Transform = group };
            }
            else
            {
                final = new ImageBrush(img.ImageSource)
                    { ViewportUnits = BrushMappingMode.Absolute, Viewport = viewport, TileMode = TileMode.Tile, Stretch = Stretch.Fill, Transform = group };
            }

            final.Freeze();
            return final;
        }

        // ── Mouse interaction ─────────────────────────────────────────

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

        /// <summary>Flat (height=0) screen→tile conversion.</summary>
        public void ScreenToTile(double wx, double wy, out int gx, out int gy)
        {
            double ax = wx - TileW / 2.0;
            gx = (int)Math.Floor((ax / (TileW / 2.0) + wy / (TileH / 2.0)) / 2.0);
            gy = (int)Math.Floor((wy / (TileH / 2.0) - ax / (TileW / 2.0)) / 2.0);
        }

        /// <summary>
        /// Height-aware screen→tile conversion: walks down from the highest possible block
        /// so that clicking the top face of a tall stack returns the correct tile.
        /// Bug #5 fix: loop starts at MaxStackHeight and stops at h >= 1 (not h >= 0),
        /// avoiding the off-by-one that caused ContainsKey(-1) lookups.
        /// </summary>
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
                {
                    gx = tx; gy = ty; return;
                }
            }
            ScreenToTile(wx, wy, out gx, out gy);
        }

        // ── Diamond / center helpers (used by MainWindow for cursors) ─

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

        // ── Private utilities ─────────────────────────────────────────

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
