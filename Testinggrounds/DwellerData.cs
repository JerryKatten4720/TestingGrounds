using System.Text.Json.Serialization;
using IsometricWPF.Combat;

namespace IsometricWPF.Dwellers;

public enum DwellerRarity {
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4
}

public enum DwellerState {
    Idle,
    Selected,
    Moving,
    Enemy,
    Dead
}

public class Weapon {
    public string Name { get; set; } = "Fists";
    public int MinDamage { get; set; } = 1;
    public int MaxDamage { get; set; } = 3;
    public WeaponSlot Slot { get; set; } = WeaponSlot.Melee;
}

public class Armor {
    public string Name { get; set; } = "None";
    public int DamageReduce { get; set; } = 0;
}

public class Pet {
    public string Name { get; set; } = string.Empty;
    public int BonusS { get; set; }
    public int BonusP { get; set; }
    public int BonusE { get; set; }
}

public enum ItemCategory {
    Consumable,
    Junk,
    Weapon,
    Armor,
    Misc
}

public class InventoryItem {
    public string Name { get; set; } = string.Empty;
    public ItemCategory Category { get; set; } = ItemCategory.Junk;
    public int Quantity { get; set; } = 1;
    public string Icon { get; set; } = "📦";


    public int HealAmount { get; set; } = 0;

    public override string ToString() {
        return Quantity > 1 ? $"{Icon} {Name} ×{Quantity}" : $"{Icon} {Name}";
    }
}

public class DwellerData {
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int S { get; set; }
    public int P { get; set; }
    public int E { get; set; }
    public int C { get; set; }
    public int I { get; set; }
    public int A { get; set; }
    public int L { get; set; }
    public int Rarity { get; set; }
    public string Texture { get; set; } = string.Empty;
    public string Backstory { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";

    [JsonIgnore] public DwellerRarity RarityEnum => (DwellerRarity)Math.Clamp(Rarity, 0, 4);

    public DwellerData Clone() {
        return (DwellerData)MemberwiseClone();
    }
}

public class DwellerInstance {
    public DwellerInstance(DwellerData data, int tileX, int tileY) {
        Data = data;
        TileX = tileX;
        TileY = tileY;
        HP = MaxHP;
        MovementPoints = MaxMovementPoints;
    }

    public DwellerData Data { get; }
    public int TileX { get; set; }
    public int TileY { get; set; }
    public DwellerState State { get; set; } = DwellerState.Idle;
    public int TeamId { get; set; } = 0;


    public int HP { get; set; }
    public int MaxHP => 10 + EffectiveE * 2;
    public bool IsDead { get; set; } = false;


    public int MovementPoints { get; set; }
    public int MaxMovementPoints => Math.Max(2, EffectiveA);


    public int Level { get; set; } = 1;
    public int XP { get; set; }
    public int XPToNext => Level * 100;
    public int PendingSpecialPoints { get; set; }


    public int BonusS { get; set; }
    public int BonusP { get; set; }
    public int BonusE { get; set; }
    public int BonusC { get; set; }
    public int BonusI { get; set; }
    public int BonusA { get; set; }
    public int BonusL { get; set; }


    public int EffectiveS => Data.S + BonusS + (Pet?.BonusS ?? 0);
    public int EffectiveP => Data.P + BonusP + (Pet?.BonusP ?? 0);
    public int EffectiveE => Data.E + BonusE + (Pet?.BonusE ?? 0);
    public int EffectiveC => Data.C + BonusC;
    public int EffectiveI => Data.I + BonusI;
    public int EffectiveA => Data.A + BonusA;
    public int EffectiveL => Data.L + BonusL;


    public Weapon? MeleeWeapon { get; set; }
    public Weapon? RangedWeapon { get; set; }
    public Armor? EquippedArmor { get; set; }
    public Pet? Pet { get; set; }


    public List<InventoryItem> Inventory { get; } = new();


    public bool ShowHpBar { get; set; } = true;


    public void ResetMovementPoints() {
        MovementPoints = MaxMovementPoints;
    }


    public bool GainXP(int amount) {
        var iBonus = Math.Max(0, EffectiveI - 5);
        amount = (int)(amount * (1.0 + iBonus * 0.05));

        XP += amount;
        var levelled = false;
        while (XP >= XPToNext) {
            XP -= XPToNext;
            Level++;
            levelled = true;
            HP = MaxHP;
            if (Level % 5 == 0) PendingSpecialPoints++;
        }

        return levelled;
    }


    public bool SpendSpecialPoint(string stat) {
        if (PendingSpecialPoints <= 0) return false;
        switch (stat.ToUpperInvariant()) {
            case "S": BonusS++; break;
            case "P": BonusP++; break;
            case "E":
                BonusE++;
                HP = Math.Min(HP + 2, MaxHP);
                break;
            case "C": BonusC++; break;
            case "I": BonusI++; break;
            case "A": BonusA++; break;
            case "L": BonusL++; break;
            default: return false;
        }

        PendingSpecialPoints--;
        return true;
    }


    public void PromoteToOverseer() {
        BonusS = Math.Max(0, 8 - Data.S);
        BonusP = Math.Max(0, 8 - Data.P);
        BonusE = Math.Max(0, 8 - Data.E);
        BonusC = Math.Max(0, 8 - Data.C);
        BonusI = Math.Max(0, 8 - Data.I);
        BonusA = Math.Max(0, 8 - Data.A);
        BonusL = Math.Max(0, 8 - Data.L);
        HP = MaxHP;
    }


    public void AddItem(InventoryItem item) {
        var existing = Inventory.Find(i => i.Name == item.Name && i.Category == item.Category);
        if (existing != null) existing.Quantity += item.Quantity;
        else Inventory.Add(item);
    }

    public void RemoveItem(InventoryItem item) {
        item.Quantity--;
        if (item.Quantity <= 0) Inventory.Remove(item);
    }


    public int UseItem(InventoryItem item) {
        if (item.HealAmount <= 0) return -1;
        var healed = Math.Min(item.HealAmount, MaxHP - HP);
        HP += healed;
        RemoveItem(item);
        return healed;
    }
}