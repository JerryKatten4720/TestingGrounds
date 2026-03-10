using IsometricWPF.Dwellers;

namespace IsometricWPF.World;

public class RadiationZone {
    private readonly HashSet<(int x, int y)> _zones = new();

    public IReadOnlySet<(int x, int y)> Zones => _zones;


    public void Add(int x, int y) {
        _zones.Add((x, y));
    }

    public void Remove(int x, int y) {
        _zones.Remove((x, y));
    }

    public bool Has(int x, int y) {
        return _zones.Contains((x, y));
    }

    public void Clear() {
        _zones.Clear();
    }


    public List<(DwellerInstance dweller, int damage)> ApplyRadiation(
        IEnumerable<DwellerInstance> dwellers,
        int baseDamage = 1) {
        var results = new List<(DwellerInstance, int)>();

        foreach (var d in dwellers) {
            if (d.IsDead) continue;
            if (!_zones.Contains((d.TileX, d.TileY))) continue;


            var reduction = d.EffectiveE / 2;
            var damage = Math.Max(1, baseDamage - reduction + 2);
            d.HP -= damage;
            if (d.HP <= 0) {
                d.HP = 0;
                d.IsDead = true;
            }

            results.Add((d, damage));
        }

        return results;
    }
}