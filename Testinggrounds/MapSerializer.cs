using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using IsometricWPF.Dwellers;

namespace IsometricWPF;

public class SerializedCell {
    public Dictionary<int, string> Blocks { get; set; } = new();
    public List<string> Decors { get; set; } = new();
    public bool? IsWalkableOverride { get; set; }
}

public class SerializedCellEntry {
    public int X { get; set; }
    public int Y { get; set; }
    public SerializedCell Cell { get; set; } = new();
}

public class SerializedTileDef {
    public string Name { get; set; } = string.Empty;
    public string TopColor { get; set; } = "#888888";
    public string LeftColor { get; set; } = "#666666";
    public string RightColor { get; set; } = "#444444";
    public string? TopTexturePath { get; set; }
    public string? LeftTexturePath { get; set; }
    public string? RightTexturePath { get; set; }
    public bool IsCustom { get; set; }
    public bool IsWalkable { get; set; } = true;
}

public class SerializedDweller {
    public string DisplayName { get; set; } = string.Empty;
    public int TileX { get; set; }
    public int TileY { get; set; }
    public int TeamId { get; set; }
}

public class SerializedMap {
    public int Version { get; set; } = 1;
    public int Columns { get; set; }
    public int Rows { get; set; }
    public List<SerializedTileDef> TileDefs { get; set; } = new();
    public List<SerializedCellEntry> Cells { get; set; } = new();
    public List<SerializedDweller> Dwellers { get; set; } = new();
}

public static class MapSerializer {
    private static readonly JsonSerializerOptions _opts = new() {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };


    public static void Export(WorldMap map, IEnumerable<DwellerInstance> dwellers, string path) {
        var data = new SerializedMap { Columns = map.Columns, Rows = map.Rows };


        foreach (var kv in TileRegistry.All) {
            var d = kv.Value;
            data.TileDefs.Add(new SerializedTileDef {
                Name = d.Name,
                IsCustom = d.IsCustom,
                IsWalkable = d.IsWalkable,
                TopColor = Hex(d.DefaultTopColor),
                LeftColor = Hex(d.DefaultLeftColor),
                RightColor = Hex(d.DefaultRightColor),
                TopTexturePath = d.TopTexturePath,
                LeftTexturePath = d.LeftTexturePath,
                RightTexturePath = d.RightTexturePath
            });
        }


        for (var x = 0; x < map.Columns; x++)
        for (var y = 0; y < map.Rows; y++) {
            var c = map[x, y];
            if (c.Blocks.Count == 0 && c.Decors.Count == 0 && c.IsWalkableOverride == null) continue;

            data.Cells.Add(new SerializedCellEntry {
                X = x, Y = y,
                Cell = new SerializedCell {
                    Blocks = new Dictionary<int, string>(c.Blocks),
                    Decors = new List<string>(c.Decors),
                    IsWalkableOverride = c.IsWalkableOverride
                }
            });
        }


        foreach (var dw in dwellers)
            data.Dwellers.Add(new SerializedDweller
                { DisplayName = dw.Data.DisplayName, TileX = dw.TileX, TileY = dw.TileY, TeamId = dw.TeamId });

        File.WriteAllText(path, JsonSerializer.Serialize(data, _opts));
    }


    public static (WorldMap map, List<DwellerInstance> dwellers, bool ok, string? error) Import(string path) {
        try {
            var data = JsonSerializer.Deserialize<SerializedMap>(File.ReadAllText(path), _opts)
                       ?? throw new InvalidDataException("File produced a null map.");


            foreach (var td in data.TileDefs) {
                TileRegistry.RegisterOrUpdate(
                    td.Name, FromHex(td.TopColor), FromHex(td.LeftColor), FromHex(td.RightColor),
                    td.IsCustom, td.IsWalkable);
                var def = TileRegistry.Get(td.Name);
                if (td.TopTexturePath != null) def.SetTopTexture(td.TopTexturePath);
                if (td.LeftTexturePath != null) def.SetLeftTexture(td.LeftTexturePath);
                if (td.RightTexturePath != null) def.SetRightTexture(td.RightTexturePath);
            }


            var map = new WorldMap(data.Columns, data.Rows);
            foreach (var entry in data.Cells ?? new List<SerializedCellEntry>()) {
                if (!map.IsInBounds(entry.X, entry.Y)) continue;
                var cell = map[entry.X, entry.Y];
                foreach (var kvp in entry.Cell.Blocks) cell.Blocks[kvp.Key] = kvp.Value;
                cell.Decors.AddRange(entry.Cell.Decors);
                cell.IsWalkableOverride = entry.Cell.IsWalkableOverride;
            }


            var dwellerList = new List<DwellerInstance>();
            foreach (var sd in data.Dwellers ?? new List<SerializedDweller>()) {
                var dd = DwellerRegistry.FindByDisplayName(sd.DisplayName);
                if (dd != null)
                    dwellerList.Add(new DwellerInstance(dd, sd.TileX, sd.TileY) { TeamId = sd.TeamId });
            }

            return (map, dwellerList, true, null);
        }
        catch (Exception ex) {
            return (null!, null!, false, ex.Message);
        }
    }


    private static string Hex(Color c) {
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static Color FromHex(string s) {
        if (string.IsNullOrWhiteSpace(s)) return Colors.Gray;
        s = s.TrimStart('#');
        return Color.FromRgb(
            Convert.ToByte(s[..2], 16),
            Convert.ToByte(s[2..4], 16),
            Convert.ToByte(s[4..6], 16));
    }
}