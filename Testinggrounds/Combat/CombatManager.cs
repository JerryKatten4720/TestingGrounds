using IsometricWPF.Dwellers;
using IsometricWPF.World;

namespace IsometricWPF.Combat;

public class CombatManager {
    private static readonly Random _rng = new();

    private readonly List<DwellerInstance> _all = new();


    private readonly List<TeamState> _teams = new();
    private int _turnIndex;


    public WeatherSystem? Weather { get; set; }
    public FogOfWarMap? Fog { get; set; }
    public RadiationZone? Radiation { get; set; }
    public RandomEventSystem? RandomEvents { get; set; }


    public IReadOnlyList<TeamState> Teams => _teams;
    public TeamState? ActiveTeam => IsActive && _teams.Count > 0 ? _teams[_turnIndex] : null;
    public bool IsActive { get; private set; }


    public event Action<TeamState>? TurnStarted;
    public event Action<TeamState>? TurnEnded;
    public event Action<TeamState>? TeamEliminated;
    public event Action<TeamState>? VictoryAchieved;
    public event Action<AttackResult>? AttackResolved;
    public event Action<DwellerInstance>? DwellerKilled;
    public event Action<DwellerInstance, int, int>? DwellerMoved;


    public event Action<string>? WorldEventOccurred;


    public event Action<ResourceNode, int>? ResourceHarvested;


    public void StartCombat(IEnumerable<TeamState> teams, IEnumerable<DwellerInstance> dwellers) {
        _teams.Clear();
        _all.Clear();
        _teams.AddRange(teams);
        _all.AddRange(dwellers);

        _turnIndex = 0;
        IsActive = true;

        BeginTurn(_teams[_turnIndex]);
    }

    public void EndCombat() {
        IsActive = false;
    }


    public void EndTurn() {
        if (!IsActive) return;

        TurnEnded?.Invoke(_teams[_turnIndex]);

        var tries = 0;
        do {
            _turnIndex = (_turnIndex + 1) % _teams.Count;
            tries++;
        } while (_teams[_turnIndex].IsEliminated && tries <= _teams.Count);

        BeginTurn(_teams[_turnIndex]);
    }

    private void BeginTurn(TeamState team) {
        team.StartTurn();


        foreach (var d in _all.Where(d => d.TeamId == team.TeamId && !d.IsDead))
            d.ResetMovementPoints();


        if (Radiation != null) {
            var hits = Radiation.ApplyRadiation(_all.Where(d => d.TeamId == team.TeamId && !d.IsDead));
            foreach (var (d, dmg) in hits) {
                WorldEventOccurred?.Invoke($"☢ {d.Data.DisplayName} irradiated! -{dmg} HP");
                if (d.IsDead) {
                    DwellerKilled?.Invoke(d);
                    CheckVictory();
                }
            }
        }


        if (Weather != null && Weather.EnvironmentalDamage > 0) {
            foreach (var d in _all.Where(d => d.TeamId == team.TeamId && !d.IsDead)) {
                d.HP -= Weather.EnvironmentalDamage;
                if (d.HP <= 0) {
                    d.HP = 0;
                    d.IsDead = true;
                    DwellerKilled?.Invoke(d);
                    CheckVictory();
                }
            }

            WorldEventOccurred?.Invoke(
                $"{Weather.DisplayName} deals {Weather.EnvironmentalDamage} dmg to {team.Name}.");
        }


        if (RandomEvents != null) {
            var fired = RandomEvents.Evaluate(_all.Where(d => d.TeamId == team.TeamId));
            foreach (var (_, msg) in fired) {
                WorldEventOccurred?.Invoke(msg);

                foreach (var d in _all.Where(d => d.IsDead && d.TeamId == team.TeamId))
                    DwellerKilled?.Invoke(d);
                CheckVictory();
            }
        }


        Fog?.Recompute(team.TeamId, _all.Where(d => d.TeamId == team.TeamId));

        TurnStarted?.Invoke(team);
    }


    public bool TryMove(DwellerInstance dweller, int toX, int toY, int pathLength) {
        var team = GetTeam(dweller);
        if (team == null || team != ActiveTeam) return false;
        if (dweller.IsDead) return false;
        if (dweller.MovementPoints < pathLength) return false;

        var paCost = team.MovementPACost(dweller);
        if (!team.CanSpend(paCost)) return false;

        team.SpendPA(paCost);
        team.RegisterMove(dweller);
        dweller.MovementPoints -= pathLength;

        dweller.TileX = toX;
        dweller.TileY = toY;


        Fog?.Recompute(team.TeamId, _all.Where(d => d.TeamId == team.TeamId && !d.IsDead));

        DwellerMoved?.Invoke(dweller, toX, toY);
        return true;
    }


