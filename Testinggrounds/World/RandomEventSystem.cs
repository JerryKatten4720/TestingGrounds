using System;
using System.Collections.Generic;
using System.Linq;
using IsometricWPF.Dwellers;

namespace IsometricWPF.World
{
    /// <summary>
    /// Severity of a random event.
    /// </summary>
    public enum EventSeverity { Positive, Neutral, Negative, Critical }

    /// <summary>
    /// A single random event descriptor.
    /// The <see cref="Effect"/> delegate is called with the active team's dwellers
    /// and returns a human-readable result string shown in the notification bar.
    /// </summary>
    public class RandomEvent
    {
        public string        Name        { get; init; } = string.Empty;
        public string        Description { get; init; } = string.Empty;
        public EventSeverity Severity    { get; init; } = EventSeverity.Neutral;
        public double        Probability { get; init; } = 0.05;  // 0–1

        /// <summary>
        /// Executes the event. Receives all active team dwellers; returns the display message.
        /// Null return means the event decided not to fire (e.g. no valid targets).
        /// </summary>
        public Func<IEnumerable<DwellerInstance>, string?>? Effect { get; init; }
    }

    /// <summary>
    /// Rolls random events at the start of each team's turn.
    /// Games can register custom events; a small set of Fallout-flavoured defaults is provided.
    /// </summary>
    public class RandomEventSystem
    {
        private readonly List<RandomEvent> _events = new();
        private static readonly Random     _rng    = new();

        public RandomEventSystem()
        {
            RegisterDefaults();
        }

        // ── Registration ──────────────────────────────────────────────

        public void Register(RandomEvent ev) => _events.Add(ev);

        public void Clear() => _events.Clear();

        // ── Evaluation ────────────────────────────────────────────────

        /// <summary>
        /// Evaluates all registered events against the given dweller list.
        /// Returns all events that actually fired, paired with their result message.
        /// Call once per team turn start.
        /// </summary>
        public List<(RandomEvent ev, string message)> Evaluate(IEnumerable<DwellerInstance> teamDwellers)
        {
            var fired   = new List<(RandomEvent, string)>();
            var dwellers = teamDwellers.ToList();

            foreach (var ev in _events)
            {
                if (_rng.NextDouble() > ev.Probability) continue;
                var result = ev.Effect?.Invoke(dwellers);
                if (result != null) fired.Add((ev, result));
            }

            return fired;
        }

        // ── Built-in events ───────────────────────────────────────────

        private void RegisterDefaults()
        {
            // ── Positive ──────────────────────────────────────────────
            _events.Add(new RandomEvent
            {
                Name        = "Wanderer's Cache",
                Description = "A buried stash is found.",
                Severity    = EventSeverity.Positive,
                Probability = 0.06,
                Effect      = dwellers =>
                {
                    var living = dwellers.Where(d => !d.IsDead).ToList();
                    if (living.Count == 0) return null;
                    var target = living[_rng.Next(living.Count)];
                    target.GainXP(50);
                    return $"✨ {target.Data.DisplayName} found a cache! +50 XP";
                },
            });

            _events.Add(new RandomEvent
            {
                Name        = "Adrenaline Surge",
                Description = "A dweller fights with renewed focus.",
                Severity    = EventSeverity.Positive,
                Probability = 0.05,
                Effect      = dwellers =>
                {
                    var living = dwellers.Where(d => !d.IsDead).ToList();
                    if (living.Count == 0) return null;
                    var target = living[_rng.Next(living.Count)];
                    int heal   = Math.Max(1, target.Data.E / 2);
                    target.HP  = Math.Min(target.HP + heal, target.MaxHP);
                    return $"💉 {target.Data.DisplayName} feels a surge! +{heal} HP";
                },
            });

            // ── Negative ──────────────────────────────────────────────
            _events.Add(new RandomEvent
            {
                Name        = "Radroach Swarm",
                Description = "A swarm bites the weakest dweller.",
                Severity    = EventSeverity.Negative,
                Probability = 0.07,
                Effect      = dwellers =>
                {
                    var target = dwellers
                        .Where(d => !d.IsDead)
                        .OrderBy(d => d.HP)
                        .FirstOrDefault();
                    if (target == null) return null;
                    int dmg = _rng.Next(1, 4);
                    target.HP -= dmg;
                    if (target.HP <= 0) { target.HP = 0; target.IsDead = true; }
                    return $"🪳 Radroach swarm bit {target.Data.DisplayName}! -{dmg} HP";
                },
            });

            _events.Add(new RandomEvent
            {
                Name        = "Radiation Leak",
                Description = "Radiation seeps from the ground.",
                Severity    = EventSeverity.Negative,
                Probability = 0.05,
                Effect      = dwellers =>
                {
                    int affected = 0;
                    foreach (var d in dwellers.Where(d => !d.IsDead))
                    {
                        int dmg = Math.Max(1, 2 - d.EffectiveE / 3);
                        d.HP -= dmg;
                        if (d.HP <= 0) { d.HP = 0; d.IsDead = true; }
                        affected++;
                    }
                    return affected > 0 ? $"☢ Radiation leak! {affected} dweller(s) irradiated." : null;
                },
            });

            // ── Critical ──────────────────────────────────────────────
            _events.Add(new RandomEvent
            {
                Name        = "Nuke Dud",
                Description = "A dormant warhead hisses ominously but doesn't blow.",
                Severity    = EventSeverity.Critical,
                Probability = 0.01,
                Effect      = _ => "☢ A nuke dud is found nearby… it hisses, then goes quiet. Luck is real.",
            });
        }
    }
}
