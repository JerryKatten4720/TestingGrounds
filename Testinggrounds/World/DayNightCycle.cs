using System;
using System.Windows.Threading;

namespace IsometricWPF.World
{
    /// <summary>
    /// Drives the day/night cycle.
    /// Every 10 real-time minutes the phase flips between Day and Night.
    /// Raises <see cref="PhaseChanged"/> for any subscriber (renderer, fog-of-war, UI).
    /// </summary>
    public class DayNightCycle : IDisposable
    {
        // ── Public surface ────────────────────────────────────────────

        public bool  IsNight     { get; private set; } = false;
        public bool  IsActive    { get; private set; } = false;

        /// <summary>How many seconds remain in the current phase.</summary>
        public double SecondsRemaining { get; private set; }

        /// <summary>Fired on the UI thread whenever the phase changes.</summary>
        public event Action<bool>? PhaseChanged;     // arg = isNight

        /// <summary>Fired every tick (once per second) so the UI can update a countdown.</summary>
        public event Action<double>? Tick;           // arg = secondsRemaining

        // ── Configuration ─────────────────────────────────────────────

        /// <summary>Duration of each phase in seconds. Default = 600 (10 minutes).</summary>
        public double PhaseDurationSeconds { get; set; } = 600.0;

        // ── Internals ─────────────────────────────────────────────────

        private readonly DispatcherTimer _timer;

        public DayNightCycle()
        {
            _timer          = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick    += OnTick;
            SecondsRemaining = PhaseDurationSeconds;
        }

        // ── Control ───────────────────────────────────────────────────

        public void Start()
        {
            SecondsRemaining = PhaseDurationSeconds;
            IsActive         = true;
            _timer.Start();
        }

        public void Stop()
        {
            IsActive = false;
            _timer.Stop();
        }

        public void Reset()
        {
            IsNight          = false;
            SecondsRemaining = PhaseDurationSeconds;
            PhaseChanged?.Invoke(false);
        }

        // ── Private ───────────────────────────────────────────────────

        private void OnTick(object? sender, EventArgs e)
        {
            SecondsRemaining -= 1.0;
            Tick?.Invoke(SecondsRemaining);

            if (SecondsRemaining <= 0)
            {
                IsNight          = !IsNight;
                SecondsRemaining = PhaseDurationSeconds;
                PhaseChanged?.Invoke(IsNight);
            }
        }

        public void Dispose() => _timer.Stop();
    }
}
