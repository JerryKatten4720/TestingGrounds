namespace IsometricWPF.World;

public enum ResourceType {
    ScrapMetal,
    FoodSupply,
    CleanWater,
    NukaCola,
    Caps
}

public class ResourceNode {
    public int TileX { get; set; }
    public int TileY { get; set; }
    public ResourceType Type { get; set; }
    public int Quantity { get; set; } = 10;
    public int MaxQuantity { get; set; } = 10;

    public bool IsDepleted => Quantity <= 0;

    public string Icon => Type switch {
        ResourceType.ScrapMetal => "⚙",
        ResourceType.FoodSupply => "🌿",
        ResourceType.CleanWater => "💧",
        ResourceType.NukaCola => "🥤",
        ResourceType.Caps => "💰",
        _ => "?"
    };


    public int Harvest(int amount = 1) {
        var actual = Math.Min(amount, Quantity);
        Quantity -= actual;
        return actual;
    }

    public void Replenish() {
        Quantity = MaxQuantity;
    }
}

public class ResourceNodeRegistry {
    private readonly Dictionary<(int, int), ResourceNode> _nodes = new();

    public IEnumerable<ResourceNode> All => _nodes.Values;

    public void Place(ResourceNode node) {
        _nodes[(node.TileX, node.TileY)] = node;
    }

    public bool Remove(int x, int y) {
        return _nodes.Remove((x, y));
    }

    public ResourceNode? At(int x, int y) {
        return _nodes.TryGetValue((x, y), out var n) ? n : null;
    }

    public bool HasNode(int x, int y) {
        return _nodes.ContainsKey((x, y));
    }


    public List<ResourceNode> Adjacent(int x, int y) {
        var result = new List<ResourceNode>();
        int[] dx = { 0, 1, -1, 0, 0 };
        int[] dy = { 0, 0, 0, 1, -1 };
        foreach (var (ox, oy) in dx.Zip(dy))
            if (_nodes.TryGetValue((x + ox, y + oy), out var n))
                result.Add(n);
        return result;
    }

    public void Clear() {
        _nodes.Clear();
    }
}