using System;
using System.Collections.Generic;
using System.Linq;

namespace IsometricWPF
{
    /// <summary>
    /// A single grid cell. Holds a height-indexed block stack and an optional decor list.
    /// </summary>
    public class TileCell
    {
        /// <summary>Block stack: key = height index (0-based), value = tile name.</summary>
        public Dictionary<int, string> Blocks { get; } = new();

        /// <summary>Decoration names stacked on top of this cell.</summary>
        public List<string> Decors { get; } = new();

        /// <summary>Per-cell walkability override; null means "use tile definition default".</summary>
        public bool? IsWalkableOverride { get; set; }

        // ── Derived helpers ───────────────────────────────────────────

        /// <summary>Highest occupied height index, or -1 when the cell is empty.</summary>
        public int MaxBlockHeight => Blocks.Count == 0 ? -1 : Blocks.Keys.Max();

        /// <summary>Name of the topmost block, or null when the cell is empty.</summary>
        public string? TopBlockName => MaxBlockHeight >= 0 ? Blocks[MaxBlockHeight] : null;

        // ── Mutation ──────────────────────────────────────────────────

        /// <summary>Pushes <paramref name="tileName"/> onto the next available height slot (up to MaxStackHeight).</summary>
        public void AddBlock(string tileName)
        {
            int height = 0;
            while (Blocks.ContainsKey(height)) height++;
            if (height < AppConfig.Instance.MaxStackHeight)
                Blocks[height] = tileName;
        }

        /// <summary>Removes the topmost block, if any.</summary>
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
    /// 2-D grid of <see cref="TileCell"/> objects with built-in terrain generators.
    /// </summary>
    public class WorldMap
    {
        public int Columns { get; }
        public int Rows    { get; }

        private readonly TileCell[,] _cells;

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

        /// <summary>Fills a cell with a contiguous stack of <paramref name="height"/> blocks of the same type.</summary>
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
            return map;
        }
    }
}
