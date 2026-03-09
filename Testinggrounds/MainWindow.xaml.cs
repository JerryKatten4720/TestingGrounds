using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IsometricWPF.Dialogs;
using IsometricWPF.Dwellers;
using Microsoft.Win32;

namespace IsometricWPF
{
    public partial class MainWindow : Window
    {
        // ── Constants ─────────────────────────────────────────────────
        private const double MINIMAP_W = 158;
        private const double MINIMAP_H = 108;
        private const double ZOOM_MIN  = 0.05;
        private const double ZOOM_MAX  = 6.0;

        // ── Core objects ──────────────────────────────────────────────
        private WorldMap          _worldMap;
        private IsometricRenderer _renderer;
        private DwellerLayer      _dwellerLayer;

        // Bug #8 fix: fast lookup instead of a fragile index formula
        private readonly Dictionary<(int x, int y), Rectangle> _minimapRects = new();

        // ── Camera ────────────────────────────────────────────────────
        private bool   _isPanning;
        private Point  _lastMousePos;
        private double _zoom = 1.0;

        // ── Editor state ──────────────────────────────────────────────
        private int    _selectedX = -1, _selectedY = -1;
        private string _activeTile = "Grass";
        private bool   _dwellerPlacementMode;

        public WorldMap World => _worldMap;

        // ── Constructor ───────────────────────────────────────────────

        public MainWindow()
        {
            InitializeComponent();
            AppConfig.Load();

            DwellerRegistry.Initialize();
            AssetRegistry.Initialize();
            PopulateDwellerPicker();

            _renderer = new IsometricRenderer();
            _renderer.TileHovered   += OnTileHovered;
            _renderer.TileHoverLeft += OnTileHoverLeft;
            TileHostContainer.Children.Add(_renderer.Host);

            // Bug #3 fix: pass a delegate so DwellerLayer never touches MainWindow directly
            _dwellerLayer = new DwellerLayer(_renderer, () => _worldMap);
            _dwellerLayer.DwellerSelected += OnDwellerSelected;

            _zoom = AppConfig.Instance.DefaultZoom;
            CameraScale.ScaleX = CameraScale.ScaleY = _zoom;
            BtnEditorMode.IsChecked = AppConfig.Instance.EditorEnabled;
            BtnGrid.IsChecked       = AppConfig.Instance.ShowGrid;
            BtnHeights.IsChecked    = AppConfig.Instance.ShowHeights;

            BuildPalette();
            LoadWorld(WorldMap.GenerateIsland(AppConfig.Instance.DefaultMapCols, AppConfig.Instance.DefaultMapRows));
            ApplyEditorMode();

            AssetListBox.ItemsSource = AssetRegistry.TextureNames.ToList();

            this.Focus();
            this.SizeChanged += (_, _) => UpdateRendererViewport();
        }

        // ── Viewport sync ─────────────────────────────────────────────

        private void UpdateRendererViewport()
        {
            if (_renderer == null || ViewportGrid == null) return;
            double w = ViewportGrid.ActualWidth  / _zoom;
            double h = ViewportGrid.ActualHeight / _zoom;
            _renderer.SetViewport(-CameraPan.X / _zoom, -CameraPan.Y / _zoom, w, h);
        }

        // ── Editor mode ───────────────────────────────────────────────

        private void ApplyEditorMode()
        {
            bool on = AppConfig.Instance.EditorEnabled;
            LeftSidePanel.Visibility  = on ? Visibility.Visible : Visibility.Collapsed;
            RightSidePanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            EditorModeLabel.Visibility = Visibility.Visible;
            EditorModeLabel.Text       = on ? "  ✏ EDITOR" : "  👁 VIEW";
            EditorModeLabel.Foreground = on
                ? new SolidColorBrush(Color.FromRgb(136, 255, 136))
                : new SolidColorBrush(Color.FromRgb(136, 170, 255));
            if (!on) { SelectionCursor.Visibility = HoverCursor.Visibility = Visibility.Hidden; }
        }

