using IsometricWPF.Dwellers;

namespace IsometricWPF.World;

public enum EventSeverity {
    Positive,
    Neutral,
    Negative,
    Critical
}

public class RandomEvent {
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public EventSeverity Severity { get; init; } = EventSeverity.Neutral;
    public double Probability { get; init; } = 0.05;


    public Func<IEnumerable<DwellerInstance>, string?>? Effect { get; init; }
}

public class RandomEventSystem {
    private static readonly Random _rng = new();
    private readonly List<RandomEvent> _events = new();

    public RandomEventSystem() {
        RegisterDefaults();
    }


    public void Register(RandomEvent ev) {
        _events.Add(ev);
    }

    public void Clear() {
        _events.Clear();
    }


    public List<(RandomEvent ev, string message)> Evaluate(IEnumerable<DwellerInstance> teamDwellers) {
        var fired = new List<(RandomEvent, string)>();
        var dwellers = teamDwellers.ToList();

        foreach (var ev in _events) {
            if (_rng.NextDouble() > ev.Probability) continue;
            var result = ev.Effect?.Invoke(dwellers);
            if (result != null) fired.Add((ev, result));
        }

        return fired;
    }


    private void RegisterDefaults() {
        _events.Add(new RandomEvent {
            Name = "Wanderer's Cache",
            Description = "A buried stash is found.",
            Severity = EventSeverity.Positive,
            Probability = 0.06,
            Effect = dwellers => {
                var living = dwellers.Where(d => !d.IsDead).ToList();
                if (living.Count == 0) return null;
                var target = living[_rng.Next(living.Count)];
                target.GainXP(50);
                return $"✨ {target.Data.DisplayName} found a cache! +50 XP";
            }
        });

        _events.Add(new RandomEvent {
            Name = "Adrenaline Surge",
            Description = "A dweller fights with renewed focus.",
            Severity = EventSeverity.Positive,
            Probability = 0.05,
            Effect = dwellers => {
                var living = dwellers.Where(d => !d.IsDead).ToList();
                if (living.Count == 0) return null;
                var target = living[_rng.Next(living.Count)];
                var heal = Math.Max(1, target.Data.E / 2);
                target.HP = Math.Min(target.HP + heal, target.MaxHP);
                return $"💉 {target.Data.DisplayName} feels a surge! +{heal} HP";
            }
        });


        _events.Add(new RandomEvent {
            Name = "Radroach Swarm",
            Description = "A swarm bites the weakest dweller.",
            Severity = EventSeverity.Negative,
            Probability = 0.07,
            Effect = dwellers => {
                var target = dwellers
                    .Where(d => !d.IsDead)
                    .OrderBy(d => d.HP)
                    .FirstOrDefault();
                if (target == null) return null;
                var dmg = _rng.Next(1, 4);
                target.HP -= dmg;
                if (target.HP <= 0) {
                    target.HP = 0;
                    target.IsDead = true;
                }

                return $"🪳 Radroach swarm bit {target.Data.DisplayName}! -{dmg} HP";
            }
        });

        _events.Add(new RandomEvent {
            Name = "Radiation Leak",
            Description = "Radiation seeps from the ground.",
            Severity = EventSeverity.Negative,
            Probability = 0.05,
            Effect = dwellers => {
                var affected = 0;
                foreach (var d in dwellers.Where(d => !d.IsDead)) {
                    var dmg = Math.Max(1, 2 - d.EffectiveE / 3);
                    d.HP -= dmg;
                    if (d.HP <= 0) {
                        d.HP = 0;
                        d.IsDead = true;
                    }

                    affected++;
                }

                return affected > 0 ? $"☢ Radiation leak! {affected} dweller(s) irradiated." : null;
            }
        });


        _events.Add(new RandomEvent {
            Name = "Nuke Dud",
            Description = "A dormant warhead hisses ominously but doesn't blow.",
            Severity = EventSeverity.Critical,
            Probability = 0.01,
            Effect = _ => "☢ A nuke dud is found nearby… it hisses, then goes quiet. Luck is real."
        });
    }
}