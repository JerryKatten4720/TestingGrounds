using System;
using System.Collections.Generic;
using System.Linq;
using IsometricWPF.Dwellers;
using IsometricWPF.World;

namespace IsometricWPF.Combat
{
    /// <summary>
    /// Central rules engine for combat.
    /// Phase 2: integrates weather hit-chance modifiers, radiation zone ticks,
    /// environmental damage ticks, resource harvesting, and random event evaluation.
    /// No WPF dependencies — all feedback goes through events.
    /// </summary>
    public class CombatManager
    {
        // ── State ─────────────────────────────────────────────────────
        private readonly List<TeamState>       _teams = new();
        private readonly List<DwellerInstance> _all   = new();
        private int  _turnIndex = 0;
        private bool _active    = false;

        private static readonly Random _rng = new();

        // ── Phase 2 world hooks (optional — set by MainWindow) ────────
        public WeatherSystem?      Weather       { get; set; }
        public FogOfWarMap?        Fog           { get; set; }
        public RadiationZone?      Radiation     { get; set; }  // from WorldMap
        public RandomEventSystem?  RandomEvents  { get; set; }

        // ── Events ────────────────────────────────────────────────────
        public event Action<TeamState>?                  TurnStarted;
        public event Action<TeamState>?                  TurnEnded;
        public event Action<TeamState>?                  TeamEliminated;
        public event Action<TeamState>?                  VictoryAchieved;
        public event Action<AttackResult>?               AttackResolved;
        public event Action<DwellerInstance>?            DwellerKilled;
        public event Action<DwellerInstance, int, int>?  DwellerMoved;

        /// <summary>Fired for each Phase 2 tick event (radiation, weather, random). Arg = display message.</summary>
        public event Action<string>? WorldEventOccurred;

        /// <summary>Fired when a resource is harvested. Args = node, amount gained.</summary>
        public event Action<ResourceNode, int>? ResourceHarvested;

        // ── Read-only surface ─────────────────────────────────────────
        public IReadOnlyList<TeamState> Teams      => _teams;
        public TeamState?               ActiveTeam => _active && _teams.Count > 0 ? _teams[_turnIndex] : null;
        public bool                     IsActive   => _active;

        // ── Setup ─────────────────────────────────────────────────────

        public void StartCombat(IEnumerable<TeamState> teams, IEnumerable<DwellerInstance> dwellers)
        {
            _teams.Clear();
            _all.Clear();
            _teams.AddRange(teams);
            _all.AddRange(dwellers);

            _turnIndex = 0;
            _active    = true;

            BeginTurn(_teams[_turnIndex]);
        }

        public void EndCombat() => _active = false;

        // ── Turn flow ─────────────────────────────────────────────────

        public void EndTurn()
        {
            if (!_active) return;

            TurnEnded?.Invoke(_teams[_turnIndex]);

            int tries = 0;
            do
            {
                _turnIndex = (_turnIndex + 1) % _teams.Count;
                tries++;
            }
            while (_teams[_turnIndex].IsEliminated && tries <= _teams.Count);

            BeginTurn(_teams[_turnIndex]);
        }

