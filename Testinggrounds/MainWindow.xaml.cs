using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using IsometricWPF.Combat;
using IsometricWPF.Dialogs;
using IsometricWPF.Dwellers;
using IsometricWPF.UI;
using IsometricWPF.World;
using Microsoft.Win32;

namespace IsometricWPF;

public partial class MainWindow : Window {
    private const double MINIMAP_W = 158;
    private const double MINIMAP_H = 108;
    private const double ZOOM_MIN = 0.05;
    private const double ZOOM_MAX = 6.0;
    private readonly CombatManager _combat = new();
    private readonly DayNightCycle _dayNight = new();
    private readonly DwellerLayer _dwellerLayer;


    private readonly Dictionary<(int, int), Rectangle> _minimapRects = new();
    private readonly RandomEventSystem _randomEvents = new();
    private readonly IsometricRenderer _renderer;
    private readonly WeatherSystem _weatherSys = new();
    private string _activeTile = "Grass";


    private bool _combatMode;
    private bool _dayNightEnabled;
    private bool _dwellerPlacementMode;


    private FogOfWarMap _fog = new(1, 1);

    private bool _fogEnabled;


    private bool _isPanning;
    private Point _lastMousePos;


    private DispatcherTimer? _notifTimer;
    private ResourceType _resourceToPaint = ResourceType.Caps;


    private int _selectedX = -1, _selectedY = -1;
    private int _viewerTeamId;
    private bool _weatherEnabled;


    private WorldPaintMode _worldPaint = WorldPaintMode.None;
    private double _zoom = 1.0;


    public MainWindow() {
        InitializeComponent();
        AppConfig.Load();

        DwellerRegistry.Initialize();
        AssetRegistry.Initialize();
        PopulateDwellerPicker();

        _renderer = new IsometricRenderer();
        _renderer.TileHovered += OnTileHovered;
        _renderer.TileHoverLeft += OnTileHoverLeft;
        TileHostContainer.Children.Add(_renderer.Host);

        _dwellerLayer = new DwellerLayer(_renderer, () => World);
        _dwellerLayer.DwellerSelected += OnDwellerSelected;
        _dwellerLayer.DwellerMoved += (d, x, y) => {
            UpdateDwellerInspector(_dwellerLayer.Selected);
            _renderer.Redraw();
        };
        _dwellerLayer.AttackResolved += OnAttackResolved;
        _dwellerLayer.DwellerDied += OnDwellerDied;

        _combat.TurnStarted += OnTurnStarted;
        _combat.TurnEnded += _ => UpdateCombatHud();
        _combat.TeamEliminated += t => ShowNotification($"☠ Team {t.Name} eliminated!");
        _combat.VictoryAchieved += OnVictory;
        _combat.DwellerKilled += d => {
            UpdateMiniMapPartial(d.TileX, d.TileY);
            _renderer.Redraw();
        };
        _combat.WorldEventOccurred += msg => ShowNotification(msg, Colors.LightYellow);
        _combat.ResourceHarvested += OnResourceHarvested;


        _dayNight.PhaseChanged += isNight => {
            _renderer.SetNight(isNight);
            if (_fog != null) _fog.IsNightMode = isNight;
            ShowNotification(isNight ? "🌙 Night falls…" : "☀ Dawn breaks.",
                isNight ? Colors.CornflowerBlue : Colors.LightYellow);
            UpdateWorldHud();
        };
        _dayNight.Tick += _ => UpdateWorldHud();


        _weatherSys.WeatherChanged += w => {
            _renderer.SetWeather(w);
            if (_combat.Weather != null) _combat.Weather = _weatherSys;
            ShowNotification($"🌤 Weather changed: {_weatherSys.DisplayName}");
            UpdateWorldHud();
        };
        _weatherSys.Tick += _ => UpdateWorldHud();

        _zoom = AppConfig.Instance.DefaultZoom;
        CameraScale.ScaleX = CameraScale.ScaleY = _zoom;
        BtnEditorMode.IsChecked = AppConfig.Instance.EditorEnabled;
        BtnGrid.IsChecked = AppConfig.Instance.ShowGrid;
        BtnHeights.IsChecked = AppConfig.Instance.ShowHeights;

        BuildPalette();
        LoadWorld(WorldMap.GenerateIsland(AppConfig.Instance.DefaultMapCols, AppConfig.Instance.DefaultMapRows));
        ApplyEditorMode();

        AssetListBox.ItemsSource = AssetRegistry.TextureNames.ToList();
        Focus();
        SizeChanged += (_, _) => UpdateRendererViewport();
    }

    public WorldMap World { get; private set; }


    private void UpdateRendererViewport() {
        if (_renderer == null || ViewportGrid == null) return;
        var w = ViewportGrid.ActualWidth / _zoom;
        var h = ViewportGrid.ActualHeight / _zoom;
        _renderer.SetViewport(-CameraPan.X / _zoom, -CameraPan.Y / _zoom, w, h);
    }


    private void ApplyEditorMode() {
        var on = AppConfig.Instance.EditorEnabled;
        LeftSidePanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        RightSidePanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        EditorModeLabel.Visibility = Visibility.Visible;
        EditorModeLabel.Text = on ? "  ✏ EDITOR" : "  👁 VIEW";
        EditorModeLabel.Foreground = on
            ? new SolidColorBrush(Color.FromRgb(136, 255, 136))
            : new SolidColorBrush(Color.FromRgb(136, 170, 255));
        if (!on) SelectionCursor.Visibility = HoverCursor.Visibility = Visibility.Hidden;
    }


