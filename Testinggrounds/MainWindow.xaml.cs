using System;
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
        private const double MINIMAP_WIDTH  = 158;
        private const double MINIMAP_HEIGHT = 108;

        private WorldMap          _worldMap;
        private IsometricRenderer _worldRenderer;
        private DwellerLayer      _dwellerController;

        public WorldMap World => _worldMap;

        private bool   _isCameraPanning;
        private Point  _lastMousePosition;
        private double _cameraZoom = 1.0;
        
        private const double ZOOM_MINIMUM = 0.05;
        private const double ZOOM_MAXIMUM = 6.0;

        private int    _selectedGridX = -1, _selectedGridY = -1;
        private int    _activeHeightBrush = 0;
        private string _activeTileName    = "Grass";
        private bool   _isTextureScopeCell = false;

        private bool _isDwellerPlacementMode = false;

        public MainWindow()
        {
            InitializeComponent();
            AppConfig.Load();

            DwellerRegistry.Initialize();
            AssetRegistry.Initialize();
            PopulateDwellerPicker();

            _worldRenderer = new IsometricRenderer();
            _worldRenderer.TileHovered   += OnTileHovered;
            _worldRenderer.TileHoverLeft += OnTileHoverLeft;
            TileHostContainer.Children.Add(_worldRenderer.Host);

            _dwellerController = new DwellerLayer(_worldRenderer);
            _dwellerController.DwellerSelected += OnDwellerSelected;

            _cameraZoom = AppConfig.Instance.DefaultZoom;
            CameraScale.ScaleX = CameraScale.ScaleY = _cameraZoom;
            BtnEditorMode.IsChecked = AppConfig.Instance.EditorEnabled;

            BuildPalette();
            
            // Start with a default island
            LoadWorld(WorldMap.GenerateIsland(AppConfig.Instance.DefaultMapCols, AppConfig.Instance.DefaultMapRows));
            
            ApplyEditorMode();
            AssetListBox.ItemsSource = AssetRegistry.TextureNames.ToList();
            
            this.Focus();
            this.SizeChanged += (sender, args) => UpdateRendererViewport();
        }

        private void UpdateRendererViewport()
        {
            if (_worldRenderer == null || ViewportGrid == null) return;
            
            double visibleWidth  = ViewportGrid.ActualWidth / _cameraZoom;
            double visibleHeight = ViewportGrid.ActualHeight / _cameraZoom;
            
            _worldRenderer.SetViewport(-CameraPan.X / _cameraZoom, -CameraPan.Y / _cameraZoom, visibleWidth, visibleHeight);
        }

        // ══ Editor mode ════════════════════════════════════════════════

        private void ApplyEditorMode()
        {
            bool on = AppConfig.Instance.EditorEnabled;
            LeftSidePanel.Visibility   = on ? Visibility.Visible : Visibility.Collapsed;
            RightSidePanel.Visibility  = on ? Visibility.Visible : Visibility.Collapsed;
            EditorModeLabel.Visibility = Visibility.Visible;
            EditorModeLabel.Text       = on ? "  ✏ EDITOR" : "  👁 VIEW";
            EditorModeLabel.Foreground = on
                ? new SolidColorBrush(Color.FromRgb(136, 255, 136))
                : new SolidColorBrush(Color.FromRgb(136, 170, 255));
            if (!on) { SelectionCursor.Visibility = Visibility.Hidden; HoverCursor.Visibility = Visibility.Hidden; }
        }

        // ══ World loading ══════════════════════════════════════════════

        private void LoadWorld(WorldMap world)
        {
            _worldMap      = world;
            _selectedGridX = _selectedGridY = -1;
            SelectionCursor.Visibility = Visibility.Hidden;
            _worldRenderer.LoadMap(world);
            _dwellerController.ClearAll();
            RenderMiniMap();
            UpdateMiniMapViewport();
            UpdateCameraLabel();
            ClearInspector();
        }

        // ══ Dwellers ══════════════════════════════════════════════════

        private void PopulateDwellerPicker()
        {
            DwellerPicker.Items.Clear();
            foreach (var dweller in DwellerRegistry.All)
                DwellerPicker.Items.Add(dweller.DisplayName);
            if (DwellerPicker.Items.Count > 0) DwellerPicker.SelectedIndex = 0;
        }

        private void SpawnDweller_Click(object sender, RoutedEventArgs e)
        {
            if (DwellerPicker.SelectedIndex < 0) return;
            _isDwellerPlacementMode = true;
            MoveCursor.Visibility = Visibility.Hidden;
            CameraLabel.Text = "Click a tile to place the dweller...";
        }

        private void OnDwellerSelected(DwellerInstance dweller)
        {
            UpdateDwellerInspector(dweller);
        }

        private void UpdateDwellerInspector(DwellerInstance dweller)
        {
            if (dweller == null) { DwellerInspectorPanel.Visibility = Visibility.Collapsed; return; }
            DwellerInspectorPanel.Visibility = Visibility.Visible;
            DwellerName.Text    = dweller.Data.DisplayName;
            DwellerRarity.Text  = $"Rarity: {dweller.Data.Rarity}";
            DwellerSpecial.Text = $"S:{dweller.Data.S} P:{dweller.Data.P} E:{dweller.Data.E}\nC:{dweller.Data.C} I:{dweller.Data.I} A:{dweller.Data.A} L:{dweller.Data.L}";
        }

        private void DwellerRemove_Click(object sender, RoutedEventArgs e)
        {
            var selectedDweller = _dwellerController.Selected;
            if (selectedDweller == null) return;
            _dwellerController.Remove(selectedDweller);
            DwellerInspectorPanel.Visibility = Visibility.Collapsed;
        }

        private void Asset_Add_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp" };
            if (ofd.ShowDialog() == true)
            {
                if (AssetRegistry.AddTexture(ofd.FileName))
                    AssetListBox.ItemsSource = AssetRegistry.TextureNames.ToList();
            }
        }

        private void Asset_Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string name)
            {
                AssetRegistry.RemoveTexture(name);
                AssetListBox.ItemsSource = AssetRegistry.TextureNames.ToList();
            }
        }

        // ══ Palette ════════════════════════════════════════════════════

        private void BuildPalette()
        {
            PalettePanel.Children.Clear();
            foreach (var kv in TileRegistry.All)
                PalettePanel.Children.Add(MakePaletteButton(kv.Key, kv.Value));
            SelectPaletteButton(_activeTileName);
        }

        private Border MakePaletteButton(string tileName, TileDefinition def)
        {
            var swatch = new Rectangle { Width = 16, Height = 16, Fill = def.TopBrush, Margin = new Thickness(0, 0, 8, 0) };
            var label  = new TextBlock  { Text = tileName, Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var row    = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(swatch);
            row.Children.Add(label);

            if (def.IsCustom)
            {
                var del = new TextBlock { Text = " ✕", Foreground = new SolidColorBrush(Color.FromRgb(255, 80, 80)),
                    FontFamily = new FontFamily("Consolas"), FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 0, 0) };
                del.MouseLeftButtonDown += (s, e) => { e.Handled = true; RemoveCustomTile(tileName); };
                row.Children.Add(del);
            }

            var border = new Border { Style = (Style)Application.Current.Resources["PaletteBtn"], BorderBrush = Brushes.Transparent, Child = row, Tag = tileName };
            border.MouseLeftButtonDown += (s, e) => { _activeTileName = tileName; _isDwellerPlacementMode = false; SelectPaletteButton(tileName); };
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
            if (_activeTileName == name) _activeTileName = "Grass";
            BuildPalette();
        }

        // ══ Mini-map ═══════════════════════════════════════════════════

        private void RenderMiniMap()
        {
            MiniMap.Children.Clear();
            if (_worldMap == null) return;
            
            double tileWidth  = MINIMAP_WIDTH  / _worldMap.Columns;
            double tileHeight = MINIMAP_HEIGHT / _worldMap.Rows;
            
            for (int x = 0; x < _worldMap.Columns; x++)
            {
                for (int y = 0; y < _worldMap.Rows; y++)
                {
                    var cell = _worldMap[x, y];
                    int maxHeight = -1;
                    foreach (var h in cell.Blocks.Keys) if (h > maxHeight) maxHeight = h;
                    
                    string topBlockName = maxHeight >= 0 ? cell.Blocks[maxHeight] : "Grass";
                    var brush = TileRegistry.Get(topBlockName).TopBrush;
                    
                    var rect = new Rectangle 
                    { 
                        Width = tileWidth + 0.6, 
                        Height = tileHeight + 0.6, 
                        Fill = brush 
                    };
                    Canvas.SetLeft(rect, x * tileWidth); 
                    Canvas.SetTop(rect, y * tileHeight);
                    MiniMap.Children.Add(rect);
                }
            }
        }

        private void UpdateMiniMapViewport()
        {
            if (_worldMap == null) return;
            
            double mapPixelWidth  = (_worldMap.Columns + _worldMap.Rows) * (AppConfig.Instance.TileWidth / 2.0);
            double mapPixelHeight = (_worldMap.Columns + _worldMap.Rows) * (AppConfig.Instance.TileHeight / 2.0);
            
            double visibleWidth  = ViewportGrid.ActualWidth  / _cameraZoom;
            double visibleHeight = ViewportGrid.ActualHeight / _cameraZoom;
            
            double viewportX = -CameraPan.X / _cameraZoom;
            double viewportY = -CameraPan.Y / _cameraZoom;
            
            Canvas.SetLeft(MiniMapViewport, Math.Max(0, (viewportX / mapPixelWidth + 0.5) * MINIMAP_WIDTH));
            Canvas.SetTop( MiniMapViewport, Math.Max(0, (viewportY / mapPixelHeight + 0.5) * MINIMAP_HEIGHT));
            
            MiniMapViewport.Width  = Math.Clamp(visibleWidth / mapPixelWidth * MINIMAP_WIDTH, 4, MINIMAP_WIDTH);
            MiniMapViewport.Height = Math.Clamp(visibleHeight / mapPixelHeight * MINIMAP_HEIGHT, 4, MINIMAP_HEIGHT);

            UpdateRendererViewport();
        }

        // ══ Hover ══════════════════════════════════════════════════════

        private void OnTileHovered(int gridX, int gridY)
        {
            if (!AppConfig.Instance.EditorEnabled) return;
            HoverCursor.Points     = _worldRenderer.GetDiamondPoints(gridX, gridY);
            HoverCursor.Visibility = Visibility.Visible;

            // Change cursor color if invalid move when a dweller is selected
            if (_dwellerController.Selected != null)
            {
                bool isValid = _dwellerController.IsValidMove(gridX, gridY);
                HoverCursor.Stroke = isValid ? Brushes.LightGreen : Brushes.Red;
                HoverCursor.Fill   = isValid ? new SolidColorBrush(Color.FromArgb(40, 0, 255, 0)) : new SolidColorBrush(Color.FromArgb(40, 255, 0, 0));
            }
            else
            {
                HoverCursor.Stroke = Brushes.White;
                HoverCursor.Fill   = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
            }
        }

        private void OnTileHoverLeft(int gridX, int gridY) => HoverCursor.Visibility = Visibility.Hidden;

        private void SnapSelectionCursor(int gridX, int gridY)
        {
            if (gridX < 0 || _worldMap == null || !AppConfig.Instance.EditorEnabled)
            {
                SelectionCursor.Visibility = Visibility.Hidden;
                return;
            }
            SelectionCursor.Points     = _worldRenderer.GetDiamondPoints(gridX, gridY);
            SelectionCursor.Visibility = Visibility.Visible;
        }

        // ══ Coordinate helper ══════════════════════════════════════════

        private Point ViewportToWorld(Point screenPoint)
        {
            var transform = TransformCanvas.TransformToAncestor((Visual)TransformCanvas.Parent).Inverse;
            return transform?.Transform(screenPoint) ?? screenPoint;
        }

        // ══ Mouse events ═══════════════════════════════════════════════

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var mousePosition = e.GetPosition(ViewportGrid);
            var worldPoint = ViewportToWorld(mousePosition);
            
            _worldRenderer.ScreenToTile(_worldMap, worldPoint.X, worldPoint.Y, out int gridX, out int gridY);
            if (!_worldMap.IsInBounds(gridX, gridY)) return;

            // Shift held: Place decor
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                _worldMap[gridX, gridY].AddDecor("Grass"); // Placeholder decor
                _worldRenderer.Redraw();
                return;
            }

            // Dweller placement mode
            if (_isDwellerPlacementMode)
            {
                var dwellerData = DwellerRegistry.GetByIndex(DwellerPicker.SelectedIndex);
                if (dwellerData != null)
                {
                    var dwellerInstance = new DwellerInstance(dwellerData, gridX, gridY);
                    _dwellerController.Add(dwellerInstance);
                }
                _isDwellerPlacementMode = false;
                UpdateCameraLabel();
                return;
            }

            // Try to select/move dweller
            bool consumed = _dwellerController.HandleTileClick(gridX, gridY, AppConfig.Instance.EditorEnabled);
            if (consumed)
            {
                UpdateDwellerInspector(_dwellerController.Selected);
                return;
            }

            if (!AppConfig.Instance.EditorEnabled) return;

            // Editor mode: Place block
            _worldMap[gridX, gridY].AddBlock(_activeTileName);
            _selectedGridX = gridX; 
            _selectedGridY = gridY;
            
            _worldRenderer.Redraw();
            SnapSelectionCursor(gridX, gridY);
            UpdateInspector(gridX, gridY);
            UpdateMiniMapPartial(gridX, gridY);
        }

        private void Viewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isCameraPanning = true;
            _lastMousePosition = e.GetPosition(this);
            CaptureMouse();
            Cursor = Cursors.SizeAll;

            // Right click + Shift: Remove decor/block
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                var worldPoint = ViewportToWorld(e.GetPosition(ViewportGrid));
                _worldRenderer.ScreenToTile(_worldMap, worldPoint.X, worldPoint.Y, out int gridX, out int gridY);
                
                if (_worldMap.IsInBounds(gridX, gridY))
                {
                    var cell = _worldMap[gridX, gridY];
                    if (cell.Decors.Count > 0) cell.ClearDecors();
                    else cell.RemoveBlock();
                    
                    _worldRenderer.Redraw();
                    UpdateMiniMapPartial(gridX, gridY);
                }
            }
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            var currentPos = e.GetPosition(ViewportGrid);

            if (_isCameraPanning)
            {
                var mousePos = e.GetPosition(this);
                var delta = mousePos - _lastMousePosition;
                
                double newPanX = CameraPan.X + delta.X;
                double newPanY = CameraPan.Y + delta.Y;

                // Apply camera bounds if enabled
                if (AppConfig.Instance.LimitCamera && _worldMap != null)
                {
                    double mapWidthPx = (_worldMap.Columns + _worldMap.Rows) * (AppConfig.Instance.TileWidth / 2.0);
                    double mapHeightPx = (_worldMap.Columns + _worldMap.Rows) * (AppConfig.Instance.TileHeight / 2.0);
                    double margin = AppConfig.Instance.CameraMargin;

                    newPanX = Math.Clamp(newPanX, -mapWidthPx / 2 - margin, mapWidthPx / 2 + margin);
                    newPanY = Math.Clamp(newPanY, -margin, mapHeightPx + margin);
                }

                CameraPan.X = newPanX;
                CameraPan.Y = newPanY;
                
                _lastMousePosition = mousePos;
                UpdateCameraLabel();
                UpdateMiniMapViewport();
            }
            else
            {
                // Math-based hover (no per-tile hit testing)
                var worldPt = ViewportToWorld(currentPos);
                _worldRenderer.OnMouseMove(worldPt);
            }
        }

        private void Viewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isCameraPanning = false; 
            Cursor = Cursors.Arrow; 
            ReleaseMouseCapture();
        }

        private void Viewport_MouseLeave(object sender, MouseEventArgs e)
        {
            _worldRenderer.OnMouseLeave();
            HoverCursor.Visibility = Visibility.Hidden;
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor  = e.Delta > 0 ? 1.12 : 0.893;
            double oldZoom = _cameraZoom;
            _cameraZoom          = Math.Clamp(_cameraZoom * factor, ZOOM_MINIMUM, ZOOM_MAXIMUM);
            
            AppConfig.Instance.DefaultZoom = _cameraZoom;
            AppConfig.Save();

            double ratio   = _cameraZoom / oldZoom;
            var mouse      = e.GetPosition(ViewportGrid);
            CameraPan.X    = mouse.X + (CameraPan.X - mouse.X) * ratio;
            CameraPan.Y    = mouse.Y + (CameraPan.Y - mouse.Y) * ratio;
            CameraScale.ScaleX = CameraScale.ScaleY = _cameraZoom;
            UpdateCameraLabel();
            UpdateMiniMapViewport();
        }

        // ══ Keyboard ═══════════════════════════════════════════════════

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.R:        Camera_Reset(null, null); break;
                case Key.G:        BtnGrid.IsChecked    = !BtnGrid.IsChecked;    Toggle_Grid(null, null);    break;
                case Key.H:        BtnHeights.IsChecked = !BtnHeights.IsChecked; Toggle_Heights(null, null); break;
                case Key.E:        BtnEditorMode.IsChecked = !BtnEditorMode.IsChecked; Toggle_EditorMode(null, null); break;
                case Key.Escape:   _isDwellerPlacementMode = false; _dwellerController.Deselect(); UpdateDwellerInspector(null); UpdateCameraLabel(); break;
                case Key.OemPlus:  case Key.Add:      HeightBrush_Inc(null, null); break;
                case Key.OemMinus: case Key.Subtract: HeightBrush_Dec(null, null); break;
            }
        }

        // ══ Toolbar ════════════════════════════════════════════════════

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
                foreach (var dwellerInstance in dwellers) _dwellerController.Add(dwellerInstance);
                BuildPalette();
            }
            else MessageBox.Show($"Import failed:\n{error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void File_Export(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "Export map", Filter = "World files|*.world.json", DefaultExt = "world.json" };
            if (dlg.ShowDialog() != true) return;
            MapSerializer.Export(_worldMap, _dwellerController.Dwellers, dlg.FileName);
        }

        private void Preset_Island(object sender, RoutedEventArgs e)    => LoadWorld(WorldMap.GenerateIsland(_worldMap?.Columns ?? 200, _worldMap?.Rows ?? 200));
        private void Preset_Wasteland(object sender, RoutedEventArgs e) => LoadWorld(WorldMap.GenerateWasteland(_worldMap?.Columns ?? 40, _worldMap?.Rows ?? 40));
        private void Preset_Clear(object sender, RoutedEventArgs e)     => LoadWorld(new WorldMap(_worldMap?.Columns ?? 40, _worldMap?.Rows ?? 40));

        private void Toggle_Grid(object sender, RoutedEventArgs e)
        {
            _worldRenderer.ShowGrid = BtnGrid.IsChecked == true;
            AppConfig.Instance.ShowGrid = BtnGrid.IsChecked == true;
            AppConfig.Save();
            if (_selectedGridX >= 0) SnapSelectionCursor(_selectedGridX, _selectedGridY);
        }

        private void Toggle_Heights(object sender, RoutedEventArgs e)
        {
            _worldRenderer.ShowHeights = BtnHeights.IsChecked == true;
            AppConfig.Instance.ShowHeights = BtnHeights.IsChecked == true;
            AppConfig.Save();
            _dwellerController.RefreshPositions();
            if (_selectedGridX >= 0) SnapSelectionCursor(_selectedGridX, _selectedGridY);
        }

        private void Toggle_EditorMode(object sender, RoutedEventArgs e)
        {
            AppConfig.Instance.EditorEnabled = BtnEditorMode.IsChecked == true;
            AppConfig.Save();
            ApplyEditorMode();
        }

        private void Camera_Reset(object sender, RoutedEventArgs e)
        {
            _cameraZoom = 1.0; CameraScale.ScaleX = CameraScale.ScaleY = 1.0;
            CameraPan.X = 450; CameraPan.Y = 120;
            UpdateCameraLabel(); UpdateMiniMapViewport();
        }

        private void HeightBrush_Inc(object sender, RoutedEventArgs e)
        {
            _activeHeightBrush = Math.Min(_activeHeightBrush + 1, 6);
            HeightBrushLabel.Text = _activeHeightBrush.ToString();
        }

        private void HeightBrush_Dec(object sender, RoutedEventArgs e)
        {
            _activeHeightBrush = Math.Max(_activeHeightBrush - 1, 0);
            HeightBrushLabel.Text = _activeHeightBrush.ToString();
        }

        private void Palette_AddCustomTile(object sender, RoutedEventArgs e)
        {
            var dlg = new AddTileDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;
            TileRegistry.Register(dlg.TileName, dlg.TopColor, dlg.LeftColor, dlg.RightColor, isCustom: true);
            BuildPalette();
            _activeTileName = dlg.TileName;
            SelectPaletteButton(dlg.TileName);
        }

        // ══ Inspector ══════════════════════════════════════════════════

        private void UpdateInspector(int gridX, int gridY)
        {
            var cell = _worldMap[gridX, gridY];
            int maxHeight = -1;
            foreach (var h in cell.Blocks.Keys) if (h > maxHeight) maxHeight = h;
            
            InspectorCoords.Text = $"Tile ({gridX}, {gridY})";
            InspectorType.Text   = maxHeight >= 0 ? cell.Blocks[maxHeight] : "Empty";
            InspectorHeight.Text = $" {cell.Blocks.Count} ";
            
            // Texture labels - with the new block system, we show the topmost block's texture info
            if (maxHeight >= 0)
            {
                var topBlockDef = TileRegistry.Get(cell.Blocks[maxHeight]);
                TextureTopLabel.Text   = topBlockDef.TopTexturePath   ?? "(default)";
                TextureLeftLabel.Text  = topBlockDef.LeftTexturePath  ?? "(default)";
                TextureRightLabel.Text = topBlockDef.RightTexturePath ?? "(default)";
            }
            else
            {
                TextureTopLabel.Text = TextureLeftLabel.Text = TextureRightLabel.Text = "(default)";
            }
        }

        private void ClearInspector()
        {
            InspectorCoords.Text = "No tile selected";
            InspectorType.Text   = "";
            InspectorHeight.Text = "";
            TextureTopLabel.Text = TextureLeftLabel.Text = TextureRightLabel.Text = "(default)";
        }

        private void Inspector_HeightInc(object sender, RoutedEventArgs e)
        {
            if (_selectedGridX < 0) return;
            _worldMap[_selectedGridX, _selectedGridY].AddBlock(_activeTileName);
            RefreshSelected();
        }

        private void Inspector_HeightDec(object sender, RoutedEventArgs e)
        {
            if (_selectedGridX < 0) return;
            _worldMap[_selectedGridX, _selectedGridY].RemoveBlock();
            RefreshSelected();
        }

        private void RefreshSelected()
        {
            _worldRenderer.Redraw();
            _dwellerController.RefreshPositions();
            SnapSelectionCursor(_selectedGridX, _selectedGridY);
            UpdateInspector(_selectedGridX, _selectedGridY);
        }

        private void TextureScope_Changed(object sender, RoutedEventArgs e)
        {
            bool isCell = sender == BtnScopeCell;
            _isTextureScopeCell    = isCell;
            BtnScopeType.IsChecked = !isCell;
            BtnScopeCell.IsChecked = isCell;
            TextureScopeHint.Text  = isCell ? "Applies to this tile only" : "Applies to all tiles of this type";
            if (_selectedGridX >= 0) UpdateInspector(_selectedGridX, _selectedGridY);
        }

        private void Texture_TopBrowse(object sender, RoutedEventArgs e)
            => BrowseTexture(path => { 
                if (_selectedGridX < 0) return;
                var cell = _worldMap[_selectedGridX, _selectedGridY];
                int maxHeight = -1;
                foreach (var h in cell.Blocks.Keys) if (h > maxHeight) maxHeight = h;
                if (maxHeight < 0) return;
                string blockName = cell.Blocks[maxHeight];
                TileRegistry.Get(blockName).SetTopTexture(path); 
                _worldRenderer.InvalidateBrushCache();
                _worldRenderer.Redraw(); 
                UpdateInspector(_selectedGridX, _selectedGridY); 
            });

        private void Texture_LeftBrowse(object sender, RoutedEventArgs e)
            => BrowseTexture(path => { 
                if (_selectedGridX < 0) return;
                var cell = _worldMap[_selectedGridX, _selectedGridY];
                int maxHeight = -1;
                foreach (var h in cell.Blocks.Keys) if (h > maxHeight) maxHeight = h;
                if (maxHeight < 0) return;
                string blockName = cell.Blocks[maxHeight];
                TileRegistry.Get(blockName).SetLeftTexture(path); 
                _worldRenderer.InvalidateBrushCache();
                _worldRenderer.Redraw(); 
                UpdateInspector(_selectedGridX, _selectedGridY); 
            });

        private void Texture_RightBrowse(object sender, RoutedEventArgs e)
            => BrowseTexture(path => { 
                if (_selectedGridX < 0) return;
                var cell = _worldMap[_selectedGridX, _selectedGridY];
                int maxHeight = -1;
                foreach (var h in cell.Blocks.Keys) if (h > maxHeight) maxHeight = h;
                if (maxHeight < 0) return;
                string blockName = cell.Blocks[maxHeight];
                TileRegistry.Get(blockName).SetRightTexture(path); 
                _worldRenderer.InvalidateBrushCache();
                _worldRenderer.Redraw(); 
                UpdateInspector(_selectedGridX, _selectedGridY); 
            });

        private void Texture_TopClear(object sender, RoutedEventArgs e)
        {
            if (_selectedGridX < 0) return;
            var cell = _worldMap[_selectedGridX, _selectedGridY];
            int maxHeight = -1;
            foreach (var h in cell.Blocks.Keys) if (h > maxHeight) maxHeight = h;
            if (maxHeight < 0) return;
            TileRegistry.Get(cell.Blocks[maxHeight]).SetTopTexture(null);
            _worldRenderer.InvalidateBrushCache();
            RefreshSelected();
        }

        private void Texture_LeftClear(object sender, RoutedEventArgs e)
        {
            if (_selectedGridX < 0) return;
            var cell = _worldMap[_selectedGridX, _selectedGridY];
            int maxHeight = -1;
            foreach (var h in cell.Blocks.Keys) if (h > maxHeight) maxHeight = h;
            if (maxHeight < 0) return;
            TileRegistry.Get(cell.Blocks[maxHeight]).SetLeftTexture(null);
            _worldRenderer.InvalidateBrushCache();
            RefreshSelected();
        }

        private void Texture_RightClear(object sender, RoutedEventArgs e)
        {
            if (_selectedGridX < 0) return;
            var cell = _worldMap[_selectedGridX, _selectedGridY];
            int maxHeight = -1;
            foreach (var h in cell.Blocks.Keys) if (h > maxHeight) maxHeight = h;
            if (maxHeight < 0) return;
            TileRegistry.Get(cell.Blocks[maxHeight]).SetRightTexture(null);
            _worldRenderer.InvalidateBrushCache();
            RefreshSelected();
        }

        private void Texture_ClearAllOverrides(object sender, RoutedEventArgs e)
        {
            if (_selectedGridX < 0) return;
            _worldMap[_selectedGridX, _selectedGridY].IsWalkableOverride = null;
            RefreshSelected();
        }

        private void BrowseTexture(Action<string> onPicked)
        {
            if (_selectedGridX < 0) return;
            var dlg = new OpenFileDialog { Title = "Select texture", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dlg.ShowDialog() == true)
            {
                AssetRegistry.AddTexture(dlg.FileName);
                AssetListBox.ItemsSource = AssetRegistry.TextureNames.ToList();
                onPicked(dlg.FileName);
            }
        }

        // ══ Mini-map partial update (single tile) ══════════════════════

        private void UpdateMiniMapPartial(int gridX, int gridY)
        {
            if (_worldMap == null) return;
            
            double tileWidth  = MINIMAP_WIDTH  / _worldMap.Columns;
            double tileHeight = MINIMAP_HEIGHT / _worldMap.Rows;
            
            var cell = _worldMap[gridX, gridY];
            int maxHeight = -1;
            foreach (var h in cell.Blocks.Keys) if (h > maxHeight) maxHeight = h;
            
            string topBlockName = maxHeight >= 0 ? cell.Blocks[maxHeight] : "Grass";
            var brush = TileRegistry.Get(topBlockName).TopBrush;

            // Find and update the matching rectangle on the minimap
            int index = gridY + gridX * _worldMap.Rows;
            if (index < MiniMap.Children.Count && MiniMap.Children[index] is Rectangle rect)
                rect.Fill = brush;
        }

        // ══ HUD ════════════════════════════════════════════════════════

        private void UpdateCameraLabel()
        {
            if (_isDwellerPlacementMode)
                CameraLabel.Text = "📍 Click a tile to place dweller  [Esc = cancel]";
            else
                CameraLabel.Text = $"zoom {_cameraZoom:F2}x   pan ({CameraPan.X:F0}, {CameraPan.Y:F0})   {_worldMap?.Columns}×{_worldMap?.Rows}";
        }
    }
}
