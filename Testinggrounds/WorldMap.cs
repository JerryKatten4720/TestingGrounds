using System;
using System.Collections.Generic;

namespace IsometricWPF
{

    public class TileCell
    {

        public Dictionary<int, string> Blocks { get; } = new Dictionary<int, string>();
        public List<string> Decors { get; } = new List<string>();
        

        public bool? IsWalkableOverride { get; set; }

        public void AddBlock(string tileName)
        {

            int height = 0;
            while (Blocks.ContainsKey(height)) height++;
            
            if (height < AppConfig.Instance.MaxStackHeight)
            {
                Blocks[height] = tileName;
            }
        }

        public void RemoveBlock()
        {

            int maxHeight = -1;
            foreach (var h in Blocks.Keys) if (h > maxHeight) maxHeight = h;
            
            if (maxHeight >= 0)
            {
                Blocks.Remove(maxHeight);
            }
        }

        public void AddDecor(string decorName) => Decors.Add(decorName);
        public void RemoveDecor(string decorName) => Decors.Remove(decorName);
        public void ClearDecors() => Decors.Clear();
    }


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
            {
                for (int y = 0; y < rows; y++)
                {
                    _cells[x, y] = new TileCell();
                }
            }
        }

        public TileCell this[int x, int y] => _cells[x, y];

        public bool IsInBounds(int x, int y) => x >= 0 && x < Columns && y >= 0 && y < Rows;

        public void SetStackHeight(int x, int y, string tileName, int height)
        {
            if (!IsInBounds(x, y)) return;
            
            var cell = _cells[x, y];
            cell.Blocks.Clear();
            for (int i = 0; i < height; i++)
            {
                cell.Blocks[i] = tileName;
            }
        }

        public static WorldMap GenerateIsland(int columns, int rows, int seed = 42)
        {
            var map = new WorldMap(columns, rows);
            var random = new Random(seed);
            
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    double centerX = x - columns / 2.0;
                    double centerY = y - rows / 2.0;
                    double distance = Math.Sqrt(centerX * centerX + centerY * centerY);
                    double noise = random.NextDouble() * 4;
                    
                    if (distance + noise > columns * 0.42)
                    {
                        map.SetStackHeight(x, y, "Water", 1);
                    }
                    else if (distance + noise > columns * 0.36)
                    {
                        map.SetStackHeight(x, y, "Sand", 1);
                    }
                    else
                    {
                        int randomChance = random.Next(100);
                        if (randomChance < 8)       map.SetStackHeight(x, y, "Stone", random.Next(1, 4));
                        else if (randomChance < 16) map.SetStackHeight(x, y, "Forest", random.Next(1, 3));
                        else if (randomChance < 22) map.SetStackHeight(x, y, "Dirt", random.Next(1, 2));
                        else if (randomChance < 25) map.SetStackHeight(x, y, "Snow", random.Next(2, 4));
                        else                        map.SetStackHeight(x, y, "Grass", random.Next(1, 2));
                    }
                }
            }
            return map;
        }

        public static WorldMap GenerateWasteland(int columns, int rows, int seed = 7)
        {
            var map = new WorldMap(columns, rows);
            var random = new Random(seed);
            
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    int randomChance = random.Next(100);
                    if (randomChance < 30)      map.SetStackHeight(x, y, "Ash", random.Next(1, 2));
                    else if (randomChance < 55) map.SetStackHeight(x, y, "Concrete", random.Next(1, 2));
                    else if (randomChance < 70) map.SetStackHeight(x, y, "Dirt", random.Next(1, 1));
                    else if (randomChance < 80) map.SetStackHeight(x, y, "Rust", random.Next(1, 3));
                    else if (randomChance < 88) map.SetStackHeight(x, y, "Stone", random.Next(1, 4));
                    else                        map.SetStackHeight(x, y, "Ash", 1);
                }
            }
            return map;
        }
    }
}
