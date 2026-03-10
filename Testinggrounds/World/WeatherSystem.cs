using System;
using System.Windows.Threading;

namespace IsometricWPF.World
{
    public enum WeatherType
    {
        Clear,
        Rain,
        Sandstorm,
        Blizzard,
        AcidRain,   // Wasteland flavour
        RadStorm,   // Reduces all vision by 2 in addition to radiation
    }

    /// <summary>
    /// Randomly switches weather every 8–18 real-time minutes.
    /// Each weather type carries optional stat modifiers that CombatManager
    /// can read when resolving attacks or movement.
    /// </summary>
    public class WeatherSystem : IDisposable
    {
        // ── Public surface ────────────────────────────────────────────

        public WeatherType Current       { get; private set; } = WeatherType.Clear;
        public bool        IsActive      { get; private set; } = false;
        public double      SecondsRemaining { get; private set; }

        /// <summary>Fired when the weather type changes. Arg = new weather.</summary>
        public event Action<WeatherType>? WeatherChanged;

        /// <summary>Fired every second for countdown display.</summary>
        public event Action<double>?      Tick;

        // ── Stat modifiers exposed for CombatManager ──────────────────

        /// <summary>Hit-chance modifier for all attacks during this weather (-ve = harder to hit).</summary>
        public double HitChanceMod => Current switch
        {
            WeatherType.Rain      => -0.08,
            WeatherType.Sandstorm => -0.15,
            WeatherType.Blizzard  => -0.12,
            WeatherType.RadStorm  => -0.10,
            _                     =>  0.00,
        };

        /// <summary>Vision radius modifier (additive tiles). Negative = shorter sight.</summary>
        public int VisionMod => Current switch
        {
            WeatherType.Rain      => -1,
            WeatherType.Sandstorm => -2,
            WeatherType.Blizzard  => -2,
            WeatherType.RadStorm  => -2,
            _                     =>  0,
        };

        /// <summary>Per-turn HP damage applied to ALL dwellers outdoors (at start of each team turn).</summary>
        public int EnvironmentalDamage => Current switch
        {
            WeatherType.AcidRain => 1,
            WeatherType.RadStorm => 2,
            _                    => 0,
        };

        public string DisplayName => Current switch
        {
            WeatherType.Clear     => "☀ Clear",
            WeatherType.Rain      => "🌧 Rain",
            WeatherType.Sandstorm => "🌪 Sandstorm",
            WeatherType.Blizzard  => "❄ Blizzard",
            WeatherType.AcidRain  => "☠ Acid Rain",
            WeatherType.RadStorm  => "☢ Rad Storm",
            _                     => "?",
        };

        // ── Internals ─────────────────────────────────────────────────

        private static readonly Random _rng = new();
        private readonly DispatcherTimer _timer;

        private static readonly WeatherType[] _pool =
        {
            WeatherType.Clear, WeatherType.Clear, WeatherType.Clear, // Clear is 3x more likely
            WeatherType.Rain,
            WeatherType.Sandstorm,
            WeatherType.Blizzard,
            WeatherType.AcidRain,
            WeatherType.RadStorm,
        };

        public WeatherSystem()
        {
            _timer         = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick   += OnTick;
            SecondsRemaining = NextDuration();
        }

        // ── Control ───────────────────────────────────────────────────

        public void Start()
        {
            IsActive = true;
            _timer.Start();
        }

        public void Stop()
        {
            IsActive = false;
            _timer.Stop();
        }

        // ── Private ───────────────────────────────────────────────────

        private void OnTick(object? sender, EventArgs e)
        {
            SecondsRemaining -= 1.0;
            Tick?.Invoke(SecondsRemaining);

            if (SecondsRemaining <= 0)
                PickNextWeather();
        }

        private void PickNextWeather()
        {
            var next = _pool[_rng.Next(_pool.Length)];
            SecondsRemaining = NextDuration();

            if (next == Current) return; // keep same weather, just reset timer

            Current = next;
            WeatherChanged?.Invoke(Current);
        }

        /// <summary>Random duration between 8 and 18 minutes (in seconds).</summary>
        private static double NextDuration()
            => _rng.Next(8 * 60, 18 * 60 + 1);

        public void Dispose() => _timer.Stop();
    }
}
