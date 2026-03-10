using IsometricWPF.World;

namespace IsometricWPF;

public class TileCell {
    public Dictionary<int, string> Blocks { get; } = new();
    public List<string> Decors { get; } = new();
    public bool? IsWalkableOverride { get; set; }


    public bool IsRadiationZone { get; set; }


    public ResourceNode? Resource { get; set; }


    public int MaxBlockHeight => Blocks.Count == 0 ? -1 : Blocks.Keys.Max();
    public string? TopBlockName => MaxBlockHeight >= 0 ? Blocks[MaxBlockHeight] : null;


    public void AddBlock(string tileName) {
        var height = 0;
        while (Blocks.ContainsKey(height)) height++;
        if (height < AppConfig.Instance.MaxStackHeight)
            Blocks[height] = tileName;
    }

    public void RemoveBlock() {
        var top = MaxBlockHeight;
        if (top >= 0) Blocks.Remove(top);
    }

    public void AddDecor(string decorName) {
        Decors.Add(decorName);
    }

    public void RemoveDecor(string decorName) {
        Decors.Remove(decorName);
    }

    public void ClearDecors() {
        Decors.Clear();
    }
}

public class WorldMap {
    private readonly TileCell[,] _cells;

    public WorldMap(int columns, int rows) {
        Columns = columns;
        Rows = rows;
        _cells = new TileCell[columns, rows];
        for (var x = 0; x < columns; x++)
        for (var y = 0; y < rows; y++)
            _cells[x, y] = new TileCell();
    }

    public int Columns { get; }
    public int Rows { get; }


    public RadiationZone Radiation { get; } = new();
    public ResourceNodeRegistry Resources { get; } = new();

    public TileCell this[int x, int y] => _cells[x, y];

    public bool IsInBounds(int x, int y) {
        return x >= 0 && x < Columns && y >= 0 && y < Rows;
    }


    public void SetRadiation(int x, int y, bool on) {
        if (!IsInBounds(x, y)) return;
        _cells[x, y].IsRadiationZone = on;
        if (on) Radiation.Add(x, y);
        else Radiation.Remove(x, y);
    }


    public void PlaceResource(int x, int y, ResourceType type, int quantity = 10) {
        if (!IsInBounds(x, y)) return;
        var node = new ResourceNode { TileX = x, TileY = y, Type = type, Quantity = quantity, MaxQuantity = quantity };
        _cells[x, y].Resource = node;
        Resources.Place(node);
    }

    public void RemoveResource(int x, int y) {
        if (!IsInBounds(x, y)) return;
        _cells[x, y].Resource = null;
        Resources.Remove(x, y);
    }


    public void SetStackHeight(int x, int y, string tileName, int height) {
        if (!IsInBounds(x, y)) return;
        var cell = _cells[x, y];
        cell.Blocks.Clear();
        for (var i = 0; i < height; i++)
            cell.Blocks[i] = tileName;
    }


    public static WorldMap GenerateIsland(int columns, int rows, int seed = 42) {
        var map = new WorldMap(columns, rows);
        var random = new Random(seed);
        double halfCols = columns / 2.0, halfRows = rows / 2.0;

        for (var x = 0; x < columns; x++)
        for (var y = 0; y < rows; y++) {
            var dist = Math.Sqrt(Math.Pow(x - halfCols, 2) + Math.Pow(y - halfRows, 2));
            var noise = random.NextDouble() * 4;

            if (dist + noise > columns * 0.42) {
                map.SetStackHeight(x, y, "Water", 1);
            }
            else if (dist + noise > columns * 0.36) {
                map.SetStackHeight(x, y, "Sand", 1);
            }
            else {
                var roll = random.Next(100);
                if (roll < 8) map.SetStackHeight(x, y, "Stone", random.Next(1, 4));
                else if (roll < 16) map.SetStackHeight(x, y, "Forest", random.Next(1, 3));
                else if (roll < 22) map.SetStackHeight(x, y, "Dirt", random.Next(1, 2));
                else if (roll < 25) map.SetStackHeight(x, y, "Snow", random.Next(2, 4));
                else map.SetStackHeight(x, y, "Grass", random.Next(1, 2));
            }
        }

        return map;
    }

    public static WorldMap GenerateWasteland(int columns, int rows, int seed = 7) {
        var map = new WorldMap(columns, rows);
        var random = new Random(seed);

        for (var x = 0; x < columns; x++)
        for (var y = 0; y < rows; y++) {
            var roll = random.Next(100);
            if (roll < 30) map.SetStackHeight(x, y, "Ash", random.Next(1, 2));
            else if (roll < 55) map.SetStackHeight(x, y, "Concrete", random.Next(1, 2));
            else if (roll < 70) map.SetStackHeight(x, y, "Dirt", 1);
            else if (roll < 80) map.SetStackHeight(x, y, "Rust", random.Next(1, 3));
            else if (roll < 88) map.SetStackHeight(x, y, "Stone", random.Next(1, 4));
            else map.SetStackHeight(x, y, "Ash", 1);
        }


        for (var x = 0; x < columns; x++)
        for (var y = 0; y < rows; y++) {
            if (random.Next(100) < 4) map.SetRadiation(x, y, true);
            if (random.Next(100) < 3) map.PlaceResource(x, y, ResourceType.Caps, random.Next(3, 12));
            if (random.Next(100) < 2) map.PlaceResource(x, y, ResourceType.ScrapMetal, random.Next(5, 15));
        }

        return map;
    }
}