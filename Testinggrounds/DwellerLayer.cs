using System;
using System.Collections.Generic;
using System.Linq;

namespace IsometricWPF.Dwellers
{
    /// <summary>
    /// Manages all dweller instances placed on the map: selection, movement validation, and rendering sync.
    /// Depends only on <see cref="IsometricRenderer"/> and a <see cref="WorldMap"/> provider — never touches MainWindow.
    /// </summary>
    public class DwellerLayer
    {
        private readonly List<DwellerInstance> _dwellers = new();
        private readonly IsometricRenderer     _renderer;
        private readonly Func<WorldMap?>        _mapProvider;

        private DwellerInstance? _selected;

        public event Action<DwellerInstance?>          DwellerSelected;
        public event Action<DwellerInstance, int, int> DwellerMoved;

        public IReadOnlyList<DwellerInstance> Dwellers => _dwellers;
        public DwellerInstance?               Selected  => _selected;

        // ── Constructor ───────────────────────────────────────────────

        /// <param name="renderer">The shared isometric renderer.</param>
        /// <param name="mapProvider">
        ///   Delegate returning the current WorldMap.
        ///   Using a delegate (rather than a direct reference) allows the map to be swapped
        ///   at runtime (e.g. after File → New) without re-creating the DwellerLayer.
        /// </param>
        public DwellerLayer(IsometricRenderer renderer, Func<WorldMap?> mapProvider)
        {
            _renderer    = renderer;
            _mapProvider = mapProvider;
        }

        // ── Dweller management ────────────────────────────────────────

        public void Add(DwellerInstance dweller)
        {
            _dwellers.Add(dweller);
            _renderer.LoadDwellers(_dwellers);
        }

        public void Remove(DwellerInstance dweller)
        {
            _dwellers.Remove(dweller);
            if (_selected == dweller) _selected = null;
            _renderer.LoadDwellers(_dwellers);
        }

        public void ClearAll()
        {
            _dwellers.Clear();
            _selected = null;
            _renderer.LoadDwellers(_dwellers);
        }

        // ── Validation ────────────────────────────────────────────────

        /// <summary>
        /// Returns true when <paramref name="gridX"/>, <paramref name="gridY"/> is a legal destination
        /// for the currently selected dweller: in-bounds, walkable, not occupied by another dweller.
        /// </summary>
        public bool IsValidMove(int gridX, int gridY)
        {
            var map = _mapProvider();
            if (map == null || !map.IsInBounds(gridX, gridY)) return false;

            var cell = map[gridX, gridY];
            if (cell.Blocks.Count == 0) return false;

            string? top = cell.TopBlockName;
            if (top == null) return false;

            bool walkable = cell.IsWalkableOverride ?? TileRegistry.Get(top).IsWalkable;
            if (!walkable) return false;

            return !_dwellers.Any(d => d != _selected && d.TileX == gridX && d.TileY == gridY);
        }

        // ── Click handling ────────────────────────────────────────────

        /// <summary>
        /// Processes a tile click in the context of the current selection state.
        /// Returns true when the event was fully consumed (move executed or dweller selected/deselected).
        /// </summary>
        public bool HandleTileClick(int gridX, int gridY)
        {
            // If something is selected, attempt a move first
            if (_selected != null)
            {
                if (IsValidMove(gridX, gridY))
                {
                    MoveDweller(_selected, gridX, gridY);
                    Deselect();
                    return true;
                }
            }

            // Try to select a dweller on the clicked tile
            var hit = _dwellers.FirstOrDefault(d => d.TileX == gridX && d.TileY == gridY);
            if (hit != null) { Select(hit); return true; }

            Deselect();
            return false;
        }

        // ── Selection ─────────────────────────────────────────────────

        public void Select(DwellerInstance dweller)
        {
            if (_selected != null) _selected.State = DwellerState.Idle;
            _selected       = dweller;
            dweller.State   = DwellerState.Selected;
            DwellerVisualFactory.InvalidateCache();
            _renderer.Redraw();
            DwellerSelected?.Invoke(dweller);
        }

        public void Deselect()
        {
            if (_selected == null) return;
            _selected.State = DwellerState.Idle;
            _selected       = null;
            DwellerVisualFactory.InvalidateCache();
            _renderer.Redraw();
            DwellerSelected?.Invoke(null);
        }

        // ── Movement ──────────────────────────────────────────────────

        public void MoveDweller(DwellerInstance dweller, int newX, int newY)
        {
            dweller.TileX = newX;
            dweller.TileY = newY;
            _renderer.Redraw();
            DwellerMoved?.Invoke(dweller, newX, newY);
        }

        public void RefreshPositions() => _renderer.Redraw();
    }
}