        // ── World loading ─────────────────────────────────────────────

        private void LoadWorld(WorldMap world)
        {
            _worldMap  = world;
            _selectedX = _selectedY = -1;
            SelectionCursor.Visibility = Visibility.Hidden;
            _renderer.LoadMap(world);
            _dwellerLayer.ClearAll();
            RenderMiniMap();
            UpdateMiniMapViewport();
            UpdateCameraLabel();
            ClearInspector();
        }

        // ── Dwellers ──────────────────────────────────────────────────

        private void PopulateDwellerPicker()
        {
            DwellerPicker.Items.Clear();
            foreach (var d in DwellerRegistry.All) DwellerPicker.Items.Add(d.DisplayName);
            if (DwellerPicker.Items.Count > 0) DwellerPicker.SelectedIndex = 0;
        }

        private void SpawnDweller_Click(object sender, RoutedEventArgs e)
        {
            if (DwellerPicker.SelectedIndex < 0) return;
            _dwellerPlacementMode = true;
            CameraLabel.Text = "📍 Click a tile to place dweller  [Esc = cancel]";
        }

        private void OnDwellerSelected(DwellerInstance? dweller) => UpdateDwellerInspector(dweller);

        private void UpdateDwellerInspector(DwellerInstance? d)
        {
            if (d == null) { DwellerInspectorPanel.Visibility = Visibility.Collapsed; return; }
            DwellerInspectorPanel.Visibility = Visibility.Visible;
            DwellerName.Text    = d.Data.DisplayName;
            DwellerRarity.Text  = $"Rarity: {d.Data.RarityEnum}";
            DwellerSpecial.Text = $"S:{d.Data.S} P:{d.Data.P} E:{d.Data.E}\nC:{d.Data.C} I:{d.Data.I} A:{d.Data.A} L:{d.Data.L}\nAP: {d.ActionPoints}/{d.MaxActionPoints}";
        }

        private void DwellerRemove_Click(object sender, RoutedEventArgs e)
        {
            var sel = _dwellerLayer.Selected;
            if (sel == null) return;
            _dwellerLayer.Remove(sel);
            DwellerInspectorPanel.Visibility = Visibility.Collapsed;
        }

        // ── Asset panel ───────────────────────────────────────────────

