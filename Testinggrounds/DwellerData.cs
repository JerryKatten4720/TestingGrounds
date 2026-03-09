using System;
using System.Text.Json.Serialization;

namespace IsometricWPF.Dwellers
{
    public enum DwellerRarity { Common = 0, Uncommon = 1, Rare = 2, Epic = 3, Legendary = 4 }
    public enum DwellerState  { Idle, Selected, Moving, Enemy }

    /// <summary>
    /// Static character sheet loaded from dwellers.json — never mutated at runtime.
    /// </summary>
    public class DwellerData
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName  { get; set; } = string.Empty;

        // SPECIAL stats
        public int S { get; set; }
        public int P { get; set; }
        public int E { get; set; }
        public int C { get; set; }
        public int I { get; set; }
        public int A { get; set; }
        public int L { get; set; }

        public int    Rarity  { get; set; }
        public string Texture { get; set; } = string.Empty;

        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";

        [JsonIgnore]
        public DwellerRarity RarityEnum => (DwellerRarity)Math.Clamp(Rarity, 0, 4);

        public DwellerData Clone() => (DwellerData)MemberwiseClone();
    }


    /// <summary>
    /// Runtime instance of a dweller placed on the map.
    /// Holds mutable position and combat state; the underlying <see cref="Data"/> is read-only.
    /// </summary>
    public class DwellerInstance
    {
        public DwellerData  Data   { get; }
        public int          TileX  { get; set; }
        public int          TileY  { get; set; }
        public DwellerState State  { get; set; } = DwellerState.Idle;
        public int          TeamId { get; set; } = 0;

        /// <summary>Action points remaining this turn. Max derived from Agility (A stat).</summary>
        public int ActionPoints    { get; set; } = 3;
        public int MaxActionPoints => Math.Max(1, Data.A / 2 + 1);

        public DwellerInstance(DwellerData data, int tileX, int tileY)
        {
            Data  = data;
            TileX = tileX;
            TileY = tileY;
            ActionPoints = MaxActionPoints;
        }

        /// <summary>Resets AP to the dweller's maximum (call at start of each turn).</summary>
        public void ResetActionPoints() => ActionPoints = MaxActionPoints;
    }
}
