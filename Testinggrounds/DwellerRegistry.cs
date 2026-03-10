using System.IO;
using System.Text.Json;
using System.Windows;

namespace IsometricWPF.Dwellers;

public static class DwellerRegistry {
    private static readonly JsonSerializerOptions _jsonOpts = new() {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true
    };

    private static List<DwellerData> _all = new();
    public static IReadOnlyList<DwellerData> All => _all;


    public static void Initialize(string? jsonPath = null) {
        try {
            string json;

            if (jsonPath != null && File.Exists(jsonPath)) {
                json = File.ReadAllText(jsonPath);
            }
            else {
                var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dwellers.json");
                if (File.Exists(local)) {
                    json = File.ReadAllText(local);
                }
                else {
                    var uri = new Uri("pack://application:,,,/dwellers.json");
                    var stream = Application.GetResourceStream(uri)
                                 ?? throw new FileNotFoundException(
                                     "dwellers.json not found beside the exe and not embedded as a resource.");
                    using var reader = new StreamReader(stream.Stream);
                    json = reader.ReadToEnd();
                }
            }

            _all = JsonSerializer.Deserialize<List<DwellerData>>(json, _jsonOpts) ?? new List<DwellerData>();
        }
        catch (Exception ex) {
            MessageBox.Show(
                $"Could not load dwellers.json:\n{ex.Message}",
                "Dweller Registry", MessageBoxButton.OK, MessageBoxImage.Warning);
            _all = new List<DwellerData>();
        }
    }


    public static DwellerData? GetByIndex(int i) {
        return i >= 0 && i < _all.Count ? _all[i].Clone() : null;
    }

    public static DwellerData? GetByFirstName(string firstName) {
        return _all.FirstOrDefault(d => d.FirstName.Equals(firstName, StringComparison.OrdinalIgnoreCase))?.Clone();
    }


    public static DwellerData? FindByDisplayName(string displayName) {
        return _all.FirstOrDefault(d => d.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase))?.Clone();
    }

    public static DwellerInstance Spawn(string firstName, int tileX, int tileY) {
        var data = GetByFirstName(firstName)
                   ?? throw new ArgumentException($"No dweller with first name '{firstName}'.");
        return new DwellerInstance(data, tileX, tileY);
    }
}