        private void BeginTurn(TeamState team)
        {
            team.StartTurn();

            // Restore PM for every living dweller on this team
            foreach (var d in _all.Where(d => d.TeamId == team.TeamId && !d.IsDead))
                d.ResetMovementPoints();

            // ── Phase 2: turn-start world effects ─────────────────────

            // 1. Radiation zone damage
            if (Radiation != null)
            {
                var hits = Radiation.ApplyRadiation(_all.Where(d => d.TeamId == team.TeamId && !d.IsDead));
                foreach (var (d, dmg) in hits)
                {
                    WorldEventOccurred?.Invoke($"☢ {d.Data.DisplayName} irradiated! -{dmg} HP");
                    if (d.IsDead) { DwellerKilled?.Invoke(d); CheckVictory(); }
                }
            }

            // 2. Environmental weather damage (acid rain / rad storm)
            if (Weather != null && Weather.EnvironmentalDamage > 0)
            {
                foreach (var d in _all.Where(d => d.TeamId == team.TeamId && !d.IsDead))
                {
                    d.HP -= Weather.EnvironmentalDamage;
                    if (d.HP <= 0) { d.HP = 0; d.IsDead = true; DwellerKilled?.Invoke(d); CheckVictory(); }
                }
                WorldEventOccurred?.Invoke($"{Weather.DisplayName} deals {Weather.EnvironmentalDamage} dmg to {team.Name}.");
            }

            // 3. Random events
            if (RandomEvents != null)
            {
                var fired = RandomEvents.Evaluate(_all.Where(d => d.TeamId == team.TeamId));
                foreach (var (_, msg) in fired)
                {
                    WorldEventOccurred?.Invoke(msg);
                    // Check for deaths caused by events
                    foreach (var d in _all.Where(d => d.IsDead && d.TeamId == team.TeamId))
                        DwellerKilled?.Invoke(d);
                    CheckVictory();
                }
            }

            // 4. Recompute fog for this team
            Fog?.Recompute(team.TeamId, _all.Where(d => d.TeamId == team.TeamId));

            TurnStarted?.Invoke(team);
        }

        // ── Movement ─────────────────────────────────────────────────

        public bool TryMove(DwellerInstance dweller, int toX, int toY, int pathLength)
        {
            var team = GetTeam(dweller);
            if (team == null || team != ActiveTeam) return false;
            if (dweller.IsDead) return false;
            if (dweller.MovementPoints < pathLength) return false;

            int paCost = team.MovementPACost(dweller);
            if (!team.CanSpend(paCost)) return false;

            team.SpendPA(paCost);
            team.RegisterMove(dweller);
            dweller.MovementPoints -= pathLength;

            dweller.TileX = toX;
            dweller.TileY = toY;

            // Fog: after moving, recompute visibility for this team
            Fog?.Recompute(team.TeamId, _all.Where(d => d.TeamId == team.TeamId && !d.IsDead));

            DwellerMoved?.Invoke(dweller, toX, toY);
            return true;
        }

        // ── Attack ────────────────────────────────────────────────────

        public AttackResult? TryAttack(DwellerInstance attacker, DwellerInstance target, WeaponSlot weapon)
        {
            var team = GetTeam(attacker);
            if (team == null || team != ActiveTeam) return null;
            if (attacker.IsDead || target.IsDead)   return null;
            if (attacker.TeamId == target.TeamId)    return null;

            int paCost = PACostForAttack(attacker, weapon);
            if (!team.SpendPA(paCost)) return null;

            var result = ResolveAttack(attacker, target, weapon);
            AttackResolved?.Invoke(result);

            if (result.Hit)
            {
                target.HP -= result.Damage;
                if (target.HP <= 0)
                {
                    target.HP = 0; target.IsDead = true;
                    // XP reward to attacker
                    int xp = 20 + target.Level * 5;
                    bool levelUp = attacker.GainXP(xp);
                    if (levelUp) WorldEventOccurred?.Invoke($"⬆ {attacker.Data.DisplayName} levelled up! (Lv {attacker.Level})");

                    DwellerKilled?.Invoke(target);
                    CheckVictory();
                }
            }

            return result;
        }

        // ── Resource harvesting ───────────────────────────────────────

        /// <summary>
        /// Active dweller loots the resource node on or adjacent to their current tile.
        /// Costs 2 PA. Returns false if nothing to loot or insufficient PA.
        /// </summary>
        public bool TryHarvest(DwellerInstance dweller, ResourceNode node)
        {
            var team = GetTeam(dweller);
            if (team == null || team != ActiveTeam) return false;
            if (dweller.IsDead || node.IsDepleted)  return false;
            if (!team.SpendPA(2)) return false;

            int amount = node.Harvest(1);
            ApplyResourceEffect(dweller, node.Type, amount);
            ResourceHarvested?.Invoke(node, amount);

            int xp = 10;
            dweller.GainXP(xp);

            return true;
        }

