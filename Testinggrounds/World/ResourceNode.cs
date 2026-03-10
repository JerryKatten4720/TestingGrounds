using System.Collections.Generic;

namespace IsometricWPF.World
{
    public enum ResourceType
    {
        ScrapMetal,  // ⚙  Crafting / trading
        FoodSupply,  // 🌿  Heals dwellers
        CleanWater,  // 💧  Boosts Endurance temporarily
        NukaCola,    // 🥤  Restores AP on use
        Caps,        // 💰  Currency / revive cost
    }

    /// <summary>
    /// A harvestable node on the map, analogous to mines/trees in AoE2.
    /// Placed via the editor. Dwellers adjacent to (or on) the node can
    /// harvest 1 unit per PA spent via <see cref="CombatManager"/> action routing.
    /// Nodes do NOT deplete over time; they are finite but replenish each new game.
    /// </summary>
    public class ResourceNode
    {
        public int          TileX        { get; set; }
        public int          TileY        { get; set; }
        public ResourceType Type         { get; set; }
        public int          Quantity     { get; set; } = 10;
        public int          MaxQuantity  { get; set; } = 10;

        public bool IsDepleted => Quantity <= 0;

        public string Icon => Type switch
        {
            ResourceType.ScrapMetal => "⚙",
            ResourceType.FoodSupply => "🌿",
            ResourceType.CleanWater => "💧",
            ResourceType.NukaCola   => "🥤",
            ResourceType.Caps       => "💰",
            _                       => "?",
        };

        /// <summary>
        /// Attempts to harvest <paramref name="amount"/> units.
        /// Returns the amount actually harvested (may be less if near-depleted).
        /// </summary>
        public int Harvest(int amount = 1)
        {
            int actual = System.Math.Min(amount, Quantity);
            Quantity  -= actual;
            return actual;
        }

        public void Replenish() => Quantity = MaxQuantity;
    }


    /// <summary>
    /// World-level registry of all resource nodes, indexed by tile position for fast lookup.
    /// </summary>
    public class ResourceNodeRegistry
    {
        private readonly Dictionary<(int, int), ResourceNode> _nodes = new();

        public IEnumerable<ResourceNode> All => _nodes.Values;

        public void Place(ResourceNode node)
            => _nodes[(node.TileX, node.TileY)] = node;

        public bool Remove(int x, int y)
            => _nodes.Remove((x, y));

        public ResourceNode? At(int x, int y)
            => _nodes.TryGetValue((x, y), out var n) ? n : null;

        public bool HasNode(int x, int y)
            => _nodes.ContainsKey((x, y));

        /// <summary>Returns all nodes within Manhattan distance 1 of (x,y) — harvestable range.</summary>
        public List<ResourceNode> Adjacent(int x, int y)
        {
            var result = new List<ResourceNode>();
            int[] dx = { 0, 1, -1, 0,  0 };
            int[] dy = { 0, 0,  0, 1, -1 };
            foreach (var (ox, oy) in System.Linq.Enumerable.Zip(dx, dy))
                if (_nodes.TryGetValue((x + ox, y + oy), out var n))
                    result.Add(n);
            return result;
        }

        public void Clear() => _nodes.Clear();
    }
}
