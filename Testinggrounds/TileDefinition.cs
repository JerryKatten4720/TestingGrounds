using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IsometricWPF
{
    public class TileDefinition
    {
        public string Name              { get; init; }
        public bool   IsCustom          { get; set; }

        public Color  DefaultTopColor   { get; private set; }
        public Color  DefaultLeftColor  { get; private set; }
        public Color  DefaultRightColor { get; private set; }

        public Brush  TopBrush   { get; set; }
        public Brush  LeftBrush  { get; set; }
        public Brush  RightBrush { get; set; }

        public string TopTexturePath   { get; private set; }
        public string LeftTexturePath  { get; private set; }
        public string RightTexturePath { get; private set; }

        public bool   IsWalkable        { get; set; } = true;

        public void InitColors(Color top, Color left, Color right)
        {
            DefaultTopColor   = top;
            DefaultLeftColor  = left;
            DefaultRightColor = right;
            TopBrush   = Frozen(top);
            LeftBrush  = Frozen(left);
            RightBrush = Frozen(right);
        }

        public void SetTopTexture(string path)    { TopTexturePath   = path; TopBrush   = LoadImageBrush(path) ?? TopBrush; }
        public void SetLeftTexture(string path)   { LeftTexturePath  = path; LeftBrush  = LoadImageBrush(path) ?? LeftBrush; }
        public void SetRightTexture(string path)  { RightTexturePath = path; RightBrush = LoadImageBrush(path) ?? RightBrush; }

        public static ImageBrush LoadImageBrush(string path)
        {
            var img = AssetRegistry.GetTexture(path);
            if (img == null) return null;
            
            var brush = new ImageBrush(img);
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }

    public static class TileRegistry
    {
        private static readonly Dictionary<string, TileDefinition> _defs = new();
        public static IReadOnlyDictionary<string, TileDefinition> All => _defs;

        static TileRegistry()
        {
            Register("Grass",    Color.FromRgb(56,  142, 60),  Color.FromRgb(33, 100, 36),  Color.FromRgb(22,  80, 24));
            Register("Dirt",     Color.FromRgb(141, 110, 99),  Color.FromRgb(93,  64, 55),  Color.FromRgb(70,  45, 38));
            Register("Stone",    Color.FromRgb(120, 144, 156), Color.FromRgb(69,  90, 100), Color.FromRgb(50,  68, 78));
            Register("Sand",     Color.FromRgb(255, 241, 118), Color.FromRgb(200, 180, 60), Color.FromRgb(170, 150, 40));
            Register("Water",    Color.FromRgb(21,  101, 192), Color.FromRgb(13,  71, 161), Color.FromRgb(8,   50, 130), isWalkable: false);
            Register("Snow",     Color.FromRgb(236, 239, 241), Color.FromRgb(176, 190, 197), Color.FromRgb(140, 158, 168));
            Register("Forest",   Color.FromRgb(27,   94, 32),  Color.FromRgb(10,  60, 15),  Color.FromRgb(5,   40, 10));
            Register("Ash",      Color.FromRgb(80,   80, 80),  Color.FromRgb(50,  50, 50),  Color.FromRgb(35,  35, 35));
            Register("Concrete", Color.FromRgb(158, 158, 158), Color.FromRgb(97,  97, 97),  Color.FromRgb(70,  70, 70));
            Register("Rust",     Color.FromRgb(183,  84, 28),  Color.FromRgb(130, 50, 10),  Color.FromRgb(100, 35,  5));
        }

        public static void Register(string name, Color top, Color left, Color right, bool isCustom = false, bool isWalkable = true)
        {
            var def = new TileDefinition { Name = name, IsCustom = isCustom, IsWalkable = isWalkable };
            def.InitColors(top, left, right);
            _defs[name] = def;
        }

        public static void RegisterOrUpdate(string name, Color top, Color left, Color right, bool isCustom = false, bool isWalkable = true)
        {
            if (_defs.ContainsKey(name))
            {
                _defs[name].InitColors(top, left, right);
                _defs[name].IsWalkable = isWalkable;
            }
            else Register(name, top, left, right, isCustom, isWalkable);
        }

        public static void Remove(string name)
        {
            if (_defs.TryGetValue(name, out var def) && def.IsCustom) _defs.Remove(name);
        }

        public static TileDefinition Get(string name) =>
            _defs.TryGetValue(name, out var d) ? d : _defs["Grass"];
    }
}