    public AttackResult? TryAttack(DwellerInstance attacker, DwellerInstance target, WeaponSlot weapon) {
        var team = GetTeam(attacker);
        if (team == null || team != ActiveTeam) return null;
        if (attacker.IsDead || target.IsDead) return null;
        if (attacker.TeamId == target.TeamId) return null;

        var paCost = PACostForAttack(attacker, weapon);
        if (!team.SpendPA(paCost)) return null;

        var result = ResolveAttack(attacker, target, weapon);
        AttackResolved?.Invoke(result);

        if (result.Hit) {
            target.HP -= result.Damage;
            if (target.HP <= 0) {
                target.HP = 0;
                target.IsDead = true;

                var xp = 20 + target.Level * 5;
                var levelUp = attacker.GainXP(xp);
                if (levelUp)
                    WorldEventOccurred?.Invoke($"⬆ {attacker.Data.DisplayName} levelled up! (Lv {attacker.Level})");

                DwellerKilled?.Invoke(target);
                CheckVictory();
            }
        }

        return result;
    }


    public bool TryHarvest(DwellerInstance dweller, ResourceNode node) {
        var team = GetTeam(dweller);
        if (team == null || team != ActiveTeam) return false;
        if (dweller.IsDead || node.IsDepleted) return false;
        if (!team.SpendPA(2)) return false;

        var amount = node.Harvest();
        ApplyResourceEffect(dweller, node.Type, amount);
        ResourceHarvested?.Invoke(node, amount);

        var xp = 10;
        dweller.GainXP(xp);

        return true;
    }

    private static void ApplyResourceEffect(DwellerInstance d, ResourceType type, int amount) {
        switch (type) {
            case ResourceType.FoodSupply:
                d.HP = Math.Min(d.HP + amount * 3, d.MaxHP);
                break;
            case ResourceType.CleanWater:
                d.HP = Math.Min(d.HP + amount * 2, d.MaxHP);

                break;
            case ResourceType.NukaCola:

                break;
            case ResourceType.ScrapMetal:
            case ResourceType.Caps:
            default:

                break;
        }
    }


    public bool TrySpendRetreatPenalty(DwellerInstance dweller) {
        var team = GetTeam(dweller);
        return team?.SpendPA(1) ?? false;
    }

    public bool IsAdjacentToEnemy(DwellerInstance dweller) {
        return _all.Any(o => !o.IsDead && o.TeamId != dweller.TeamId && ManhattanDistance(dweller, o) == 1);
    }


    public TeamState? GetTeam(DwellerInstance d) {
        return _teams.FirstOrDefault(t => t.TeamId == d.TeamId);
    }

    public TeamState? GetTeamById(int id) {
        return _teams.FirstOrDefault(t => t.TeamId == id);
    }

    public IEnumerable<DwellerInstance> LivingDwellers(int teamId) {
        return _all.Where(d => d.TeamId == teamId && !d.IsDead);
    }


    private int PACostForAttack(DwellerInstance attacker, WeaponSlot weapon) {
        var cost = 2;
        var reduction = attacker.EffectiveA >= 7 ? 1 : 0;
        return Math.Max(1, cost - reduction);
    }

    private AttackResult ResolveAttack(DwellerInstance attacker, DwellerInstance target, WeaponSlot weapon) {
        var hitChance = 0.70 + (attacker.EffectiveP - 5) * 0.03;
        if (Weather != null) hitChance += Weather.HitChanceMod;
        hitChance = Math.Clamp(hitChance, 0.05, 0.97);

        var hit = _rng.NextDouble() < hitChance;
        var isCrit = false;
        var damage = 0;

        if (hit) {
            damage = _rng.Next(2, 7) + attacker.EffectiveS / 2;


            var armor = attacker.EquippedArmor?.DamageReduce ?? 0;
            damage = Math.Max(1, damage - armor);

            var critChance = attacker.EffectiveL * 0.02;
            isCrit = _rng.NextDouble() < critChance;
            if (isCrit) damage = (int)(damage * 1.5);
        }

        return new AttackResult {
            Attacker = attacker,
            Target = target,
            Weapon = weapon,
            Hit = hit,
            IsCrit = isCrit,
            Damage = damage
        };
    }

    private void CheckVictory() {
        foreach (var t in _teams.Where(t => t.IsEliminated).ToList())
            TeamEliminated?.Invoke(t);

        var survivors = _teams.Where(t => !t.IsEliminated).ToList();
        if (survivors.Count == 1) {
            IsActive = false;
            VictoryAchieved?.Invoke(survivors[0]);
        }
    }

    private static int ManhattanDistance(DwellerInstance a, DwellerInstance b) {
        return Math.Abs(a.TileX - b.TileX) + Math.Abs(a.TileY - b.TileY);
    }
}

public enum WeaponSlot {
    Melee,
    Ranged
}

public class AttackResult {
    public DwellerInstance Attacker { get; init; } = null!;
    public DwellerInstance Target { get; init; } = null!;
    public WeaponSlot Weapon { get; init; }
    public bool Hit { get; init; }
    public bool IsCrit { get; init; }
    public int Damage { get; init; }
}