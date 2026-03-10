using IsometricWPF.Dwellers;

namespace IsometricWPF.World;

public enum TileVisibility {
    Dark = 0,


    Seen = 1,


    Visible = 2
}

public class FogOfWarMap {
    private readonly int _cols, _rows;


    private readonly Dictionary<int, TileVisibility[,]> _teamGrids = new();

    public FogOfWarMap(int columns, int rows) {
        _cols = columns;
        _rows = rows;
    }

    public bool IsNightMode { get; set; } = false;


    public TileVisibility Get(int teamId, int x, int y) {
        if (!_teamGrids.TryGetValue(teamId, out var grid)) return TileVisibility.Dark;
        if (x < 0 || x >= _cols || y < 0 || y >= _rows) return TileVisibility.Dark;
        return grid[x, y];
    }


    public void Recompute(int teamId, IEnumerable<DwellerInstance> friendlies) {
        var grid = EnsureGrid(teamId);


        for (var x = 0; x < _cols; x++)
        for (var y = 0; y < _rows; y++)
            if (grid[x, y] == TileVisibility.Visible)
                grid[x, y] = TileVisibility.Seen;


        foreach (var d in friendlies) {
            if (d.IsDead) continue;
            var radius = VisionRadius(d);
            FloodFillVision(grid, d.TileX, d.TileY, radius);
        }
    }


    public void RecomputeAll(IEnumerable<DwellerInstance> allDwellers) {
        var byTeam = new Dictionary<int, List<DwellerInstance>>();
        foreach (var d in allDwellers) {
            if (!byTeam.TryGetValue(d.TeamId, out var list))
                byTeam[d.TeamId] = list = new List<DwellerInstance>();
            list.Add(d);
        }

        foreach (var kv in byTeam)
            Recompute(kv.Key, kv.Value);
    }


    public int VisionRadius(DwellerInstance d) {
        var base_ = Math.Max(1, d.EffectiveP);
        return IsNightMode ? Math.Max(1, base_ / 2) : base_;
    }


    private TileVisibility[,] EnsureGrid(int teamId) {
        if (!_teamGrids.TryGetValue(teamId, out var grid)) {
            grid = new TileVisibility[_cols, _rows];
            _teamGrids[teamId] = grid;
        }

        return grid;
    }


    private void FloodFillVision(TileVisibility[,] grid, int cx, int cy, int radius) {
        var x0 = Math.Max(0, cx - radius);
        var x1 = Math.Min(_cols - 1, cx + radius);
        var y0 = Math.Max(0, cy - radius);
        var y1 = Math.Min(_rows - 1, cy + radius);

        for (var x = x0; x <= x1; x++)
        for (var y = y0; y <= y1; y++)
            grid[x, y] = TileVisibility.Visible;
    }


    public void Reset(int teamId) {
        _teamGrids.Remove(teamId);
    }

    public void ResetAll() {
        _teamGrids.Clear();
    }
}