        private void Asset_Add_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dlg.ShowDialog() == true)
                if (AssetRegistry.AddTexture(dlg.FileName))
                    AssetListBox.ItemsSource = AssetRegistry.TextureNames.ToList();
        }

        private void Asset_Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string name })
            {
                AssetRegistry.RemoveTexture(name);
                AssetListBox.ItemsSource = AssetRegistry.TextureNames.ToList();
            }
        }

        // ── Palette ───────────────────────────────────────────────────

        private void BuildPalette()
        {
            PalettePanel.Children.Clear();
            foreach (var kv in TileRegistry.All)
                PalettePanel.Children.Add(MakePaletteButton(kv.Key, kv.Value));
            SelectPaletteButton(_activeTile);
        }

        private Border MakePaletteButton(string name, TileDefinition def)
        {
            var swatch = new Rectangle { Width = 16, Height = 16, Fill = def.TopBrush, Margin = new Thickness(0, 0, 8, 0) };
            var label  = new TextBlock  { Text = name, Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var row    = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(swatch);
            row.Children.Add(label);

            if (def.IsCustom)
            {
                var del = new TextBlock
                {
                    Text = " ✕", Foreground = new SolidColorBrush(Color.FromRgb(255, 80, 80)),
                    FontFamily = new FontFamily("Consolas"), FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 0, 0)
                };
                del.MouseLeftButtonDown += (s, e) => { e.Handled = true; RemoveCustomTile(name); };
                row.Children.Add(del);
            }

            var border = new Border
            {
                Style = (Style)Application.Current.Resources["PaletteBtn"],
                BorderBrush = Brushes.Transparent, Child = row, Tag = name
            };
            border.MouseLeftButtonDown += (_, _) =>
            {
                _activeTile          = name;
                _dwellerPlacementMode = false;
                SelectPaletteButton(name);
            };
            return border;
        }

        private void SelectPaletteButton(string name)
        {
            foreach (Border b in PalettePanel.Children)
            {
                bool active = b.Tag?.ToString() == name;
                b.BorderBrush = active ? new SolidColorBrush(Color.FromRgb(200, 168, 75)) : Brushes.Transparent;
                b.Background  = active ? new SolidColorBrush(Color.FromArgb(60, 200, 168, 75)) : Brushes.Transparent;
            }
        }

        private void RemoveCustomTile(string name)
        {
            TileRegistry.Remove(name);
            if (_activeTile == name) _activeTile = "Grass";
            BuildPalette();
        }

        // ── Minimap ───────────────────────────────────────────────────

        private void RenderMiniMap()
        {
            MiniMap.Children.Clear();
            _minimapRects.Clear();
            if (_worldMap == null) return;

            double tw = MINIMAP_W / _worldMap.Columns;
            double th = MINIMAP_H / _worldMap.Rows;

            for (int x = 0; x < _worldMap.Columns; x++)
            for (int y = 0; y < _worldMap.Rows; y++)
            {
                var brush = TopBrushForCell(_worldMap[x, y]);
                var rect  = new Rectangle { Width = tw + 0.6, Height = th + 0.6, Fill = brush };
                Canvas.SetLeft(rect, x * tw);
                Canvas.SetTop(rect,  y * th);
                MiniMap.Children.Add(rect);

                // Bug #8 fix: store reference in dictionary for O(1) partial updates
                _minimapRects[(x, y)] = rect;
            }
        }

        private void UpdateMiniMapViewport()
        {
            if (_worldMap == null) return;

            double mapPxW = (_worldMap.Columns + _worldMap.Rows) * (AppConfig.Instance.TileWidth  / 2.0);
            double mapPxH = (_worldMap.Columns + _worldMap.Rows) * (AppConfig.Instance.TileHeight / 2.0);
            double visW   = ViewportGrid.ActualWidth  / _zoom;
            double visH   = ViewportGrid.ActualHeight / _zoom;
            double vpX    = -CameraPan.X / _zoom;
            double vpY    = -CameraPan.Y / _zoom;

            Canvas.SetLeft(MiniMapViewport, Math.Max(0, (vpX / mapPxW + 0.5) * MINIMAP_W));
            Canvas.SetTop( MiniMapViewport, Math.Max(0, (vpY / mapPxH + 0.5) * MINIMAP_H));
            MiniMapViewport.Width  = Math.Clamp(visW / mapPxW * MINIMAP_W, 4, MINIMAP_W);
            MiniMapViewport.Height = Math.Clamp(visH / mapPxH * MINIMAP_H, 4, MINIMAP_H);

            UpdateRendererViewport();
        }

        private void UpdateMiniMapPartial(int x, int y)
        {
            if (_minimapRects.TryGetValue((x, y), out var rect))
                rect.Fill = TopBrushForCell(_worldMap[x, y]);
        }

        private static Brush TopBrushForCell(TileCell cell)
        {
            string? top = cell.TopBlockName;
            return top != null ? TileRegistry.Get(top).TopBrush : TileRegistry.Get("Grass").TopBrush;
        }

        // ── Hover cursors ─────────────────────────────────────────────

        private void OnTileHovered(int gx, int gy)
        {
            if (!AppConfig.Instance.EditorEnabled) return;
            HoverCursor.Points     = _renderer.GetDiamondPoints(gx, gy);
            HoverCursor.Visibility = Visibility.Visible;

            bool hasSel  = _dwellerLayer.Selected != null;
            bool isValid = hasSel && _dwellerLayer.IsValidMove(gx, gy);
            HoverCursor.Stroke = hasSel ? (isValid ? Brushes.LightGreen : Brushes.Red) : Brushes.White;
            HoverCursor.Fill   = hasSel
                ? (isValid
                    ? new SolidColorBrush(Color.FromArgb(40, 0, 255, 0))
                    : new SolidColorBrush(Color.FromArgb(40, 255, 0, 0)))
                : new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
        }

        private void OnTileHoverLeft(int gx, int gy) => HoverCursor.Visibility = Visibility.Hidden;

        private void SnapSelectionCursor(int gx, int gy)
        {
            if (gx < 0 || _worldMap == null || !AppConfig.Instance.EditorEnabled)
            {
                SelectionCursor.Visibility = Visibility.Hidden;
                return;
            }
            SelectionCursor.Points     = _renderer.GetDiamondPoints(gx, gy);
            SelectionCursor.Visibility = Visibility.Visible;
        }

        // ── Coordinate helpers ────────────────────────────────────────

        private Point ViewportToWorld(Point screen)
        {
            var inverse = TransformCanvas.TransformToAncestor((Visual)TransformCanvas.Parent).Inverse;
            return inverse?.Transform(screen) ?? screen;
        }

        // ── Mouse events ──────────────────────────────────────────────

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var world = ViewportToWorld(e.GetPosition(ViewportGrid));
            _renderer.ScreenToTile(_worldMap, world.X, world.Y, out int gx, out int gy);
            if (!_worldMap.IsInBounds(gx, gy)) return;

            // Shift+Click: place decor of the active tile type
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                // Bug #4 fix: use _activeTile instead of hardcoded "Grass"
                _worldMap[gx, gy].AddDecor(_activeTile);
                _renderer.Redraw();
                return;
            }

            // Dweller placement mode
            if (_dwellerPlacementMode)
            {
                var data = DwellerRegistry.GetByIndex(DwellerPicker.SelectedIndex);
                if (data != null) _dwellerLayer.Add(new DwellerInstance(data, gx, gy));
                _dwellerPlacementMode = false;
                UpdateCameraLabel();
                return;
            }

            // Try dweller click / move
            bool consumed = _dwellerLayer.HandleTileClick(gx, gy);
            if (consumed)
            {
                UpdateDwellerInspector(_dwellerLayer.Selected);
                return;
            }

            if (!AppConfig.Instance.EditorEnabled) return;

            // Place block
            _worldMap[gx, gy].AddBlock(_activeTile);
            _selectedX = gx; _selectedY = gy;
            _renderer.Redraw();
            SnapSelectionCursor(gx, gy);
            UpdateInspector(gx, gy);
            UpdateMiniMapPartial(gx, gy);
        }

        private void Viewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isPanning    = true;
            _lastMousePos = e.GetPosition(this);
            CaptureMouse();
            Cursor = Cursors.SizeAll;

            // Shift+RightClick: remove decor/block
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                var world = ViewportToWorld(e.GetPosition(ViewportGrid));
                _renderer.ScreenToTile(_worldMap, world.X, world.Y, out int gx, out int gy);
                if (_worldMap.IsInBounds(gx, gy))
                {
                    var cell = _worldMap[gx, gy];
                    if (cell.Decors.Count > 0) cell.ClearDecors();
                    else                       cell.RemoveBlock();
                    _renderer.Redraw();
                    UpdateMiniMapPartial(gx, gy);
                }
            }
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                var cur   = e.GetPosition(this);
                var delta = cur - _lastMousePos;

                double nx = CameraPan.X + delta.X;
                double ny = CameraPan.Y + delta.Y;

                if (AppConfig.Instance.LimitCamera && _worldMap != null)
                {
                    double mw = (_worldMap.Columns + _worldMap.Rows) * (AppConfig.Instance.TileWidth  / 2.0);
                    double mh = (_worldMap.Columns + _worldMap.Rows) * (AppConfig.Instance.TileHeight / 2.0);
                    double mg = AppConfig.Instance.CameraMargin;
                    nx = Math.Clamp(nx, -mw / 2 - mg, mw / 2 + mg);
                    ny = Math.Clamp(ny, -mg, mh + mg);
                }

                CameraPan.X    = nx;
                CameraPan.Y    = ny;
                _lastMousePos  = cur;
                UpdateCameraLabel();
                UpdateMiniMapViewport();
            }
            else
            {
                _renderer.OnMouseMove(ViewportToWorld(e.GetPosition(ViewportGrid)));
            }
        }

        private void Viewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isPanning = false;
            Cursor     = Cursors.Arrow;
            ReleaseMouseCapture();
        }

        private void Viewport_MouseLeave(object sender, MouseEventArgs e)
        {
            _renderer.OnMouseLeave();
            HoverCursor.Visibility = Visibility.Hidden;
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor   = e.Delta > 0 ? 1.12 : 0.893;
            double oldZoom  = _zoom;
            _zoom           = Math.Clamp(_zoom * factor, ZOOM_MIN, ZOOM_MAX);
            double ratio    = _zoom / oldZoom;
            var    mouse    = e.GetPosition(ViewportGrid);

            CameraPan.X = mouse.X + (CameraPan.X - mouse.X) * ratio;
            CameraPan.Y = mouse.Y + (CameraPan.Y - mouse.Y) * ratio;
            CameraScale.ScaleX = CameraScale.ScaleY = _zoom;

            AppConfig.Instance.DefaultZoom = _zoom;
            // Bug #9 fix: debounce so rapid scrolling doesn't hammer the disk
            AppConfig.SaveDebounced();

            UpdateCameraLabel();
            UpdateMiniMapViewport();
        }

        // ── Keyboard ──────────────────────────────────────────────────

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.R: Camera_Reset(null!, null!); break;
                case Key.G: BtnGrid.IsChecked    = !BtnGrid.IsChecked;    Toggle_Grid(null!, null!);    break;
                case Key.H: BtnHeights.IsChecked = !BtnHeights.IsChecked; Toggle_Heights(null!, null!); break;
                case Key.E: BtnEditorMode.IsChecked = !BtnEditorMode.IsChecked; Toggle_EditorMode(null!, null!); break;
                case Key.Escape:
                    _dwellerPlacementMode = false;
                    _dwellerLayer.Deselect();
                    UpdateDwellerInspector(null);
                    UpdateCameraLabel();
                    break;
                case Key.OemPlus:  case Key.Add:      HeightBrush_Inc(null!, null!); break;
                case Key.OemMinus: case Key.Subtract: HeightBrush_Dec(null!, null!); break;
            }
        }

        // ── Toolbar handlers ──────────────────────────────────────────

        private void File_New(object sender, RoutedEventArgs e)
        {
            var dlg = new NewMapDialog { Owner = this };
            if (dlg.ShowDialog() == true) LoadWorld(new WorldMap(dlg.MapCols, dlg.MapRows));
        }

        private void File_Import(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Import map", Filter = "World files|*.world.json|All|*.*" };
            if (dlg.ShowDialog() != true) return;
            var (map, dwellers, ok, error) = MapSerializer.Import(dlg.FileName);
            if (ok)
            {
                LoadWorld(map);
                foreach (var d in dwellers) _dwellerLayer.Add(d);
                BuildPalette();
            }
            else MessageBox.Show($"Import failed:\n{error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void File_Export(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "Export map", Filter = "World files|*.world.json", DefaultExt = "world.json" };
            if (dlg.ShowDialog() == true) MapSerializer.Export(_worldMap, _dwellerLayer.Dwellers, dlg.FileName);
        }

        private void Preset_Island(object sender, RoutedEventArgs e)    => LoadWorld(WorldMap.GenerateIsland(_worldMap?.Columns ?? 40, _worldMap?.Rows ?? 40));
        private void Preset_Wasteland(object sender, RoutedEventArgs e) => LoadWorld(WorldMap.GenerateWasteland(_worldMap?.Columns ?? 40, _worldMap?.Rows ?? 40));
        private void Preset_Clear(object sender, RoutedEventArgs e)     => LoadWorld(new WorldMap(_worldMap?.Columns ?? 40, _worldMap?.Rows ?? 40));

        private void Toggle_Grid(object sender, RoutedEventArgs e)
        {
            _renderer.ShowGrid = BtnGrid.IsChecked == true;
            AppConfig.Instance.ShowGrid = _renderer.ShowGrid;
            AppConfig.Save();
        }

        private void Toggle_Heights(object sender, RoutedEventArgs e)
        {
            _renderer.ShowHeights = BtnHeights.IsChecked == true;
            AppConfig.Instance.ShowHeights = _renderer.ShowHeights;
            AppConfig.Save();
            _dwellerLayer.RefreshPositions();
            if (_selectedX >= 0) SnapSelectionCursor(_selectedX, _selectedY);
        }

        private void Toggle_EditorMode(object sender, RoutedEventArgs e)
        {
            AppConfig.Instance.EditorEnabled = BtnEditorMode.IsChecked == true;
            AppConfig.Save();
            ApplyEditorMode();
        }

        private void Camera_Reset(object sender, RoutedEventArgs e)
        {
            _zoom = 1.0;
            CameraScale.ScaleX = CameraScale.ScaleY = 1.0;
            CameraPan.X = 450; CameraPan.Y = 120;
            UpdateCameraLabel();
            UpdateMiniMapViewport();
        }

        private void HeightBrush_Inc(object sender, RoutedEventArgs e)
        {
            int h = int.TryParse(HeightBrushLabel.Text, out int v) ? v : 0;
            HeightBrushLabel.Text = Math.Min(h + 1, AppConfig.Instance.MaxStackHeight - 1).ToString();
        }

        private void HeightBrush_Dec(object sender, RoutedEventArgs e)
        {
            int h = int.TryParse(HeightBrushLabel.Text, out int v) ? v : 0;
            HeightBrushLabel.Text = Math.Max(h - 1, 0).ToString();
        }

        private void Palette_AddCustomTile(object sender, RoutedEventArgs e)
        {
            var dlg = new AddTileDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;
            TileRegistry.Register(dlg.TileName, dlg.TopColor, dlg.LeftColor, dlg.RightColor, isCustom: true);
            BuildPalette();
            _activeTile = dlg.TileName;
            SelectPaletteButton(dlg.TileName);
        }

        // ── Inspector ─────────────────────────────────────────────────

        private void UpdateInspector(int gx, int gy)
        {
            var cell = _worldMap[gx, gy];
            InspectorCoords.Text = $"Tile ({gx}, {gy})";
            InspectorType.Text   = cell.TopBlockName ?? "Empty";
            InspectorHeight.Text = $" {cell.Blocks.Count} ";

            string? top = cell.TopBlockName;
            if (top != null)
            {
                var def = TileRegistry.Get(top);
                TextureTopLabel.Text   = def.TopTexturePath   ?? "(default)";
                TextureLeftLabel.Text  = def.LeftTexturePath  ?? "(default)";
                TextureRightLabel.Text = def.RightTexturePath ?? "(default)";
            }
            else
            {
                TextureTopLabel.Text = TextureLeftLabel.Text = TextureRightLabel.Text = "(default)";
            }
        }

        private void ClearInspector()
        {
            InspectorCoords.Text = "No tile selected";
            InspectorType.Text   = InspectorHeight.Text = "";
            TextureTopLabel.Text = TextureLeftLabel.Text = TextureRightLabel.Text = "(default)";
        }

        private void Inspector_HeightInc(object sender, RoutedEventArgs e)
        {
            if (_selectedX < 0) return;
            _worldMap[_selectedX, _selectedY].AddBlock(_activeTile);
            RefreshSelected();
        }

        private void Inspector_HeightDec(object sender, RoutedEventArgs e)
        {
            if (_selectedX < 0) return;
            _worldMap[_selectedX, _selectedY].RemoveBlock();
            RefreshSelected();
        }

        private void RefreshSelected()
        {
            _renderer.Redraw();
            _dwellerLayer.RefreshPositions();
            SnapSelectionCursor(_selectedX, _selectedY);
            UpdateInspector(_selectedX, _selectedY);
        }

        // ── Texture scope ─────────────────────────────────────────────

        private void TextureScope_Changed(object sender, RoutedEventArgs e)
        {
            bool isCell = sender == BtnScopeCell;
            BtnScopeType.IsChecked = !isCell;
            BtnScopeCell.IsChecked = isCell;
            TextureScopeHint.Text  = isCell ? "Applies to this tile only" : "Applies to all tiles of this type";
            if (_selectedX >= 0) UpdateInspector(_selectedX, _selectedY);
        }

        private void Texture_TopBrowse(object sender, RoutedEventArgs e)
            => BrowseTexture(path =>
            {
                string? top = _worldMap[_selectedX, _selectedY].TopBlockName;
                if (top == null) return;
                TileRegistry.Get(top).SetTopTexture(path);
                _renderer.InvalidateBrushCache();
                _renderer.Redraw();
                UpdateInspector(_selectedX, _selectedY);
            });

        private void Texture_LeftBrowse(object sender, RoutedEventArgs e)
            => BrowseTexture(path =>
            {
                string? top = _worldMap[_selectedX, _selectedY].TopBlockName;
                if (top == null) return;
                TileRegistry.Get(top).SetLeftTexture(path);
                _renderer.InvalidateBrushCache();
                _renderer.Redraw();
                UpdateInspector(_selectedX, _selectedY);
            });

        private void Texture_RightBrowse(object sender, RoutedEventArgs e)
            => BrowseTexture(path =>
            {
                string? top = _worldMap[_selectedX, _selectedY].TopBlockName;
                if (top == null) return;
                TileRegistry.Get(top).SetRightTexture(path);
                _renderer.InvalidateBrushCache();
                _renderer.Redraw();
                UpdateInspector(_selectedX, _selectedY);
            });

        private void Texture_TopClear(object sender, RoutedEventArgs e)
        {
            string? top = _worldMap[_selectedX, _selectedY].TopBlockName;
            if (top == null) return;
            TileRegistry.Get(top).SetTopTexture(null);
            _renderer.InvalidateBrushCache();
            RefreshSelected();
        }

        private void Texture_LeftClear(object sender, RoutedEventArgs e)
        {
            string? top = _worldMap[_selectedX, _selectedY].TopBlockName;
            if (top == null) return;
            TileRegistry.Get(top).SetLeftTexture(null);
            _renderer.InvalidateBrushCache();
            RefreshSelected();
        }

        private void Texture_RightClear(object sender, RoutedEventArgs e)
        {
            string? top = _worldMap[_selectedX, _selectedY].TopBlockName;
            if (top == null) return;
            TileRegistry.Get(top).SetRightTexture(null);
            _renderer.InvalidateBrushCache();
            RefreshSelected();
        }

        private void Texture_ClearAllOverrides(object sender, RoutedEventArgs e)
        {
            if (_selectedX < 0) return;
            _worldMap[_selectedX, _selectedY].IsWalkableOverride = null;
            RefreshSelected();
        }

        private void BrowseTexture(Action<string> onPicked)
        {
            if (_selectedX < 0) return;
            var dlg = new OpenFileDialog { Title = "Select texture", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dlg.ShowDialog() == true)
            {
                AssetRegistry.AddTexture(dlg.FileName);
                AssetListBox.ItemsSource = AssetRegistry.TextureNames.ToList();
                onPicked(dlg.FileName);
            }
        }

        // ── HUD ───────────────────────────────────────────────────────

        private void UpdateCameraLabel()
        {
            CameraLabel.Text = _dwellerPlacementMode
                ? "📍 Click a tile to place dweller  [Esc = cancel]"
                : $"zoom {_zoom:F2}x   pan ({CameraPan.X:F0}, {CameraPan.Y:F0})   {_worldMap?.Columns}×{_worldMap?.Rows}";
        }
    }
}
