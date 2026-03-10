using System.Collections.Generic;
using System.Linq;

namespace IsometricWPF.Combat
{
    /// <summary>
    /// Runtime state for one team during a combat session.
    /// PA (Points d'Action) are shared across the whole team.
    /// PM (Points de Mouvement) are per-dweller and stored on DwellerInstance.
    /// </summary>
    public class TeamState
    {
        public int    TeamId      { get; }
        public string Name        { get; set; }
        public int    MaxPA       { get; set; } = 6;
        public int    CurrentPA   { get; set; }

        /// <summary>
        /// The dweller designated as this team's Overseer.
        /// Eliminating the Overseer ends the game for that team.
        /// Overseer stats are set to 8 across all SPECIAL at game start.
        /// </summary>
        public Dwellers.DwellerInstance? Overseer { get; set; }

        /// <summary>Tracks which dwellers have already cost 1 PA for movement this turn.</summary>
        private readonly HashSet<Dwellers.DwellerInstance> _movedThisTurn = new();

        public TeamState(int teamId, string name)
        {
            TeamId    = teamId;
            Name      = name;
            CurrentPA = MaxPA;
        }

        // ── PA helpers ────────────────────────────────────────────────

        public bool CanSpend(int cost) => CurrentPA >= cost;

        /// <summary>
        /// Spends PA. Returns false and does nothing if insufficient.
        /// </summary>
        public bool SpendPA(int cost)
        {
            if (!CanSpend(cost)) return false;
            CurrentPA -= cost;
            return true;
        }

        // ── Movement PA cost (1 PA once per dweller per turn) ─────────

        /// <summary>
        /// Returns the PA cost of moving <paramref name="dweller"/> this turn.
        /// First move of a dweller costs 1 PA; subsequent moves on the same turn are free (PA-wise).
        /// Always costs PM on the dweller itself.
        /// </summary>
        public int MovementPACost(Dwellers.DwellerInstance dweller)
            => _movedThisTurn.Contains(dweller) ? 0 : 1;

        /// <summary>Records that this dweller has moved at least once this turn.</summary>
        public void RegisterMove(Dwellers.DwellerInstance dweller)
            => _movedThisTurn.Add(dweller);

        // ── Turn lifecycle ────────────────────────────────────────────

        public void StartTurn()
        {
            CurrentPA = MaxPA;
            _movedThisTurn.Clear();
        }

        // ── Victory check ─────────────────────────────────────────────

        public bool IsEliminated => Overseer == null || Overseer.IsDead;
    }
}
