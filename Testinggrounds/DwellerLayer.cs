using System;
using System.Collections.Generic;
using System.Linq;
using IsometricWPF.Combat;

namespace IsometricWPF.Dwellers
{
    /// <summary>
    /// Manages all placed dwellers: selection, A*-based movement, attack routing,
    /// and renderer sync. In combat mode defers PA/PM spending to CombatManager.
    /// Never touches MainWindow directly.
    /// </summary>
    public class DwellerLayer
    {
        private readonly List<DwellerInstance> _dwellers   = new();
        private readonly IsometricRenderer     _renderer;
        private readonly Func<WorldMap?>       _mapProvider;

        private DwellerInstance?     _selected;
        private HashSet<(int,int)>   _reachable  = new();
        private List<(int,int)>?     _previewPath;

        // Injected after construction when a combat session starts
        public CombatManager? Combat { get; set; }

        // ── Events ────────────────────────────────────────────────────
        public event Action<DwellerInstance?>?         DwellerSelected;
        public event Action<DwellerInstance,int,int>?  DwellerMoved;
        public event Action<AttackResult>?             AttackResolved;
        public event Action<DwellerInstance>?          DwellerDied;

        // ── Read-only surface ─────────────────────────────────────────
        public IReadOnlyList<DwellerInstance>  Dwellers     => _dwellers;
        public DwellerInstance?                Selected     => _selected;
        public IReadOnlySet<(int,int)>         Reachable    => _reachable;
        public IReadOnlyList<(int,int)>?       PreviewPath  => _previewPath;

        // ── Constructor ───────────────────────────────────────────────

        public DwellerLayer(IsometricRenderer renderer, Func<WorldMap?> mapProvider)
        {
            _renderer    = renderer;
            _mapProvider = mapProvider;
        }

        // ── Collection management ─────────────────────────────────────

        public void Add(DwellerInstance d)
        {
            _dwellers.Add(d);
            SyncRenderer();
        }

        public void Remove(DwellerInstance d)
        {
            _dwellers.Remove(d);
            if (_selected == d) ClearSelection();
            SyncRenderer();
        }

        public void ClearAll()
        {
            _dwellers.Clear();
            ClearSelection();
            SyncRenderer();
        }

        public IEnumerable<DwellerInstance> OfTeam(int teamId)
            => _dwellers.Where(d => d.TeamId == teamId);

        // ── Selection ─────────────────────────────────────────────────

        public void Select(DwellerInstance d)
        {
            if (_selected != null) _selected.State = DwellerState.Idle;
            _selected   = d;
            d.State     = DwellerState.Selected;
            DwellerVisualFactory.InvalidateCache();
            RefreshReachable();
            _renderer.SetMovementHighlight(_reachable);
            _renderer.Redraw();
            DwellerSelected?.Invoke(d);
        }

        public void Deselect()
        {
            if (_selected == null) return;
            ClearSelection();
            _renderer.SetMovementHighlight(null);
            _renderer.SetPathPreview(null);
            _renderer.Redraw();
            DwellerSelected?.Invoke(null);
        }

        private void ClearSelection()
        {
            if (_selected != null) _selected.State = DwellerState.Idle;
            _selected    = null;
            _reachable   = new();
            _previewPath = null;
            DwellerVisualFactory.InvalidateCache();
        }

        // ── Hover: path preview ───────────────────────────────────────

        /// <summary>
        /// Called on mouse-hover while a dweller is selected.
        /// Recomputes the A* preview path to (hx,hy) and tells the renderer to draw it.
        /// </summary>
        public void UpdatePathPreview(int hx, int hy)
        {
            if (_selected == null || _mapProvider() is not { } map)
            {
                _previewPath = null;
                _renderer.SetPathPreview(null);
                return;
            }

            if (!_reachable.Contains((hx, hy)))
            {
                _previewPath = null;
                _renderer.SetPathPreview(null);
                return;
            }

            _previewPath = Pathfinder.FindPath(
                map, _selected.TileX, _selected.TileY, hx, hy, _dwellers, _selected);
            _renderer.SetPathPreview(_previewPath);
            _renderer.Redraw();
        }

        // ── Click handling ────────────────────────────────────────────

