using System.Windows.Threading;

namespace IsometricWPF.World;

public enum WeatherType {
    Clear,
    Rain,
    Sandstorm,
    Blizzard,
    AcidRain,
    RadStorm
}

public class WeatherSystem : IDisposable {
    private static readonly Random _rng = new();

    private static readonly WeatherType[] _pool = {
        WeatherType.Clear, WeatherType.Clear, WeatherType.Clear,
        WeatherType.Rain,
        WeatherType.Sandstorm,
        WeatherType.Blizzard,
        WeatherType.AcidRain,
        WeatherType.RadStorm
    };

    private readonly DispatcherTimer _timer;

    public WeatherSystem() {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        SecondsRemaining = NextDuration();
    }


    public WeatherType Current { get; private set; } = WeatherType.Clear;
    public bool IsActive { get; private set; }
    public double SecondsRemaining { get; private set; }


    public double HitChanceMod => Current switch {
        WeatherType.Rain => -0.08,
        WeatherType.Sandstorm => -0.15,
        WeatherType.Blizzard => -0.12,
        WeatherType.RadStorm => -0.10,
        _ => 0.00
    };


    public int VisionMod => Current switch {
        WeatherType.Rain => -1,
        WeatherType.Sandstorm => -2,
        WeatherType.Blizzard => -2,
        WeatherType.RadStorm => -2,
        _ => 0
    };


    public int EnvironmentalDamage => Current switch {
        WeatherType.AcidRain => 1,
        WeatherType.RadStorm => 2,
        _ => 0
    };

    public string DisplayName => Current switch {
        WeatherType.Clear => "☀ Clear",
        WeatherType.Rain => "🌧 Rain",
        WeatherType.Sandstorm => "🌪 Sandstorm",
        WeatherType.Blizzard => "❄ Blizzard",
        WeatherType.AcidRain => "☠ Acid Rain",
        WeatherType.RadStorm => "☢ Rad Storm",
        _ => "?"
    };

    public void Dispose() {
        _timer.Stop();
    }


    public event Action<WeatherType>? WeatherChanged;


    public event Action<double>? Tick;


    public void Start() {
        IsActive = true;
        _timer.Start();
    }

    public void Stop() {
        IsActive = false;
        _timer.Stop();
    }


    private void OnTick(object? sender, EventArgs e) {
        SecondsRemaining -= 1.0;
        Tick?.Invoke(SecondsRemaining);

        if (SecondsRemaining <= 0)
            PickNextWeather();
    }

    private void PickNextWeather() {
        var next = _pool[_rng.Next(_pool.Length)];
        SecondsRemaining = NextDuration();

        if (next == Current) return;

        Current = next;
        WeatherChanged?.Invoke(Current);
    }


    private static double NextDuration() {
        return _rng.Next(8 * 60, 18 * 60 + 1);
    }
}