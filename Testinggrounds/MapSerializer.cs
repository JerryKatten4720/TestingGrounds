using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using IsometricWPF.Dwellers;

namespace IsometricWPF
{
    public class SerializedCell
    {
        public Dictionary<int, string> Blocks { get; set; } = new();
        public List<string> Decors { get; set; } = new();
        public bool? IsWalkableOverride { get; set; }
    }

    public class SerializedTileDef
    {
        public string Name             { get; set; }
        public string TopColor         { get; set; }
        public string LeftColor        { get; set; }
        public string RightColor       { get; set; }
        public string TopTexturePath   { get; set; }
        public string LeftTexturePath  { get; set; }
        public string RightTexturePath { get; set; }
        public bool   IsCustom         { get; set; }
        public bool   IsWalkable       { get; set; } = true;
    }

    public class SerializedDweller
    {
        public string DisplayName { get; set; }
        public int    TileX       { get; set; }
        public int    TileY       { get; set; }
        public int    TeamId      { get; set; }
    }

    public class SerializedMap
    {
        public int                      Columns   { get; set; }
        public int                      Rows      { get; set; }
        public List<SerializedTileDef>  TileDefs  { get; set; } = new();
        public SerializedCell[,]        Cells     { get; set; }
        public List<SerializedDweller>  Dwellers  { get; set; } = new();
    }

    public static class MapSerializer
    {
        private static readonly JsonSerializerOptions Opts = new()
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static void Export(WorldMap map, IEnumerable<DwellerInstance> dwellers, string path)
        {
            var data = new SerializedMap { Columns = map.Columns, Rows = map.Rows, Cells = new SerializedCell[map.Columns, map.Rows] };

            foreach (var kv in TileRegistry.All)
            {
                var d = kv.Value;
                data.TileDefs.Add(new SerializedTileDef
                {
                    Name = d.Name, IsCustom = d.IsCustom, IsWalkable = d.IsWalkable,
                    TopColor = Hex(d.DefaultTopColor), LeftColor = Hex(d.DefaultLeftColor), RightColor = Hex(d.DefaultRightColor),
                    TopTexturePath = d.TopTexturePath, LeftTexturePath = d.LeftTexturePath, RightTexturePath = d.RightTexturePath
                });
            }

            for (int x = 0; x < map.Columns; x++)
                for (int y = 0; y < map.Rows; y++)
                {
                    var c = map[x, y];
                    data.Cells[x, y] = new SerializedCell
                    {
                        Blocks = new Dictionary<int, string>(c.Blocks),
                        Decors = new List<string>(c.Decors),
                        IsWalkableOverride = c.IsWalkableOverride
                    };
                }

            foreach (var dw in dwellers)
                data.Dwellers.Add(new SerializedDweller { DisplayName = dw.Data.DisplayName, TileX = dw.TileX, TileY = dw.TileY, TeamId = dw.TeamId });

            File.WriteAllText(path, JsonSerializer.Serialize(data, Opts));
        }

        public static (WorldMap map, List<DwellerInstance> dwellers, bool ok, string error) Import(string path)
        {
            try
            {
                var data = JsonSerializer.Deserialize<SerializedMap>(File.ReadAllText(path), Opts);

                foreach (var td in data.TileDefs)
                {
                    TileRegistry.RegisterOrUpdate(td.Name, FromHex(td.TopColor), FromHex(td.LeftColor), FromHex(td.RightColor), td.IsCustom, td.IsWalkable);
                    var def = TileRegistry.Get(td.Name);
                    if (td.TopTexturePath   != null) def.SetTopTexture(td.TopTexturePath);
                    if (td.LeftTexturePath  != null) def.SetLeftTexture(td.LeftTexturePath);
                    if (td.RightTexturePath != null) def.SetRightTexture(td.RightTexturePath);
                }

                var map = new WorldMap(data.Columns, data.Rows);
                for (int x = 0; x < data.Columns; x++)
                    for (int y = 0; y < data.Rows; y++)
                    {
                        var sc = data.Cells?[x, y];
                        if (sc == null) continue;
                        var cell = map[x, y];
                        foreach (var kvp in sc.Blocks) cell.Blocks[kvp.Key] = kvp.Value;
                        cell.Decors.AddRange(sc.Decors);
                        cell.IsWalkableOverride = sc.IsWalkableOverride;
                    }

                var dwellerList = new List<DwellerInstance>();
                foreach (var sd in data.Dwellers ?? new())
                {
                    var dd = DwellerRegistry.All.FirstOrDefault(d => d.DisplayName == sd.DisplayName);
                    if (dd != null) dwellerList.Add(new DwellerInstance(dd, sd.TileX, sd.TileY) { TeamId = sd.TeamId });
                }

                return (map, dwellerList, true, null);
            }
            catch (Exception ex) { return (null, null, false, ex.Message); }
        }

        private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        private static Color FromHex(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Colors.Gray;
            s = s.TrimStart('#');
            return Color.FromRgb(Convert.ToByte(s[0..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16));
        }
    }
}
