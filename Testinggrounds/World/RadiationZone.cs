using System.Collections.Generic;
using IsometricWPF.Dwellers;

namespace IsometricWPF.World
{
    /// <summary>
    /// Tracks which tiles are radiation zones and applies per-turn damage
    /// to dwellers standing on them.
    ///
    /// RadiationZones are placed by the editor (placeable as a cell flag) and
    /// serialised alongside the map. During combat, <see cref="ApplyRadiation"/>
    /// is called once per team turn by CombatManager.
    /// </summary>
    public class RadiationZone
    {
        // ── Storage ───────────────────────────────────────────────────

        private readonly HashSet<(int x, int y)> _zones = new();

        public IReadOnlySet<(int x, int y)> Zones => _zones;

        // ── Editing ───────────────────────────────────────────────────

        public void Add(int x, int y)    => _zones.Add((x, y));
        public void Remove(int x, int y) => _zones.Remove((x, y));
        public bool Has(int x, int y)    => _zones.Contains((x, y));
        public void Clear()              => _zones.Clear();

        // ── Combat application ────────────────────────────────────────

        /// <summary>
        /// Applies radiation damage to every living dweller whose tile is a radiation zone.
        /// Base damage = 1 HP per zone turn; Endurance halves the damage (minimum 1 when hit).
        /// Returns the list of (dweller, damage) pairs for the UI to display.
        /// </summary>
        public List<(DwellerInstance dweller, int damage)> ApplyRadiation(
            IEnumerable<DwellerInstance> dwellers,
            int baseDamage = 1)
        {
            var results = new List<(DwellerInstance, int)>();

            foreach (var d in dwellers)
            {
                if (d.IsDead) continue;
                if (!_zones.Contains((d.TileX, d.TileY))) continue;

                // Endurance reduces radiation: each 2 points of E reduces damage by 1, minimum 1
                int reduction = d.EffectiveE / 2;
                int damage    = System.Math.Max(1, baseDamage - reduction + 2); // +2 base to feel punishing
                d.HP -= damage;
                if (d.HP <= 0) { d.HP = 0; d.IsDead = true; }
                results.Add((d, damage));
            }

            return results;
        }
    }
}