        /// <summary>
        /// Main entry point for tile clicks.
        /// Returns true when fully handled (move, attack, or selection change).
        /// </summary>
        public bool HandleTileClick(int gx, int gy)
        {
            var map = _mapProvider();

            if (_selected != null)
            {
                // Attack: enemy dweller on that tile
                var enemy = _dwellers.FirstOrDefault(
                    d => !d.IsDead && d.TileX == gx && d.TileY == gy && d.TeamId != _selected.TeamId);

                if (enemy != null)
                {
                    TryAttack(_selected, enemy);
                    return true;
                }

                // Move: tile is within reachable set
                if (_reachable.Contains((gx, gy)))
                {
                    TryMove(_selected, gx, gy, map!);
                    return true;
                }
            }

            // Select: friendly dweller on that tile (or any dweller in editor mode)
            var hit = _dwellers.FirstOrDefault(d => !d.IsDead && d.TileX == gx && d.TileY == gy);
            if (hit != null)
            {
                // In combat only allow selecting own-team dwellers
                bool canSelect = Combat == null
                    || Combat.ActiveTeam?.TeamId == hit.TeamId;
                if (canSelect) { Select(hit); return true; }
            }

            Deselect();
            return false;
        }

        // ── Movement ─────────────────────────────────────────────────

        private void TryMove(DwellerInstance dweller, int toX, int toY, WorldMap map)
        {
            var path = Pathfinder.FindPath(
                map, dweller.TileX, dweller.TileY, toX, toY, _dwellers, dweller);
            if (path == null || path.Count == 0) return;

            if (Combat != null)
            {
                // Retreat penalty: costs 1 extra PA when leaving an adjacent enemy
                if (Combat.IsAdjacentToEnemy(dweller))
                    if (!Combat.TrySpendRetreatPenalty(dweller)) return;

                if (!Combat.TryMove(dweller, toX, toY, path.Count)) return;
            }
            else
            {
                // Editor mode — free movement
                dweller.TileX = toX;
                dweller.TileY = toY;
            }

            DwellerMoved?.Invoke(dweller, toX, toY);

            // Refresh overlay: PM may have changed
            RefreshReachable();
            _renderer.SetMovementHighlight(_reachable);
            _renderer.SetPathPreview(null);
            DwellerVisualFactory.InvalidateCache();
            _renderer.Redraw();
        }

        // ── Attack ────────────────────────────────────────────────────

        private void TryAttack(DwellerInstance attacker, DwellerInstance target)
        {
            if (Combat == null) return;

            // Use melee weapon by default; ranged if melee is null
            var slot = attacker.MeleeWeapon != null ? WeaponSlot.Melee : WeaponSlot.Ranged;
            var result = Combat.TryAttack(attacker, target, slot);
            if (result == null) return;

            AttackResolved?.Invoke(result);

            if (target.IsDead)
            {
                target.State = DwellerState.Dead;
                DwellerDied?.Invoke(target);
            }

            DwellerVisualFactory.InvalidateCache();
            _renderer.Redraw();
        }

        // ── Walkability ───────────────────────────────────────────────

        public bool IsValidDestination(int gx, int gy)
        {
            var map = _mapProvider();
            if (map == null || !map.IsInBounds(gx, gy)) return false;
            var cell = map[gx, gy];
            if (cell.Blocks.Count == 0) return false;
            string? top = cell.TopBlockName;
            if (top == null) return false;
            bool walkable = cell.IsWalkableOverride ?? TileRegistry.Get(top).IsWalkable;
            if (!walkable) return false;
            return !_dwellers.Any(d => d != _selected && !d.IsDead && d.TileX == gx && d.TileY == gy);
        }

        // ── Helpers ───────────────────────────────────────────────────

        private void RefreshReachable()
        {
            if (_selected == null || _mapProvider() is not { } map)
            {
                _reachable = new();
                return;
            }

            int pm = Combat != null ? _selected.MovementPoints : 999;
            _reachable = Pathfinder.ReachableTiles(
                map, _selected.TileX, _selected.TileY, pm, _dwellers, _selected);
        }

        private void SyncRenderer()
        {
            _renderer.LoadDwellers(_dwellers);
        }

        public void RefreshPositions() => _renderer.Redraw();
    }
}
