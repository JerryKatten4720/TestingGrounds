using System.Windows.Threading;

namespace IsometricWPF.World;

public class DayNightCycle : IDisposable {
    private readonly DispatcherTimer _timer;

    public DayNightCycle() {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        SecondsRemaining = PhaseDurationSeconds;
    }


    public bool IsNight { get; private set; }
    public bool IsActive { get; private set; }


    public double SecondsRemaining { get; private set; }


    public double PhaseDurationSeconds { get; set; } = 600.0;

    public void Dispose() {
        _timer.Stop();
    }


    public event Action<bool>? PhaseChanged;


    public event Action<double>? Tick;


    public void Start() {
        SecondsRemaining = PhaseDurationSeconds;
        IsActive = true;
        _timer.Start();
    }

    public void Stop() {
        IsActive = false;
        _timer.Stop();
    }

    public void Reset() {
        IsNight = false;
        SecondsRemaining = PhaseDurationSeconds;
        PhaseChanged?.Invoke(false);
    }


    private void OnTick(object? sender, EventArgs e) {
        SecondsRemaining -= 1.0;
        Tick?.Invoke(SecondsRemaining);

        if (SecondsRemaining <= 0) {
            IsNight = !IsNight;
            SecondsRemaining = PhaseDurationSeconds;
            PhaseChanged?.Invoke(IsNight);
        }
    }
}