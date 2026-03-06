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





    public class DrawingVisualHost : UIElement
    {
        private readonly VisualCollection _visuals;

        public DrawingVisualHost()
        {
            _visuals = new VisualCollection(this);
        }

        public DrawingVisual AddVisual()
        {
            var v = new DrawingVisual();
            _visuals.Add(v);
            return v;
        }

        public void Clear() => _visuals.Clear();

        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];
    }







    public class IsometricRenderer
    {
        private WorldMap _map;
        private List<DwellerInstance> _dwellers = new();
        private bool     _showGrid    = true;
        private bool     _showHeights = true;

        private readonly DrawingVisualHost _host;
        private readonly DrawingVisual     _tileVisual;
        private readonly Pen               _gridPen;


        private Rect _viewport = Rect.Empty;


        private readonly Dictionary<string, Brush[]> _brushCache = new();


        private readonly Dictionary<int, StreamGeometry[]> _geoCache = new();


        private double TileW => AppConfig.Instance.TileWidth;
        private double TileH => AppConfig.Instance.TileHeight;
        private double StackStep => AppConfig.Instance.BlockStackStep;

        public delegate void TileHoveredHandler(int gx, int gy);
        public event TileHoveredHandler TileHovered;
        public event TileHoveredHandler TileHoverLeft;

        private int _lastHoverX = -1, _lastHoverY = -1;

        public DrawingVisualHost Host => _host;

        public IsometricRenderer()
        {
            _host       = new DrawingVisualHost();
            _tileVisual = _host.AddVisual();

            _gridPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)), 0.5);
            _gridPen.Freeze();
        }

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

        public void InvalidateBrushCache() => _brushCache.Clear();



        public void SetViewport(double x, double y, double width, double height)
        {
            _viewport = new Rect(x, y, width, height);
            Redraw();
        }

        public void Redraw()
        {
            if (_map == null) return;


            var dwellerLookup = new Dictionary<long, List<DwellerInstance>>();
            foreach (var d in _dwellers)
            {
                long key = (long)d.TileX << 32 | (uint)d.TileY;
                if (!dwellerLookup.TryGetValue(key, out var list))
                {
                    list = new List<DwellerInstance>();
                    dwellerLookup[key] = list;
                }
                list.Add(d);
            }

            using var dc = _tileVisual.RenderOpen();


            int maxSum = _map.Columns + _map.Rows - 2;


            int minSumVisible = 0;
            int maxSumVisible = maxSum;

            if (!_viewport.IsEmpty)
            {



                minSumVisible = Math.Max(0, (int)((_viewport.Top - 10 * StackStep) / (TileH / 2.0)) - 1);
                maxSumVisible = Math.Min(maxSum, (int)((_viewport.Bottom) / (TileH / 2.0)) + 1);
            }

            for (int sum = minSumVisible; sum <= maxSumVisible; sum++)
            {
                int minX = 0;
                int maxX = sum;

                if (!_viewport.IsEmpty)
                {
                    minX = Math.Max(0, (int)(((_viewport.Left - TileW) / (TileW / 2.0) + sum) / 2.0));
                    maxX = Math.Min(sum, (int)(((_viewport.Right) / (TileW / 2.0) + sum) / 2.0) + 1);
                }

                for (int x = minX; x <= maxX; x++)
                {
                    int y = sum - x;
                    if (y >= 0 && y < _map.Rows && x < _map.Columns) 
                    {
                        DrawCell(dc, x, y);
                        

                        long k = (long)x << 32 | (uint)y;
                        if (dwellerLookup.TryGetValue(k, out var list))
                        {
                            foreach (var dweller in list)
                                DrawDweller(dc, dweller);
                        }
                    }
                }
            }
        }

        private void DrawDweller(DrawingContext dc, DwellerInstance dweller) {
            var drawing = DwellerVisualFactory.Create(dweller);
            if (drawing == null) return;

            Point pos = GetTileCenter(dweller.TileX, dweller.TileY);
            dc.PushTransform(new TranslateTransform(pos.X, pos.Y));
            dc.DrawDrawing(drawing);
            dc.Pop();
        }

        private void DrawCell(DrawingContext drawingContext, int gridX, int gridY)
        {
            var cell = _map[gridX, gridY];
            TileToScreen(gridX, gridY, out double screenX, out double screenY);
            Pen gridPen = _showGrid ? _gridPen : null;

            drawingContext.PushTransform(new TranslateTransform(screenX, screenY));

            if (_showHeights && cell.Blocks.Count > 0)
            {

                foreach (var kvp in cell.Blocks.OrderBy(b => b.Key))
                {
                    int heightIndex = kvp.Key;
                    string blockName = kvp.Value;
                    var blockDefinition = TileRegistry.Get(blockName);
                    var brushes = ResolveBrushes(blockName, blockDefinition);

                    double topY = -(heightIndex + 1) * StackStep;
                    

                    drawingContext.DrawGeometry(brushes[1], gridPen, GetSideGeo(topY, StackStep + 0.5, true));
                    drawingContext.DrawGeometry(brushes[2], gridPen, GetSideGeo(topY, StackStep + 0.5, false));


                    if (!cell.Blocks.ContainsKey(heightIndex + 1))
                    {
                        drawingContext.DrawGeometry(brushes[0], gridPen, GetTopGeo(topY));
                    }
                }
            }
            else if (cell.Blocks.Count == 0)
            {



            }
            else
            {

                int maxHeight = -1;
                foreach (var h in cell.Blocks.Keys) if (h > maxHeight) maxHeight = h;
                
                if (maxHeight >= 0)
                {
                    string topBlockName = cell.Blocks[maxHeight];
                    var brushes = ResolveBrushes(topBlockName, TileRegistry.Get(topBlockName));
                    drawingContext.DrawGeometry(brushes[0], gridPen, GetTopGeo(0));
                }
            }


            if (cell.Decors.Count > 0)
            {
                double stackHeightPx = _showHeights ? cell.Blocks.Count * StackStep : 0;
                foreach (var decorName in cell.Decors)
                {
                    DrawDecor(drawingContext, decorName, stackHeightPx);
                }
            }

            drawingContext.Pop();
        }



        private StreamGeometry GetTopGeo(double t)
        {
            int key = (int)t;
            if (_geoCache.TryGetValue(key, out var cached) && cached[0] != null) return cached[0];

            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(TileW / 2.0, t), true, true);
                ctx.LineTo(new Point(TileW, t + TileH / 2.0), true, false);
                ctx.LineTo(new Point(TileW / 2.0, t + TileH), true, false);
                ctx.LineTo(new Point(0, t + TileH / 2.0), true, false);
            }
            g.Freeze();
            
            if (!_geoCache.ContainsKey(key)) _geoCache[key] = new StreamGeometry[3];
            _geoCache[key][0] = g;
            return g;
        }

        private StreamGeometry GetSideGeo(double t, double h, bool left)
        {
            int key = (int)(t * 1000 + h);
            int slot = left ? 1 : 2;
            if (_geoCache.TryGetValue(key, out var cached) && cached[slot] != null) return cached[slot];

            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                if (left)
                {
                    ctx.BeginFigure(new Point(0, t + TileH / 2.0), true, true);
                    ctx.LineTo(new Point(TileW / 2.0, t + TileH), true, false);
                    ctx.LineTo(new Point(TileW / 2.0, t + TileH + h), true, false);
                    ctx.LineTo(new Point(0, t + TileH / 2.0 + h), true, false);
                }
                else
                {
                    ctx.BeginFigure(new Point(TileW / 2.0, t + TileH), true, true);
                    ctx.LineTo(new Point(TileW, t + TileH / 2.0), true, false);
                    ctx.LineTo(new Point(TileW, t + TileH / 2.0 + h), true, false);
                    ctx.LineTo(new Point(TileW / 2.0, t + TileH + h), true, false);
                }
            }
            g.Freeze();

            if (!_geoCache.ContainsKey(key)) _geoCache[key] = new StreamGeometry[3];
            _geoCache[key][slot] = g;
            return g;
        }

        private void DrawDecor(DrawingContext dc, string decorName, double heightOffset)
        {


            var decorBrush = Brushes.Green;
            dc.DrawEllipse(decorBrush, null, new Point(TileW / 2, -heightOffset + TileH / 2), 10, 10);
        }

        private Brush[] ResolveBrushes(string tileName, TileDefinition definition)
        {
            if (_brushCache.TryGetValue(tileName, out var cached)) return cached;

            var brushes = new Brush[]
            {
                ProjectBrush(definition.TopBrush,   FaceType.Top),
                ProjectBrush(definition.LeftBrush,  FaceType.Left),
                ProjectBrush(definition.RightBrush, FaceType.Right)
            };

            _brushCache[tileName] = brushes;
            return brushes;
        }

        private enum FaceType { Top, Left, Right }

        private Brush ProjectBrush(Brush b, FaceType face)
        {
            float factor = face switch { FaceType.Top => 1.0f, FaceType.Left => 0.8f, _ => 0.6f };

            if (b is SolidColorBrush scb)
            {
                var c = scb.Color;
                var newB = new SolidColorBrush(Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor)));
                newB.Freeze();
                return newB;
            }

            if (!(b is ImageBrush img)) return b;


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

            Brush finalBrush;
            if (factor < 1.0f)
            {

                var dg = new DrawingGroup();
                dg.Children.Add(new ImageDrawing(img.ImageSource, new Rect(0, 0, 32, 32)));
                
                var shadeBrush = new SolidColorBrush(Color.FromArgb((byte)(255 * (1.0 - factor)), 0, 0, 0));
                shadeBrush.Freeze();
                dg.Children.Add(new GeometryDrawing(shadeBrush, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
                
                finalBrush = new DrawingBrush(dg)
                {
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = viewport,
                    TileMode = TileMode.Tile,
                    Stretch  = Stretch.Fill,
                    Transform = group
                };
            }
            else
            {
                finalBrush = new ImageBrush(img.ImageSource)
                {
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport      = viewport,
                    TileMode      = TileMode.Tile,
                    Stretch       = Stretch.Fill,
                    Transform     = group
                };
            }

            finalBrush.Freeze();
            return finalBrush;
        }



        public void OnMouseMove(Point worldPos)
        {
            ScreenToTile(_map, worldPos.X, worldPos.Y, out int gx, out int gy);

            if (_map == null || !_map.IsInBounds(gx, gy))
            {
                if (_lastHoverX >= 0) { TileHoverLeft?.Invoke(_lastHoverX, _lastHoverY); _lastHoverX = _lastHoverY = -1; }
                return;
            }

            if (gx == _lastHoverX && gy == _lastHoverY) return;

            if (_lastHoverX >= 0) TileHoverLeft?.Invoke(_lastHoverX, _lastHoverY);
            _lastHoverX = gx;
            _lastHoverY = gy;
            TileHovered?.Invoke(gx, gy);
        }

        public void OnMouseLeave()
        {
            if (_lastHoverX >= 0) TileHoverLeft?.Invoke(_lastHoverX, _lastHoverY);
            _lastHoverX = _lastHoverY = -1;
        }



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

        public void ScreenToTile(WorldMap map, double wx, double wy, out int gx, out int gy)
        {
            map ??= _map;
            if (map == null) { ScreenToTile(wx, wy, out gx, out gy); return; }


            for (int h = AppConfig.Instance.MaxStackHeight; h >= 0; h--)
            {
                double curWy = wy + h * StackStep;
                double ax = wx - TileW / 2.0;
                int tx = (int)Math.Floor((ax / (TileW / 2.0) + curWy / (TileH / 2.0)) / 2.0);
                int ty = (int)Math.Floor((curWy / (TileH / 2.0) - ax / (TileW / 2.0)) / 2.0);

                if (map.IsInBounds(tx, ty) && map[tx, ty].Blocks.ContainsKey(h - 1))
                {
                    gx = tx; gy = ty;
                    return;
                }
            }
            ScreenToTile(wx, wy, out gx, out gy);
        }

        public PointCollection GetDiamondPoints(int gx, int gy)
        {
            TileToScreen(gx, gy, out double sx, out double sy);
            int maxHeight = -1;
            if (_map != null) foreach (var h in _map[gx, gy].Blocks.Keys) if (h > maxHeight) maxHeight = h;
            double heightPx = (_showHeights && maxHeight >= 0) ? (maxHeight + 1) * StackStep : 0;
            double t = sy - heightPx;
            return new PointCollection
            {
                new(sx + TileW / 2.0, t),
                new(sx + TileW,       t + TileH / 2.0),
                new(sx + TileW / 2.0, t + TileH),
                new(sx,               t + TileH / 2.0)
            };
        }

        public Point GetTileCenter(int gx, int gy)
        {
            TileToScreen(gx, gy, out double sx, out double sy);
            int maxHeight = -1;
            if (_map != null) foreach (var h in _map[gx, gy].Blocks.Keys) if (h > maxHeight) maxHeight = h;
            double heightPx = (_showHeights && maxHeight >= 0) ? (maxHeight + 1) * StackStep : 0;
            return new Point(sx + TileW / 2.0, sy - heightPx + TileH / 2.0);
        }
    }
}
