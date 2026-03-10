using IsometricWPF.Dwellers;

namespace IsometricWPF.Combat;

public class TeamState {
    private readonly HashSet<DwellerInstance> _movedThisTurn = new();

    public TeamState(int teamId, string name) {
        TeamId = teamId;
        Name = name;
        CurrentPA = MaxPA;
    }

    public int TeamId { get; }
    public string Name { get; set; }
    public int MaxPA { get; set; } = 6;
    public int CurrentPA { get; set; }


    public DwellerInstance? Overseer { get; set; }


    public bool IsEliminated => Overseer == null || Overseer.IsDead;


    public bool CanSpend(int cost) {
        return CurrentPA >= cost;
    }


    public bool SpendPA(int cost) {
        if (!CanSpend(cost)) return false;
        CurrentPA -= cost;
        return true;
    }


    public int MovementPACost(DwellerInstance dweller) {
        return _movedThisTurn.Contains(dweller) ? 0 : 1;
    }


    public void RegisterMove(DwellerInstance dweller) {
        _movedThisTurn.Add(dweller);
    }


    public void StartTurn() {
        CurrentPA = MaxPA;
        _movedThisTurn.Clear();
    }
}