        private static void ApplyResourceEffect(DwellerInstance d, ResourceType type, int amount)
        {
            switch (type)
            {
                case ResourceType.FoodSupply:
                    d.HP = Math.Min(d.HP + amount * 3, d.MaxHP);
                    break;
                case ResourceType.CleanWater:
                    d.HP = Math.Min(d.HP + amount * 2, d.MaxHP);
                    // Temporary E boost is cosmetic-only here; could add BonusE with an expiry system
                    break;
                case ResourceType.NukaCola:
                    // Restores the team's PA by 1 (handled via the team object the caller owns)
                    break;
                case ResourceType.ScrapMetal:
                case ResourceType.Caps:
                default:
                    // Stored in team inventory (future feature); no immediate stat effect
                    break;
            }
        }

        // ── Retreat penalty ───────────────────────────────────────────

        public bool TrySpendRetreatPenalty(DwellerInstance dweller)
        {
            var team = GetTeam(dweller);
            return team?.SpendPA(1) ?? false;
        }

        public bool IsAdjacentToEnemy(DwellerInstance dweller)
            => _all.Any(o => !o.IsDead && o.TeamId != dweller.TeamId && ManhattanDistance(dweller, o) == 1);

        // ── Queries ───────────────────────────────────────────────────

        public TeamState? GetTeam(DwellerInstance d)
            => _teams.FirstOrDefault(t => t.TeamId == d.TeamId);

        public TeamState? GetTeamById(int id)
            => _teams.FirstOrDefault(t => t.TeamId == id);

        public IEnumerable<DwellerInstance> LivingDwellers(int teamId)
            => _all.Where(d => d.TeamId == teamId && !d.IsDead);

        // ── Internals ─────────────────────────────────────────────────

        private int PACostForAttack(DwellerInstance attacker, WeaponSlot weapon)
        {
            int cost      = 2;
            int reduction = attacker.EffectiveA >= 7 ? 1 : 0;
            return Math.Max(1, cost - reduction);
        }

        private AttackResult ResolveAttack(DwellerInstance attacker, DwellerInstance target, WeaponSlot weapon)
        {
            // Base hit chance from PER, modified by weather
            double hitChance = 0.70 + (attacker.EffectiveP - 5) * 0.03;
            if (Weather != null) hitChance += Weather.HitChanceMod;
            hitChance = Math.Clamp(hitChance, 0.05, 0.97);

            bool hit    = _rng.NextDouble() < hitChance;
            bool isCrit = false;
            int  damage = 0;

            if (hit)
            {
                damage = _rng.Next(2, 7) + attacker.EffectiveS / 2;

                // Armor reduction
                int armor = attacker.EquippedArmor?.DamageReduce ?? 0;
                damage = Math.Max(1, damage - armor);

                double critChance = attacker.EffectiveL * 0.02;
                isCrit = _rng.NextDouble() < critChance;
                if (isCrit) damage = (int)(damage * 1.5);
            }

            return new AttackResult
            {
                Attacker = attacker,
                Target   = target,
                Weapon   = weapon,
                Hit      = hit,
                IsCrit   = isCrit,
                Damage   = damage,
            };
        }

        private void CheckVictory()
        {
            foreach (var t in _teams.Where(t => t.IsEliminated).ToList())
                TeamEliminated?.Invoke(t);

            var survivors = _teams.Where(t => !t.IsEliminated).ToList();
            if (survivors.Count == 1)
            {
                _active = false;
                VictoryAchieved?.Invoke(survivors[0]);
            }
        }

        private static int ManhattanDistance(DwellerInstance a, DwellerInstance b)
            => Math.Abs(a.TileX - b.TileX) + Math.Abs(a.TileY - b.TileY);
    }

    // ── Supporting types ──────────────────────────────────────────────────────

    public enum WeaponSlot { Melee, Ranged }

    public class AttackResult
    {
        public DwellerInstance Attacker { get; init; } = null!;
        public DwellerInstance Target   { get; init; } = null!;
        public WeaponSlot      Weapon   { get; init; }
        public bool            Hit      { get; init; }
        public bool            IsCrit   { get; init; }
        public int             Damage   { get; init; }
    }
}
