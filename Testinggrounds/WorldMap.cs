using System;
using System.Collections.Generic;
using System.Linq;
using IsometricWPF.World;

namespace IsometricWPF
{
    /// <summary>
    /// A single grid cell. Holds a height-indexed block stack, decor list,
    /// and Phase 2 world-layer flags (radiation, resource node).
    /// </summary>
    public class TileCell
    {
        // ── Existing ──────────────────────────────────────────────────
        public Dictionary<int, string> Blocks { get; } = new();
        public List<string>            Decors { get; } = new();
        public bool?                   IsWalkableOverride { get; set; }

        // ── Phase 2: world layer ──────────────────────────────────────

        /// <summary>True when this tile is a radiation zone (player-placed in the editor).</summary>
        public bool IsRadiationZone { get; set; } = false;

        /// <summary>Resource node sitting on this tile, if any.</summary>
        public ResourceNode? Resource { get; set; } = null;

        // ── Derived helpers ───────────────────────────────────────────
        public int     MaxBlockHeight => Blocks.Count == 0 ? -1 : Blocks.Keys.Max();
        public string? TopBlockName   => MaxBlockHeight >= 0 ? Blocks[MaxBlockHeight] : null;

        // ── Mutation ──────────────────────────────────────────────────
        public void AddBlock(string tileName)
        {
            int height = 0;
            while (Blocks.ContainsKey(height)) height++;
            if (height < AppConfig.Instance.MaxStackHeight)
                Blocks[height] = tileName;
        }

        public void RemoveBlock()
        {
            int top = MaxBlockHeight;
            if (top >= 0) Blocks.Remove(top);
        }

        public void AddDecor(string decorName)    => Decors.Add(decorName);
        public void RemoveDecor(string decorName) => Decors.Remove(decorName);
        public void ClearDecors()                 => Decors.Clear();
    }


    /// <summary>
    /// 2-D grid of <see cref="TileCell"/> objects.
    /// Phase 2: also owns the <see cref="RadiationZone"/> index (kept in sync with cell flags)
    /// and the <see cref="ResourceNodeRegistry"/>.
    /// </summary>
    public class WorldMap
    {
        public int Columns { get; }
        public int Rows    { get; }

        private readonly TileCell[,] _cells;

        // ── Phase 2 world layers ──────────────────────────────────────
        public RadiationZone         Radiation { get; } = new();
        public ResourceNodeRegistry  Resources { get; } = new();

        public WorldMap(int columns, int rows)
        {
            Columns = columns;
            Rows    = rows;
            _cells  = new TileCell[columns, rows];
            for (int x = 0; x < columns; x++)
                for (int y = 0; y < rows; y++)
                    _cells[x, y] = new TileCell();
        }

        public TileCell this[int x, int y] => _cells[x, y];
        public bool IsInBounds(int x, int y) => x >= 0 && x < Columns && y >= 0 && y < Rows;

        // ── Radiation helpers ─────────────────────────────────────────

        public void SetRadiation(int x, int y, bool on)
        {
            if (!IsInBounds(x, y)) return;
            _cells[x, y].IsRadiationZone = on;
            if (on)  Radiation.Add(x, y);
            else     Radiation.Remove(x, y);
        }

        // ── Resource helpers ──────────────────────────────────────────

        public void PlaceResource(int x, int y, ResourceType type, int quantity = 10)
        {
            if (!IsInBounds(x, y)) return;
            var node = new ResourceNode { TileX = x, TileY = y, Type = type, Quantity = quantity, MaxQuantity = quantity };
            _cells[x, y].Resource = node;
            Resources.Place(node);
        }

        public void RemoveResource(int x, int y)
        {
            if (!IsInBounds(x, y)) return;
            _cells[x, y].Resource = null;
            Resources.Remove(x, y);
        }

        // ── Fill helper ───────────────────────────────────────────────

        public void SetStackHeight(int x, int y, string tileName, int height)
        {
            if (!IsInBounds(x, y)) return;
            var cell = _cells[x, y];
            cell.Blocks.Clear();
            for (int i = 0; i < height; i++)
                cell.Blocks[i] = tileName;
        }

        // ── Terrain generators ────────────────────────────────────────

        public static WorldMap GenerateIsland(int columns, int rows, int seed = 42)
        {
            var map    = new WorldMap(columns, rows);
            var random = new Random(seed);
            double halfCols = columns / 2.0, halfRows = rows / 2.0;

            for (int x = 0; x < columns; x++)
            for (int y = 0; y < rows; y++)
            {
                double dist  = Math.Sqrt(Math.Pow(x - halfCols, 2) + Math.Pow(y - halfRows, 2));
                double noise = random.NextDouble() * 4;

                if      (dist + noise > columns * 0.42) map.SetStackHeight(x, y, "Water",  1);
                else if (dist + noise > columns * 0.36) map.SetStackHeight(x, y, "Sand",   1);
                else
                {
                    int roll = random.Next(100);
                    if      (roll < 8)  map.SetStackHeight(x, y, "Stone",  random.Next(1, 4));
                    else if (roll < 16) map.SetStackHeight(x, y, "Forest", random.Next(1, 3));
                    else if (roll < 22) map.SetStackHeight(x, y, "Dirt",   random.Next(1, 2));
                    else if (roll < 25) map.SetStackHeight(x, y, "Snow",   random.Next(2, 4));
                    else                map.SetStackHeight(x, y, "Grass",  random.Next(1, 2));
                }
            }
            return map;
        }

        public static WorldMap GenerateWasteland(int columns, int rows, int seed = 7)
        {
            var map    = new WorldMap(columns, rows);
            var random = new Random(seed);

            for (int x = 0; x < columns; x++)
            for (int y = 0; y < rows; y++)
            {
                int roll = random.Next(100);
                if      (roll < 30) map.SetStackHeight(x, y, "Ash",      random.Next(1, 2));
                else if (roll < 55) map.SetStackHeight(x, y, "Concrete", random.Next(1, 2));
                else if (roll < 70) map.SetStackHeight(x, y, "Dirt",     1);
                else if (roll < 80) map.SetStackHeight(x, y, "Rust",     random.Next(1, 3));
                else if (roll < 88) map.SetStackHeight(x, y, "Stone",    random.Next(1, 4));
                else                map.SetStackHeight(x, y, "Ash",      1);
            }

            // Scatter some rad zones and caps piles in the wasteland
            for (int x = 0; x < columns; x++)
            for (int y = 0; y < rows; y++)
            {
                if (random.Next(100) < 4)  map.SetRadiation(x, y, true);
                if (random.Next(100) < 3)  map.PlaceResource(x, y, ResourceType.Caps,      random.Next(3, 12));
                if (random.Next(100) < 2)  map.PlaceResource(x, y, ResourceType.ScrapMetal, random.Next(5, 15));
            }

            return map;
        }
    }
}