    private void LoadWorld(WorldMap world) {
        World = world;
        _selectedX = _selectedY = -1;
        SelectionCursor.Visibility = Visibility.Hidden;


        _fog = new FogOfWarMap(world.Columns, world.Rows);
        _fog.IsNightMode = _dayNight.IsNight;
        _renderer.SetFog(_fogEnabled ? _fog : null, _viewerTeamId);

        _renderer.LoadMap(world);
        _dwellerLayer.ClearAll();
        if (_combatMode) ExitCombat();
        RenderMiniMap();
        UpdateMiniMapViewport();
        UpdateCameraLabel();
        ClearInspector();
    }


    private void BtnFog_Click(object sender, RoutedEventArgs e) {
        _fogEnabled = BtnFog.IsChecked == true;
        _renderer.SetFog(_fogEnabled ? _fog : null, _viewerTeamId);
        _renderer.Redraw();
    }

    private void BtnFogReveal_Click(object sender, RoutedEventArgs e) {
        _fog.Recompute(_viewerTeamId, _dwellerLayer.Dwellers.Where(d => d.TeamId == _viewerTeamId));
        _renderer.Redraw();
    }


    private void BtnDayNight_Click(object sender, RoutedEventArgs e) {
        _dayNightEnabled = BtnDayNight.IsChecked == true;
        if (_dayNightEnabled) _dayNight.Start();
        else _dayNight.Stop();
        UpdateWorldHud();
    }

    private void BtnToggleNight_Click(object sender, RoutedEventArgs e) {
        var nowNight = !_dayNight.IsNight;
        _dayNight.Reset();
        if (nowNight) {
            _renderer.SetNight(true);
            if (_fog != null) _fog.IsNightMode = true;
            ShowNotification("🌙 Night (manual)");
        }

        UpdateWorldHud();
    }


    private void BtnWeather_Click(object sender, RoutedEventArgs e) {
        _weatherEnabled = BtnWeather.IsChecked == true;
        if (_weatherEnabled) {
            _combat.Weather = _weatherSys;
            _weatherSys.Start();
        }
        else {
            _combat.Weather = null;
            _weatherSys.Stop();
            _renderer.SetWeather(WeatherType.Clear);
        }

        UpdateWorldHud();
    }


    private void BtnPaintRad_Click(object sender, RoutedEventArgs e) {
        _worldPaint = BtnPaintRad.IsChecked == true ? WorldPaintMode.Radiation : WorldPaintMode.None;
        if (_worldPaint != WorldPaintMode.None) BtnPaintResource.IsChecked = false;
    }

    private void BtnPaintResource_Click(object sender, RoutedEventArgs e) {
        _worldPaint = BtnPaintResource.IsChecked == true ? WorldPaintMode.Resource : WorldPaintMode.None;
        if (_worldPaint != WorldPaintMode.None) BtnPaintRad.IsChecked = false;
    }

