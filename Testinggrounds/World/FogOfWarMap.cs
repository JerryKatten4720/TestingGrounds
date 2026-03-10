using System;
using System.Collections.Generic;
using IsometricWPF.Dwellers;

namespace IsometricWPF.World
{
    /// <summary>
    /// Visibility state for a single tile, from the perspective of one team.
    /// </summary>
    public enum TileVisibility
    {
        /// <summary>Never entered the sight range of any friendly dweller.</summary>
        Dark    = 0,
        /// <summary>Was visible at some point but no friendly dweller can see it right now.</summary>
        Seen    = 1,
        /// <summary>Currently within sight range of at least one friendly dweller.</summary>
        Visible = 2,
    }

    /// <summary>
    /// Maintains a per-team fog-of-war grid.
    /// Vision radius comes from each dweller's Perception (P) stat.
    /// Recomputed whenever dwellers move or the active team changes.
    /// </summary>
    public class FogOfWarMap
    {
        private readonly int _cols, _rows;

        // One visibility array per team. Allocated lazily.
        private readonly Dictionary<int, TileVisibility[,]> _teamGrids = new();

        public bool IsNightMode { get; set; } = false;

        public FogOfWarMap(int columns, int rows)
        {
            _cols = columns;
            _rows = rows;
        }

        // ── Querying ──────────────────────────────────────────────────

        public TileVisibility Get(int teamId, int x, int y)
        {
            if (!_teamGrids.TryGetValue(teamId, out var grid)) return TileVisibility.Dark;
            if (x < 0 || x >= _cols || y < 0 || y >= _rows)   return TileVisibility.Dark;
            return grid[x, y];
        }

        // ── Recompute ─────────────────────────────────────────────────

        /// <summary>
        /// Recomputes visibility for <paramref name="teamId"/> given the current positions
        /// of all friendly dwellers. Marks previously-Visible tiles as Seen.
        /// </summary>
        public void Recompute(int teamId, IEnumerable<DwellerInstance> friendlies)
        {
            var grid = EnsureGrid(teamId);

            // Step 1: demote currently Visible → Seen
            for (int x = 0; x < _cols; x++)
                for (int y = 0; y < _rows; y++)
                    if (grid[x, y] == TileVisibility.Visible)
                        grid[x, y] = TileVisibility.Seen;

            // Step 2: paint new Visible circles
            foreach (var d in friendlies)
            {
                if (d.IsDead) continue;
                int radius = VisionRadius(d);
                FloodFillVision(grid, d.TileX, d.TileY, radius);
            }
        }

        /// <summary>
        /// Convenience overload: recomputes all teams present in <paramref name="allDwellers"/>.
        /// </summary>
        public void RecomputeAll(IEnumerable<DwellerInstance> allDwellers)
        {
            // Group by team first
            var byTeam = new Dictionary<int, List<DwellerInstance>>();
            foreach (var d in allDwellers)
            {
                if (!byTeam.TryGetValue(d.TeamId, out var list))
                    byTeam[d.TeamId] = list = new();
                list.Add(d);
            }
            foreach (var kv in byTeam)
                Recompute(kv.Key, kv.Value);
        }

        // ── Vision radius ─────────────────────────────────────────────

        /// <summary>
        /// Vision radius = P stat, halved at night (rounded down, minimum 1).
        /// </summary>
        public int VisionRadius(DwellerInstance d)
        {
            int base_ = Math.Max(1, d.EffectiveP);
            return IsNightMode ? Math.Max(1, base_ / 2) : base_;
        }

        // ── Internals ─────────────────────────────────────────────────

        private TileVisibility[,] EnsureGrid(int teamId)
        {
            if (!_teamGrids.TryGetValue(teamId, out var grid))
            {
                grid = new TileVisibility[_cols, _rows];
                _teamGrids[teamId] = grid;
            }
            return grid;
        }

        /// <summary>
        /// Simple Chebyshev (square) flood fill — fast, avoids diagonal raycasting,
        /// and feels natural for an isometric grid.
        /// </summary>
        private void FloodFillVision(TileVisibility[,] grid, int cx, int cy, int radius)
        {
            int x0 = Math.Max(0, cx - radius);
            int x1 = Math.Min(_cols - 1, cx + radius);
            int y0 = Math.Max(0, cy - radius);
            int y1 = Math.Min(_rows - 1, cy + radius);

            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    grid[x, y] = TileVisibility.Visible;
        }

        // ── Reset ─────────────────────────────────────────────────────

        public void Reset(int teamId) => _teamGrids.Remove(teamId);
        public void ResetAll()        => _teamGrids.Clear();
    }
}
