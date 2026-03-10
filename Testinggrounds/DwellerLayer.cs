using IsometricWPF.Combat;

namespace IsometricWPF.Dwellers;

public class DwellerLayer {
    private readonly List<DwellerInstance> _dwellers = new();
    private readonly Func<WorldMap?> _mapProvider;
    private readonly IsometricRenderer _renderer;
    private List<(int, int)>? _previewPath;
    private HashSet<(int, int)> _reachable = new();


    public DwellerLayer(IsometricRenderer renderer, Func<WorldMap?> mapProvider) {
        _renderer = renderer;
        _mapProvider = mapProvider;
    }


    public CombatManager? Combat { get; set; }


    public IReadOnlyList<DwellerInstance> Dwellers => _dwellers;
    public DwellerInstance? Selected { get; private set; }

    public IReadOnlySet<(int, int)> Reachable => _reachable;
    public IReadOnlyList<(int, int)>? PreviewPath => _previewPath;


    public event Action<DwellerInstance?>? DwellerSelected;
    public event Action<DwellerInstance, int, int>? DwellerMoved;
    public event Action<AttackResult>? AttackResolved;
    public event Action<DwellerInstance>? DwellerDied;


    public void Add(DwellerInstance d) {
        _dwellers.Add(d);
        SyncRenderer();
    }

    public void Remove(DwellerInstance d) {
        _dwellers.Remove(d);
        if (Selected == d) ClearSelection();
        SyncRenderer();
    }

    public void ClearAll() {
        _dwellers.Clear();
        ClearSelection();
        SyncRenderer();
    }

    public IEnumerable<DwellerInstance> OfTeam(int teamId) {
        return _dwellers.Where(d => d.TeamId == teamId);
    }


    public void Select(DwellerInstance d) {
        if (Selected != null) Selected.State = DwellerState.Idle;
        Selected = d;
        d.State = DwellerState.Selected;
        DwellerVisualFactory.InvalidateCache();
        RefreshReachable();
        _renderer.SetMovementHighlight(_reachable);
        _renderer.Redraw();
        DwellerSelected?.Invoke(d);
    }

    public void Deselect() {
        if (Selected == null) return;
        ClearSelection();
        _renderer.SetMovementHighlight(null);
        _renderer.SetPathPreview(null);
        _renderer.Redraw();
        DwellerSelected?.Invoke(null);
    }

    private void ClearSelection() {
        if (Selected != null) Selected.State = DwellerState.Idle;
        Selected = null;
        _reachable = new HashSet<(int, int)>();
        _previewPath = null;
        DwellerVisualFactory.InvalidateCache();
    }


    public void UpdatePathPreview(int hx, int hy) {
        if (Selected == null || _mapProvider() is not { } map) {
            _previewPath = null;
            _renderer.SetPathPreview(null);
            return;
        }

        if (!_reachable.Contains((hx, hy))) {
            _previewPath = null;
            _renderer.SetPathPreview(null);
            return;
        }

        _previewPath = Pathfinder.FindPath(
            map, Selected.TileX, Selected.TileY, hx, hy, _dwellers, Selected);
        _renderer.SetPathPreview(_previewPath);
        _renderer.Redraw();
    }


    public bool HandleTileClick(int gx, int gy) {
        var map = _mapProvider();

        if (Selected != null) {
            var enemy = _dwellers.FirstOrDefault(d =>
                !d.IsDead && d.TileX == gx && d.TileY == gy && d.TeamId != Selected.TeamId);

            if (enemy != null) {
                TryAttack(Selected, enemy);
                return true;
            }


            if (_reachable.Contains((gx, gy))) {
                TryMove(Selected, gx, gy, map!);
                return true;
            }
        }


        var hit = _dwellers.FirstOrDefault(d => !d.IsDead && d.TileX == gx && d.TileY == gy);
        if (hit != null) {
            var canSelect = Combat == null
                            || Combat.ActiveTeam?.TeamId == hit.TeamId;
            if (canSelect) {
                Select(hit);
                return true;
            }
        }

        Deselect();
        return false;
    }


    private void TryMove(DwellerInstance dweller, int toX, int toY, WorldMap map) {
        var path = Pathfinder.FindPath(
            map, dweller.TileX, dweller.TileY, toX, toY, _dwellers, dweller);
        if (path == null || path.Count == 0) return;

        if (Combat != null) {
            if (Combat.IsAdjacentToEnemy(dweller))
                if (!Combat.TrySpendRetreatPenalty(dweller))
                    return;

            if (!Combat.TryMove(dweller, toX, toY, path.Count)) return;
        }
        else {
            dweller.TileX = toX;
            dweller.TileY = toY;
        }

        DwellerMoved?.Invoke(dweller, toX, toY);


        RefreshReachable();
        _renderer.SetMovementHighlight(_reachable);
        _renderer.SetPathPreview(null);
        DwellerVisualFactory.InvalidateCache();
        _renderer.Redraw();
    }


    private void TryAttack(DwellerInstance attacker, DwellerInstance target) {
        if (Combat == null) return;


        var slot = attacker.MeleeWeapon != null ? WeaponSlot.Melee : WeaponSlot.Ranged;
        var result = Combat.TryAttack(attacker, target, slot);
        if (result == null) return;

        AttackResolved?.Invoke(result);

        if (target.IsDead) {
            target.State = DwellerState.Dead;
            DwellerDied?.Invoke(target);
        }

        DwellerVisualFactory.InvalidateCache();
        _renderer.Redraw();
    }


    public bool IsValidDestination(int gx, int gy) {
        var map = _mapProvider();
        if (map == null || !map.IsInBounds(gx, gy)) return false;
        var cell = map[gx, gy];
        if (cell.Blocks.Count == 0) return false;
        var top = cell.TopBlockName;
        if (top == null) return false;
        var walkable = cell.IsWalkableOverride ?? TileRegistry.Get(top).IsWalkable;
        if (!walkable) return false;
        return !_dwellers.Any(d => d != Selected && !d.IsDead && d.TileX == gx && d.TileY == gy);
    }


    private void RefreshReachable() {
        if (Selected == null || _mapProvider() is not { } map) {
            _reachable = new HashSet<(int, int)>();
            return;
        }

        var pm = Combat != null ? Selected.MovementPoints : 999;
        _reachable = Pathfinder.ReachableTiles(
            map, Selected.TileX, Selected.TileY, pm, _dwellers, Selected);
    }

    private void SyncRenderer() {
        _renderer.LoadDwellers(_dwellers);
    }

    public void RefreshPositions() {
        _renderer.Redraw();
    }
}