    private void ResourceTypePicker_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (ResourceTypePicker.SelectedIndex >= 0)
            _resourceToPaint = (ResourceType)ResourceTypePicker.SelectedIndex;
    }


    private void OnResourceHarvested(ResourceNode node, int amount) {
        ShowNotification($"{node.Icon} Harvested {amount}x {node.Type} (+10 XP)");
        UpdateMiniMapPartial(node.TileX, node.TileY);
        _renderer.Redraw();
    }


    private void BtnHarvest_Click(object sender, RoutedEventArgs e) {
        var sel = _dwellerLayer.Selected;
        if (sel == null || !_combatMode) return;

        var nodes = World.Resources.Adjacent(sel.TileX, sel.TileY);
        foreach (var node in nodes)
            if (_combat.TryHarvest(sel, node)) {
                UpdateCombatHud();
                return;
            }

        ShowNotification("Nothing to harvest here (costs 2 PA).", Colors.Gray);
    }


    private void UpdateWorldHud() {
        var night = _dayNight.IsNight ? "🌙" : "☀";
        var weather = _weatherEnabled ? _weatherSys.DisplayName : "";
        var time = _dayNightEnabled
            ? $"{night} {TimeSpan.FromSeconds(_dayNight.SecondsRemaining):mm\\:ss}"
            : night;

        WorldHudLabel.Text = $"{time}  {weather}".Trim();
        WorldHudBar.Visibility = _dayNightEnabled || _weatherEnabled ? Visibility.Visible : Visibility.Collapsed;
    }


    private void EnterCombat() {
        if (_dwellerLayer.Dwellers.Count == 0) {
            ShowNotification("Place some dwellers first.");
            return;
        }

        var teamIds = _dwellerLayer.Dwellers.Select(d => d.TeamId).Distinct().OrderBy(x => x).ToList();
        if (teamIds.Count < 2) {
            ShowNotification("Need at least 2 teams to start combat.");
            return;
        }

        var rng = new Random();
        var teams = teamIds.Select(id => {
            var ts = new TeamState(id, $"Team {id + 1}");
            var candidates = _dwellerLayer.OfTeam(id).ToList();
            if (candidates.Count > 0) {
                var overseer = candidates[rng.Next(candidates.Count)];
                overseer.PromoteToOverseer();
                ts.Overseer = overseer;
            }

            return ts;
        }).ToList();

        _combatMode = true;
        _dwellerLayer.Combat = _combat;


        _combat.Fog = _fogEnabled ? _fog : null;
        _combat.RandomEvents = _randomEvents;
        _combat.Weather = _weatherEnabled ? _weatherSys : null;
        _combat.Radiation = World.Radiation;

        _combat.StartCombat(teams, _dwellerLayer.Dwellers);


        if (_fogEnabled) {
            _viewerTeamId = teams[0].TeamId;
            _fog.Recompute(_viewerTeamId, _dwellerLayer.OfTeam(_viewerTeamId));
            _renderer.SetFog(_fog, _viewerTeamId);
        }

        BtnCombat.Content = "⚔ END COMBAT";
        BtnEndTurn.Visibility = Visibility.Visible;
        CombatHud.Visibility = Visibility.Visible;
        BtnHarvest.Visibility = Visibility.Visible;

        ShowNotification("⚔ Combat started!");
        UpdateCombatHud();
    }

    private void ExitCombat() {
        _combatMode = false;
        _combat.EndCombat();
        _dwellerLayer.Combat = null;
        _combat.Fog = null;
        _combat.Weather = null;
        _combat.Radiation = null;
        _combat.RandomEvents = null;
        _dwellerLayer.Deselect();

        BtnCombat.Content = "⚔ Start Combat";
        BtnEndTurn.Visibility = Visibility.Collapsed;
        CombatHud.Visibility = Visibility.Collapsed;
        BtnHarvest.Visibility = Visibility.Collapsed;
        _renderer.SetMovementHighlight(null);
        _renderer.SetPathPreview(null);
        _renderer.Redraw();
    }

    private void BtnCombat_Click(object sender, RoutedEventArgs e) {
        if (_combatMode) ExitCombat();
        else EnterCombat();
    }

    private void BtnEndTurn_Click(object sender, RoutedEventArgs e) {
        if (!_combatMode) return;
        _dwellerLayer.Deselect();
        _combat.EndTurn();
    }

    private void OnTurnStarted(TeamState team) {
        UpdateCombatHud();

        if (_fogEnabled) {
            _viewerTeamId = team.TeamId;
            _renderer.SetFog(_fog, _viewerTeamId);
        }

        ShowNotification($"▶ {team.Name}'s turn  —  PA: {team.CurrentPA}/{team.MaxPA}");
        _dwellerLayer.Deselect();
        _renderer.Redraw();
    }

    private void UpdateCombatHud() {
        var team = _combat.ActiveTeam;
        CombatHudLabel.Text = team != null
            ? $"⚔ {team.Name}   PA {team.CurrentPA}/{team.MaxPA}"
            : "";
    }

    private void OnVictory(TeamState winner) {
        MessageBox.Show($"🏆 {winner.Name} wins!\nOverseer survived.", "Victory", MessageBoxButton.OK,
            MessageBoxImage.Information);
        ExitCombat();
    }


    private void OnAttackResolved(AttackResult result) {
        UpdateCombatHud();
        UpdateDwellerInspector(_dwellerLayer.Selected);
        var verb = result.IsCrit ? "💥 CRIT" : result.Hit ? "⚔ Hit" : "✗ Miss";
        var msg = result.Hit
            ? $"{verb}: {result.Attacker.Data.DisplayName} → {result.Target.Data.DisplayName}  (-{result.Damage} HP)"
            : $"{verb}: {result.Attacker.Data.DisplayName} → {result.Target.Data.DisplayName}";
        ShowNotification(msg, result.IsCrit ? Colors.Orange : result.Hit ? Colors.LightGreen : Colors.Gray);
    }

    private void OnDwellerDied(DwellerInstance d) {
        ShowNotification($"☠ {d.Data.DisplayName} has fallen.");
        if (_dwellerLayer.Selected == d) ClearInspector();
        _renderer.Redraw();
    }

    private void ShowNotification(string text, Color? color = null) {
        NotificationLabel.Text = text;
        NotificationLabel.Foreground = new SolidColorBrush(color ?? Colors.White);
        NotificationBar.Visibility = Visibility.Visible;
        _notifTimer?.Stop();
        _notifTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _notifTimer.Tick += (_, _) => {
            _notifTimer.Stop();
            NotificationBar.Visibility = Visibility.Collapsed;
        };
        _notifTimer.Start();
    }


    private void PopulateDwellerPicker() {
        DwellerPicker.Items.Clear();
        foreach (var d in DwellerRegistry.All) DwellerPicker.Items.Add(d.DisplayName);
        if (DwellerPicker.Items.Count > 0) DwellerPicker.SelectedIndex = 0;
    }

    private void SpawnDweller_Click(object sender, RoutedEventArgs e) {
        if (DwellerPicker.SelectedIndex < 0) return;
        _dwellerPlacementMode = true;
        UpdateCameraLabel();
    }

    private void OnDwellerSelected(DwellerInstance? d) {
        UpdateDwellerInspector(d);
    }

    private void UpdateDwellerInspector(DwellerInstance? d) {
        if (d == null) {
            DwellerInspectorPanel.Visibility = Visibility.Collapsed;
            return;
        }

        DwellerInspectorPanel.Visibility = Visibility.Visible;
        DwellerName.Text = d.Data.DisplayName;
        DwellerRarity.Text = $"{d.Data.RarityEnum}  ·  Team {d.TeamId + 1}  ·  Lv {d.Level}";

        var hpBar = BuildBar(d.HP, d.MaxHP, 10, '█', '░');
        var pmBar = BuildBar(d.MovementPoints, d.MaxMovementPoints, 8, '●', '○');

        DwellerSpecial.Text =
            $"S:{d.EffectiveS} P:{d.EffectiveP} E:{d.EffectiveE} C:{d.EffectiveC}\n" +
            $"I:{d.EffectiveI} A:{d.EffectiveA} L:{d.EffectiveL}\n" +
            $"HP  {hpBar}  {d.HP}/{d.MaxHP}\n" +
            $"PM  {pmBar}  {d.MovementPoints}/{d.MaxMovementPoints}\n" +
            $"XP  {d.XP}/{d.XPToNext}";


        BtnLevelUp.Visibility = d.PendingSpecialPoints > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnLevelUp.Content = $"⬆ Spend {d.PendingSpecialPoints} SPECIAL point(s)!";


        BtnToggleHpBar.IsChecked = d.ShowHpBar;


        SlotMelee.Text = $"⚔ {d.MeleeWeapon?.Name ?? "— (none)"}";
        SlotRanged.Text = $"🏹 {d.RangedWeapon?.Name ?? "— (none)"}";
        SlotArmor.Text = $"🛡 {d.EquippedArmor?.Name ?? "— (none)"}";
        SlotPet.Text = $"🐾 {d.Pet?.Name ?? "— (none)"}";


        InventoryList.ItemsSource = null;
        InventoryList.ItemsSource = d.Inventory;


        try {
            var path = DwellerVisualFactory.TextureBasePath + d.Data.Texture;
            var bmp = new BitmapImage(
                new Uri(path, UriKind.RelativeOrAbsolute));
            bmp.Freeze();
            PortraitThumb.Source = bmp;
        }
        catch {
            PortraitThumb.Source = null;
        }


        DwellerBackstory.Text = d.Data.Backstory;
        DwellerBackstory.Visibility = string.IsNullOrWhiteSpace(d.Data.Backstory)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }


    private void BtnLevelUp_Click(object sender, RoutedEventArgs e) {
        var d = _dwellerLayer.Selected;
        if (d == null || d.PendingSpecialPoints <= 0) return;

        var dlg = new SpecialAllocationDialog(d) { Owner = this };
        dlg.ShowDialog();

        DwellerVisualFactory.InvalidateCache();
        UpdateDwellerInspector(d);
        _renderer.Redraw();
        ShowNotification($"✨ {d.Data.DisplayName}'s SPECIAL updated!", Colors.LightGreen);
    }


    private void BtnPortrait_Click(object sender, RoutedEventArgs e) {
        var d = _dwellerLayer.Selected;
        if (d == null) return;
        var overlay = new PortraitOverlay(d, this);
        overlay.Show();
    }


    private void BtnToggleHpBar_Click(object sender, RoutedEventArgs e) {
        var d = _dwellerLayer.Selected;
        if (d == null) return;
        d.ShowHpBar = BtnToggleHpBar.IsChecked == true;
        _renderer.Redraw();
    }

    private void BtnHpBarsGlobal_Click(object sender, RoutedEventArgs e) {
        _renderer.SetShowHpBars(BtnHpBarsGlobal.IsChecked == true);
    }


    private void BtnEquipMelee_Click(object sender, RoutedEventArgs e) {
        var d = _dwellerLayer.Selected;
        if (d == null) return;
        var name = PromptString("Melee weapon name:", d.MeleeWeapon?.Name ?? "");
        if (name == null) return;
        if (string.IsNullOrWhiteSpace(name)) {
            d.MeleeWeapon = null;
        }
        else {
            var dmg = PromptInt("Min damage:", d.MeleeWeapon?.MinDamage ?? 1);
            d.MeleeWeapon = new Weapon { Name = name, MinDamage = dmg, MaxDamage = dmg + 3, Slot = WeaponSlot.Melee };
        }

        DwellerVisualFactory.InvalidateCache();
        UpdateDwellerInspector(d);
    }

    private void BtnEquipRanged_Click(object sender, RoutedEventArgs e) {
        var d = _dwellerLayer.Selected;
        if (d == null) return;
        var name = PromptString("Ranged weapon name:", d.RangedWeapon?.Name ?? "");
        if (name == null) return;
        if (string.IsNullOrWhiteSpace(name)) {
            d.RangedWeapon = null;
        }
        else {
            var dmg = PromptInt("Min damage:", d.RangedWeapon?.MinDamage ?? 1);
            d.RangedWeapon = new Weapon { Name = name, MinDamage = dmg, MaxDamage = dmg + 2, Slot = WeaponSlot.Ranged };
        }

        DwellerVisualFactory.InvalidateCache();
        UpdateDwellerInspector(d);
    }

    private void BtnEquipArmor_Click(object sender, RoutedEventArgs e) {
        var d = _dwellerLayer.Selected;
        if (d == null) return;
        var name = PromptString("Armor name:", d.EquippedArmor?.Name ?? "");
        if (name == null) return;
        if (string.IsNullOrWhiteSpace(name)) {
            d.EquippedArmor = null;
        }
        else {
            var reduce = PromptInt("Damage reduction:", d.EquippedArmor?.DamageReduce ?? 1);
            d.EquippedArmor = new Armor { Name = name, DamageReduce = reduce };
        }

        UpdateDwellerInspector(d);
    }

    private void BtnEquipPet_Click(object sender, RoutedEventArgs e) {
        var d = _dwellerLayer.Selected;
        if (d == null) return;
        var name = PromptString("Pet name:", d.Pet?.Name ?? "");
        if (name == null) return;
        if (string.IsNullOrWhiteSpace(name)) {
            d.Pet = null;
        }
        else {
            var bonusS = PromptInt("Bonus STR:", d.Pet?.BonusS ?? 0);
            var bonusP = PromptInt("Bonus PER:", d.Pet?.BonusP ?? 0);
            var bonusE = PromptInt("Bonus END:", d.Pet?.BonusE ?? 0);
            d.Pet = new Pet { Name = name, BonusS = bonusS, BonusP = bonusP, BonusE = bonusE };
        }

        DwellerVisualFactory.InvalidateCache();
        UpdateDwellerInspector(d);
    }


    private void BtnAddItem_Click(object sender, RoutedEventArgs e) {
        var d = _dwellerLayer.Selected;
        if (d == null) return;
        var name = PromptString("Item name:", "Stimpak");
        if (string.IsNullOrWhiteSpace(name)) return;
        var heal = PromptInt("HP heal when used (0 = no effect):", 20);
        var item = new InventoryItem {
            Name = name,
            Category = heal > 0 ? ItemCategory.Consumable : ItemCategory.Junk,
            HealAmount = heal,
            Icon = heal > 0 ? "💉" : "📦"
        };
        d.AddItem(item);
        UpdateDwellerInspector(d);
    }

    private void InventoryList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void BtnUseItem_Click(object sender, RoutedEventArgs e) {
        var d = _dwellerLayer.Selected;
        if (d == null) return;
        if (InventoryList.SelectedItem is not InventoryItem item) return;

        var healed = d.UseItem(item);
        if (healed < 0) {
            ShowNotification("That item has no effect.", Colors.Gray);
            return;
        }

        UpdateDwellerInspector(d);
        _renderer.Redraw();
        ShowNotification($"💉 {d.Data.DisplayName} used {item.Icon} {item.Name}! +{healed} HP", Colors.LightGreen);
    }


    private static string? PromptString(string prompt, string defaultValue = "") {
        var dlg = new Window {
            Title = prompt,
            Width = 320,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(
                Color.FromRgb(18, 18, 28)),
            ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        var lbl = new TextBlock {
            Text = prompt, Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(0, 0, 0, 8)
        };
        var tb = new TextBox {
            Text = defaultValue, FontFamily = new FontFamily("Consolas"), FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 48)),
            Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromArgb(80, 200, 168, 75)),
            Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 12)
        };
        var ok = new Button {
            Content = "OK", IsDefault = true, Height = 30,
            Background = new SolidColorBrush(Color.FromRgb(200, 168, 75)),
            Foreground = new SolidColorBrush(Color.FromRgb(18, 18, 28)),
            BorderThickness = new Thickness(0), FontFamily = new FontFamily("Consolas"), FontSize = 12
        };
        ok.Click += (_, _) => dlg.DialogResult = true;
        panel.Children.Add(lbl);
        panel.Children.Add(tb);
        panel.Children.Add(ok);
        dlg.Content = panel;
        tb.SelectAll();
        tb.Focus();
        return dlg.ShowDialog() == true ? tb.Text : null;
    }

    private static int PromptInt(string prompt, int defaultValue = 0) {
        var s = PromptString(prompt, defaultValue.ToString());
        return int.TryParse(s, out var v) ? v : defaultValue;
    }

    private static string BuildBar(int cur, int max, int width, char full, char empty) {
        if (max <= 0) return new string(empty, width);
        var filled = (int)Math.Round((double)cur / max * width);
        return new string(full, Math.Clamp(filled, 0, width)) + new string(empty, Math.Clamp(width - filled, 0, width));
    }

    private void DwellerRemove_Click(object sender, RoutedEventArgs e) {
        var sel = _dwellerLayer.Selected;
        if (sel == null) return;
        _dwellerLayer.Remove(sel);
        DwellerInspectorPanel.Visibility = Visibility.Collapsed;
    }

    private int SelectedSpawnTeam() {
        return TeamPicker.SelectedIndex >= 0 ? TeamPicker.SelectedIndex : 0;
    }


    private void Asset_Add_Click(object sender, RoutedEventArgs e) {
        var dlg = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog() == true && AssetRegistry.AddTexture(dlg.FileName))
            AssetListBox.ItemsSource = AssetRegistry.TextureNames.ToList();
    }

    private void Asset_Remove_Click(object sender, RoutedEventArgs e) {
        if (sender is Button { Tag: string name }) {
            AssetRegistry.RemoveTexture(name);
            AssetListBox.ItemsSource = AssetRegistry.TextureNames.ToList();
        }
    }


    private void BuildPalette() {
        PalettePanel.Children.Clear();
        foreach (var kv in TileRegistry.All)
            PalettePanel.Children.Add(MakePaletteButton(kv.Key, kv.Value));
        SelectPaletteButton(_activeTile);
    }

    private Border MakePaletteButton(string name, TileDefinition def) {
        var swatch = new Rectangle { Width = 16, Height = 16, Fill = def.TopBrush, Margin = new Thickness(0, 0, 8, 0) };
        var label = new TextBlock {
            Text = name, Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(swatch);
        row.Children.Add(label);

        if (def.IsCustom) {
            var del = new TextBlock {
                Text = " ✕", Foreground = new SolidColorBrush(Color.FromRgb(255, 80, 80)),
                FontFamily = new FontFamily("Consolas"), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 0, 0)
            };
            del.MouseLeftButtonDown += (s, ev) => {
                ev.Handled = true;
                RemoveCustomTile(name);
            };
            row.Children.Add(del);
        }

        var border = new Border {
            Style = (Style)Application.Current.Resources["PaletteBtn"],
            BorderBrush = Brushes.Transparent, Child = row, Tag = name
        };
        border.MouseLeftButtonDown += (_, _) => {
            _activeTile = name;
            _dwellerPlacementMode = false;
            _worldPaint = WorldPaintMode.None;
            BtnPaintRad.IsChecked = BtnPaintResource.IsChecked = false;
            SelectPaletteButton(name);
        };
        return border;
    }

    private void SelectPaletteButton(string name) {
        foreach (Border b in PalettePanel.Children) {
            var active = b.Tag?.ToString() == name;
            b.BorderBrush = active ? new SolidColorBrush(Color.FromRgb(200, 168, 75)) : Brushes.Transparent;
            b.Background = active ? new SolidColorBrush(Color.FromArgb(60, 200, 168, 75)) : Brushes.Transparent;
        }
    }

    private void RemoveCustomTile(string name) {
        TileRegistry.Remove(name);
        if (_activeTile == name) _activeTile = "Grass";
        BuildPalette();
    }


    private void RenderMiniMap() {
        MiniMap.Children.Clear();
        _minimapRects.Clear();
        if (World == null) return;
        var tw = MINIMAP_W / World.Columns;
        var th = MINIMAP_H / World.Rows;
        for (var x = 0; x < World.Columns; x++)
        for (var y = 0; y < World.Rows; y++) {
            var rect = new Rectangle { Width = tw + 0.6, Height = th + 0.6, Fill = TopBrushForCell(World[x, y]) };
            Canvas.SetLeft(rect, x * tw);
            Canvas.SetTop(rect, y * th);
            MiniMap.Children.Add(rect);
            _minimapRects[(x, y)] = rect;
        }
    }

    private void UpdateMiniMapViewport() {
        if (World == null) return;
        var mapPxW = (World.Columns + World.Rows) * (AppConfig.Instance.TileWidth / 2.0);
        var mapPxH = (World.Columns + World.Rows) * (AppConfig.Instance.TileHeight / 2.0);
        var visW = ViewportGrid.ActualWidth / _zoom;
        var visH = ViewportGrid.ActualHeight / _zoom;
        var vpX = -CameraPan.X / _zoom;
        var vpY = -CameraPan.Y / _zoom;
        Canvas.SetLeft(MiniMapViewport, Math.Max(0, (vpX / mapPxW + 0.5) * MINIMAP_W));
        Canvas.SetTop(MiniMapViewport, Math.Max(0, (vpY / mapPxH + 0.5) * MINIMAP_H));
        MiniMapViewport.Width = Math.Clamp(visW / mapPxW * MINIMAP_W, 4, MINIMAP_W);
        MiniMapViewport.Height = Math.Clamp(visH / mapPxH * MINIMAP_H, 4, MINIMAP_H);
        UpdateRendererViewport();
    }

    private void UpdateMiniMapPartial(int x, int y) {
        if (_minimapRects.TryGetValue((x, y), out var rect))
            rect.Fill = TopBrushForCell(World[x, y]);
    }

    private static Brush TopBrushForCell(TileCell cell) {
        if (cell.IsRadiationZone) return new SolidColorBrush(Color.FromRgb(60, 200, 60));
        var top = cell.TopBlockName;
        return top != null ? TileRegistry.Get(top).TopBrush : Brushes.Black;
    }


    private void OnTileHovered(int gx, int gy) {
        HoverCursor.Points = _renderer.GetDiamondPoints(gx, gy);
        HoverCursor.Visibility = Visibility.Visible;

        var sel = _dwellerLayer.Selected;
        var hasSel = sel != null;
        var hasEnemy = hasSel &&
                       _dwellerLayer.Dwellers.Any(d =>
                           !d.IsDead && d.TileX == gx && d.TileY == gy && d.TeamId != sel!.TeamId);
        var reachable = hasSel && _dwellerLayer.Reachable.Contains((gx, gy));

        HoverCursor.Stroke = hasEnemy ? Brushes.Red
            : reachable ? Brushes.LightGreen
            : Brushes.White;
        HoverCursor.Fill = hasEnemy
            ? new SolidColorBrush(Color.FromArgb(40, 255, 0, 0))
            : reachable
                ? new SolidColorBrush(Color.FromArgb(20, 0, 255, 0))
                : new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));

        if (reachable) _dwellerLayer.UpdatePathPreview(gx, gy);


        var node = World?.Resources.At(gx, gy);
        if (node != null)
            CameraLabel.Text = $"{node.Icon} {node.Type}  {node.Quantity}/{node.MaxQuantity}";
    }

    private void OnTileHoverLeft(int gx, int gy) {
        HoverCursor.Visibility = Visibility.Hidden;
        _renderer.SetPathPreview(null);
        _renderer.Redraw();
        UpdateCameraLabel();
    }

    private void SnapSelectionCursor(int gx, int gy) {
        if (gx < 0 || !AppConfig.Instance.EditorEnabled) {
            SelectionCursor.Visibility = Visibility.Hidden;
            return;
        }

        SelectionCursor.Points = _renderer.GetDiamondPoints(gx, gy);
        SelectionCursor.Visibility = Visibility.Visible;
    }


    private Point ViewportToWorld(Point screen) {
        var inverse = TransformCanvas.TransformToAncestor((Visual)TransformCanvas.Parent).Inverse;
        return inverse?.Transform(screen) ?? screen;
    }


    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        var world = ViewportToWorld(e.GetPosition(ViewportGrid));
        _renderer.ScreenToTile(World, world.X, world.Y, out var gx, out var gy);
        if (!World.IsInBounds(gx, gy)) return;


        if (_worldPaint == WorldPaintMode.Radiation) {
            var newState = !World[gx, gy].IsRadiationZone;
            World.SetRadiation(gx, gy, newState);
            UpdateMiniMapPartial(gx, gy);
            _renderer.Redraw();
            return;
        }

        if (_worldPaint == WorldPaintMode.Resource) {
            if (World[gx, gy].Resource != null)
                World.RemoveResource(gx, gy);
            else
                World.PlaceResource(gx, gy, _resourceToPaint);
            _renderer.Redraw();
            return;
        }


        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) {
            World[gx, gy].AddDecor(_activeTile);
            _renderer.Redraw();
            return;
        }


        if (_dwellerPlacementMode) {
            var data = DwellerRegistry.GetByIndex(DwellerPicker.SelectedIndex);
            if (data != null) {
                var inst = new DwellerInstance(data, gx, gy) { TeamId = SelectedSpawnTeam() };
                _dwellerLayer.Add(inst);
            }

            _dwellerPlacementMode = false;
            UpdateCameraLabel();
            return;
        }


        var consumed = _dwellerLayer.HandleTileClick(gx, gy);
        if (consumed) return;

        if (!AppConfig.Instance.EditorEnabled) return;


        World[gx, gy].AddBlock(_activeTile);
        _selectedX = gx;
        _selectedY = gy;
        _renderer.Redraw();
        SnapSelectionCursor(gx, gy);
        UpdateInspector(gx, gy);
        UpdateMiniMapPartial(gx, gy);
    }

    private void Viewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
        _isPanning = true;
        _lastMousePos = e.GetPosition(this);
        CaptureMouse();
        Cursor = Cursors.SizeAll;

        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) {
            var world = ViewportToWorld(e.GetPosition(ViewportGrid));
            _renderer.ScreenToTile(World, world.X, world.Y, out var gx, out var gy);
            if (World.IsInBounds(gx, gy)) {
                var cell = World[gx, gy];
                if (cell.Decors.Count > 0) cell.ClearDecors();
                else cell.RemoveBlock();
                _renderer.Redraw();
                UpdateMiniMapPartial(gx, gy);
            }
        }
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e) {
        if (_isPanning) {
            var cur = e.GetPosition(this);
            var delta = cur - _lastMousePos;
            var nx = CameraPan.X + delta.X;
            var ny = CameraPan.Y + delta.Y;

            if (AppConfig.Instance.LimitCamera && World != null) {
                var mw = (World.Columns + World.Rows) * (AppConfig.Instance.TileWidth / 2.0);
                var mh = (World.Columns + World.Rows) * (AppConfig.Instance.TileHeight / 2.0);
                var mg = AppConfig.Instance.CameraMargin;
                nx = Math.Clamp(nx, -mw / 2 - mg, mw / 2 + mg);
                ny = Math.Clamp(ny, -mg, mh + mg);
            }

            CameraPan.X = nx;
            CameraPan.Y = ny;
            _lastMousePos = cur;
            UpdateCameraLabel();
            UpdateMiniMapViewport();
        }
        else {
            _renderer.OnMouseMove(ViewportToWorld(e.GetPosition(ViewportGrid)));
        }
    }

    private void Viewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e) {
        _isPanning = false;
        Cursor = Cursors.Arrow;
        ReleaseMouseCapture();
    }

    private void Viewport_MouseLeave(object sender, MouseEventArgs e) {
        _renderer.OnMouseLeave();
        HoverCursor.Visibility = Visibility.Hidden;
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e) {
        var factor = e.Delta > 0 ? 1.12 : 0.893;
        var oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom * factor, ZOOM_MIN, ZOOM_MAX);
        var ratio = _zoom / oldZoom;
        var mouse = e.GetPosition(ViewportGrid);
        CameraPan.X = mouse.X + (CameraPan.X - mouse.X) * ratio;
        CameraPan.Y = mouse.Y + (CameraPan.Y - mouse.Y) * ratio;
        CameraScale.ScaleX = CameraScale.ScaleY = _zoom;
        AppConfig.Instance.DefaultZoom = _zoom;
        AppConfig.SaveDebounced();
        UpdateCameraLabel();
        UpdateMiniMapViewport();
    }


    private void Window_KeyDown(object sender, KeyEventArgs e) {
        switch (e.Key) {
            case Key.R: Camera_Reset(null!, null!); break;
            case Key.G:
                BtnGrid.IsChecked = !BtnGrid.IsChecked;
                Toggle_Grid(null!, null!);
                break;
            case Key.H:
                BtnHeights.IsChecked = !BtnHeights.IsChecked;
                Toggle_Heights(null!, null!);
                break;
            case Key.E:
                BtnEditorMode.IsChecked = !BtnEditorMode.IsChecked;
                Toggle_EditorMode(null!, null!);
                break;
            case Key.T:
                if (_combatMode) _combat.EndTurn();
                break;
            case Key.Escape:
                _dwellerPlacementMode = false;
                _worldPaint = WorldPaintMode.None;
                BtnPaintRad.IsChecked = BtnPaintResource.IsChecked = false;
                _dwellerLayer.Deselect();
                UpdateDwellerInspector(null);
                UpdateCameraLabel();
                break;
            case Key.OemPlus:
            case Key.Add: HeightBrush_Inc(null!, null!); break;
            case Key.OemMinus:
            case Key.Subtract: HeightBrush_Dec(null!, null!); break;
        }
    }


    private void File_New(object sender, RoutedEventArgs e) {
        var dlg = new NewMapDialog { Owner = this };
        if (dlg.ShowDialog() == true) LoadWorld(new WorldMap(dlg.MapCols, dlg.MapRows));
    }

    private void File_Import(object sender, RoutedEventArgs e) {
        var dlg = new OpenFileDialog { Title = "Import map", Filter = "World files|*.world.json|All|*.*" };
        if (dlg.ShowDialog() != true) return;
        var (map, dwellers, ok, error) = MapSerializer.Import(dlg.FileName);
        if (ok) {
            LoadWorld(map);
            foreach (var d in dwellers) _dwellerLayer.Add(d);
            BuildPalette();
        }
        else {
            MessageBox.Show($"Import failed:\n{error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void File_Export(object sender, RoutedEventArgs e) {
        var dlg = new SaveFileDialog
            { Title = "Export map", Filter = "World files|*.world.json", DefaultExt = "world.json" };
        if (dlg.ShowDialog() == true) MapSerializer.Export(World, _dwellerLayer.Dwellers, dlg.FileName);
    }

    private void Preset_Island(object sender, RoutedEventArgs e) {
        LoadWorld(WorldMap.GenerateIsland(World?.Columns ?? 40, World?.Rows ?? 40));
    }

    private void Preset_Wasteland(object sender, RoutedEventArgs e) {
        LoadWorld(WorldMap.GenerateWasteland(World?.Columns ?? 40, World?.Rows ?? 40));
    }

    private void Preset_Clear(object sender, RoutedEventArgs e) {
        LoadWorld(new WorldMap(World?.Columns ?? 40, World?.Rows ?? 40));
    }

    private void Toggle_Grid(object sender, RoutedEventArgs e) {
        _renderer.ShowGrid = BtnGrid.IsChecked == true;
        AppConfig.Instance.ShowGrid = _renderer.ShowGrid;
        AppConfig.Save();
    }

    private void Toggle_Heights(object sender, RoutedEventArgs e) {
        _renderer.ShowHeights = BtnHeights.IsChecked == true;
        AppConfig.Instance.ShowHeights = _renderer.ShowHeights;
        AppConfig.Save();
        _dwellerLayer.RefreshPositions();
        if (_selectedX >= 0) SnapSelectionCursor(_selectedX, _selectedY);
    }

    private void Toggle_EditorMode(object sender, RoutedEventArgs e) {
        AppConfig.Instance.EditorEnabled = BtnEditorMode.IsChecked == true;
        AppConfig.Save();
        ApplyEditorMode();
    }

    private void Camera_Reset(object sender, RoutedEventArgs e) {
        _zoom = 1.0;
        CameraScale.ScaleX = CameraScale.ScaleY = 1.0;
        CameraPan.X = 450;
        CameraPan.Y = 120;
        UpdateCameraLabel();
        UpdateMiniMapViewport();
    }

    private void HeightBrush_Inc(object sender, RoutedEventArgs e) {
        var h = int.TryParse(HeightBrushLabel.Text, out var v) ? v : 0;
        HeightBrushLabel.Text = Math.Min(h + 1, AppConfig.Instance.MaxStackHeight - 1).ToString();
    }

    private void HeightBrush_Dec(object sender, RoutedEventArgs e) {
        var h = int.TryParse(HeightBrushLabel.Text, out var v) ? v : 0;
        HeightBrushLabel.Text = Math.Max(h - 1, 0).ToString();
    }

    private void Palette_AddCustomTile(object sender, RoutedEventArgs e) {
        var dlg = new AddTileDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        TileRegistry.Register(dlg.TileName, dlg.TopColor, dlg.LeftColor, dlg.RightColor, true);
        BuildPalette();
        _activeTile = dlg.TileName;
        SelectPaletteButton(dlg.TileName);
    }


    private void UpdateInspector(int gx, int gy) {
        var cell = World[gx, gy];
        InspectorCoords.Text = $"Tile ({gx}, {gy})";
        InspectorType.Text = cell.TopBlockName ?? "Empty";
        InspectorHeight.Text = $" {cell.Blocks.Count} ";


        var extra = "";
        if (cell.IsRadiationZone) extra += "  ☢ RAD";
        if (cell.Resource != null) extra += $"  {cell.Resource.Icon}{cell.Resource.Type}";
        InspectorType.Text += extra;

        var top = cell.TopBlockName;
        var def = top != null ? TileRegistry.Get(top) : null;
        TextureTopLabel.Text = def?.TopTexturePath ?? "(default)";
        TextureLeftLabel.Text = def?.LeftTexturePath ?? "(default)";
        TextureRightLabel.Text = def?.RightTexturePath ?? "(default)";
    }

    private void ClearInspector() {
        InspectorCoords.Text = "No tile selected";
        InspectorType.Text = InspectorHeight.Text = "";
        TextureTopLabel.Text = TextureLeftLabel.Text = TextureRightLabel.Text = "(default)";
        DwellerInspectorPanel.Visibility = Visibility.Collapsed;
    }

    private void Inspector_HeightInc(object sender, RoutedEventArgs e) {
        if (_selectedX < 0) return;
        World[_selectedX, _selectedY].AddBlock(_activeTile);
        RefreshSelected();
    }

    private void Inspector_HeightDec(object sender, RoutedEventArgs e) {
        if (_selectedX < 0) return;
        World[_selectedX, _selectedY].RemoveBlock();
        RefreshSelected();
    }

    private void RefreshSelected() {
        _renderer.Redraw();
        _dwellerLayer.RefreshPositions();
        SnapSelectionCursor(_selectedX, _selectedY);
        UpdateInspector(_selectedX, _selectedY);
    }


    private void TextureScope_Changed(object sender, RoutedEventArgs e) {
        var isCell = sender == BtnScopeCell;
        BtnScopeType.IsChecked = !isCell;
        BtnScopeCell.IsChecked = isCell;
        TextureScopeHint.Text = isCell ? "Applies to this tile only" : "Applies to all tiles of this type";
    }

    private void Texture_TopBrowse(object sender, RoutedEventArgs e) {
        BrowseTexture(p => ApplyTexture(p, 0));
    }

    private void Texture_LeftBrowse(object sender, RoutedEventArgs e) {
        BrowseTexture(p => ApplyTexture(p, 1));
    }

    private void Texture_RightBrowse(object sender, RoutedEventArgs e) {
        BrowseTexture(p => ApplyTexture(p, 2));
    }

    private void Texture_TopClear(object sender, RoutedEventArgs e) {
        ApplyTexture(null, 0);
    }

    private void Texture_LeftClear(object sender, RoutedEventArgs e) {
        ApplyTexture(null, 1);
    }

    private void Texture_RightClear(object sender, RoutedEventArgs e) {
        ApplyTexture(null, 2);
    }

    private void Texture_ClearAllOverrides(object sender, RoutedEventArgs e) {
        if (_selectedX < 0) return;
        World[_selectedX, _selectedY].IsWalkableOverride = null;
        RefreshSelected();
    }

    private void ApplyTexture(string? path, int face) {
        if (_selectedX < 0) return;
        var top = World[_selectedX, _selectedY].TopBlockName;
        if (top == null) return;
        var def = TileRegistry.Get(top);
        if (face == 0) def.SetTopTexture(path);
        else if (face == 1) def.SetLeftTexture(path);
        else def.SetRightTexture(path);
        _renderer.InvalidateBrushCache();
        RefreshSelected();
        UpdateInspector(_selectedX, _selectedY);
    }

    private void BrowseTexture(Action<string> onPicked) {
        if (_selectedX < 0) return;
        var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog() == true) {
            AssetRegistry.AddTexture(dlg.FileName);
            AssetListBox.ItemsSource = AssetRegistry.TextureNames.ToList();
            onPicked(dlg.FileName);
        }
    }


    private void UpdateCameraLabel() {
        CameraLabel.Text = _dwellerPlacementMode
            ? $"📍 Click tile to place — Team {SelectedSpawnTeam() + 1}  [Esc = cancel]"
            : $"zoom {_zoom:F2}x   ({CameraPan.X:F0}, {CameraPan.Y:F0})   {World?.Columns}×{World?.Rows}";
    }


    private enum WorldPaintMode {
        None,
        Radiation,
        Resource
    }
}