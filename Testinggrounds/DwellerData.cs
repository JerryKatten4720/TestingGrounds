using System.Text.Json.Serialization;

namespace IsometricWPF.Dwellers
{
    public enum DwellerRarity { Common = 0, Uncommon = 1, Rare = 2, Epic = 3, Legendary = 4 }
    public enum DwellerState  { Idle, Selected, Moving, Enemy }

    public class DwellerData
    {
        public string FirstName { get; set; }
        public string LastName  { get; set; }
        public int    S         { get; set; }
        public int    P         { get; set; }
        public int    E         { get; set; }
        public int    C         { get; set; }
        public int    I         { get; set; }
        public int    A         { get; set; }
        public int    L         { get; set; }
        public int    Rarity    { get; set; }
        public string Texture   { get; set; }

        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(LastName)
            ? FirstName
            : $"{FirstName} {LastName}";

        [JsonIgnore]
        public DwellerRarity RarityEnum => (DwellerRarity)System.Math.Clamp(Rarity, 0, 4);

        public DwellerData Clone() => (DwellerData)MemberwiseClone();
    }

    // Runtime instance placed on the map — wraps DwellerData with positional state
    public class DwellerInstance
    {
        public DwellerData Data      { get; }
        public int          TileX    { get; set; }
        public int          TileY    { get; set; }
        public DwellerState State    { get; set; } = DwellerState.Idle;
        public int          TeamId   { get; set; } = 0;
        public int          ActionPoints { get; set; } = 3;

        public DwellerInstance(DwellerData data, int tileX, int tileY)
        {
            Data  = data;
            TileX = tileX;
            TileY = tileY;
        }
    }
}
