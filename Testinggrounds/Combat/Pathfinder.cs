using IsometricWPF.Dwellers;

namespace IsometricWPF.Combat;

public static class Pathfinder {
    public static List<(int x, int y)>? FindPath(
        WorldMap map,
        int startX, int startY,
        int goalX, int goalY,
        IEnumerable<DwellerInstance>? occupants = null,
        DwellerInstance? mover = null) {
        if (!map.IsInBounds(goalX, goalY)) return null;
        if (!IsWalkable(map, goalX, goalY)) return null;


        var blocked = new HashSet<(int, int)>();
        if (occupants != null)
            foreach (var d in occupants)
                if (d != mover && !d.IsDead)
                    blocked.Add((d.TileX, d.TileY));

        var open = new PriorityQueue<Node, float>();
        var closed = new HashSet<(int, int)>();
        var came = new Dictionary<(int, int), (int, int)>();
        var gScore = new Dictionary<(int, int), float>();

        var start = (startX, startY);
        var goal = (goalX, goalY);

        gScore[start] = 0;
        open.Enqueue(new Node(start, 0, Heuristic(start, goal)), 0 + Heuristic(start, goal));

        while (open.Count > 0) {
            var current = open.Dequeue();
            var pos = current.Pos;

            if (pos == goal)
                return ReconstructPath(came, goal);

            if (!closed.Add(pos)) continue;

            foreach (var nb in Neighbours(map, pos, blocked)) {
                if (closed.Contains(nb)) continue;

                var tentative = gScore.GetValueOrDefault(pos, float.MaxValue) + 1f;
                if (tentative < gScore.GetValueOrDefault(nb, float.MaxValue)) {
                    gScore[nb] = tentative;
                    came[nb] = pos;
                    var f = tentative + Heuristic(nb, goal);
                    open.Enqueue(new Node(nb, tentative, f), f);
                }
            }
        }

        return null;
    }


    public static HashSet<(int x, int y)> ReachableTiles(
        WorldMap map,
        int originX, int originY,
        int maxPM,
        IEnumerable<DwellerInstance>? occupants = null,
        DwellerInstance? mover = null) {
        var blocked = new HashSet<(int, int)>();
        if (occupants != null)
            foreach (var d in occupants)
                if (d != mover && !d.IsDead)
                    blocked.Add((d.TileX, d.TileY));

        var reachable = new HashSet<(int, int)>();
        var dist = new Dictionary<(int, int), int> { [(originX, originY)] = 0 };
        var queue = new Queue<(int, int)>();
        queue.Enqueue((originX, originY));

        while (queue.Count > 0) {
            var pos = queue.Dequeue();
            var d = dist[pos];
            if (d >= maxPM) continue;

            foreach (var nb in Neighbours(map, pos, blocked)) {
                if (dist.ContainsKey(nb)) continue;
                dist[nb] = d + 1;
                reachable.Add(nb);
                queue.Enqueue(nb);
            }
        }

        return reachable;
    }


    private static IEnumerable<(int, int)> Neighbours(
        WorldMap map, (int x, int y) pos, HashSet<(int, int)> blocked) {
        (int dx, int dy)[] dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        foreach (var (dx, dy) in dirs) {
            var nb = (pos.x + dx, pos.y + dy);
            if (!map.IsInBounds(nb.Item1, nb.Item2)) continue;
            if (!IsWalkable(map, nb.Item1, nb.Item2)) continue;
            if (blocked.Contains(nb)) continue;
            yield return nb;
        }
    }

    private static bool IsWalkable(WorldMap map, int x, int y) {
        var cell = map[x, y];
        if (cell.Blocks.Count == 0) return false;
        var top = cell.TopBlockName;
        return top != null && (cell.IsWalkableOverride ?? TileRegistry.Get(top).IsWalkable);
    }

    private static float Heuristic((int x, int y) a, (int x, int y) b) {
        return Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
    }

    private static List<(int, int)> ReconstructPath(
        Dictionary<(int, int), (int, int)> came, (int, int) goal) {
        var path = new List<(int, int)>();
        var cur = goal;
        while (came.TryGetValue(cur, out var prev)) {
            path.Add(cur);
            cur = prev;
        }

        path.Reverse();
        return path;
    }

    private record Node((int, int) Pos, float G, float F);
}