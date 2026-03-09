using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace IsometricWPF.Dwellers
{
    /// <summary>
    /// Loads and exposes the master list of <see cref="DwellerData"/> from dwellers.json.
    /// Always returns clones so callers cannot mutate the master data.
    /// </summary>
    public static class DwellerRegistry
    {
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            IncludeFields               = true,
            PropertyNameCaseInsensitive = true,
        };

        private static List<DwellerData> _all = new();
        public  static IReadOnlyList<DwellerData> All => _all;

        // ── Initialization ────────────────────────────────────────────

        /// <summary>
        /// Loads dwellers.json. Searches beside the executable first, then falls back
        /// to the embedded WPF pack resource.
        /// </summary>
        public static void Initialize(string? jsonPath = null)
        {
            try
            {
                string json;

                if (jsonPath != null && File.Exists(jsonPath))
                {
                    json = File.ReadAllText(jsonPath);
                }
                else
                {
                    string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dwellers.json");
                    if (File.Exists(local))
                    {
                        json = File.ReadAllText(local);
                    }
                    else
                    {
                        var uri    = new Uri("pack://application:,,,/dwellers.json");
                        var stream = Application.GetResourceStream(uri)
                            ?? throw new FileNotFoundException(
                                "dwellers.json not found beside the exe and not embedded as a resource.");
                        using var reader = new StreamReader(stream.Stream);
                        json = reader.ReadToEnd();
                    }
                }

                _all = JsonSerializer.Deserialize<List<DwellerData>>(json, _jsonOpts) ?? new();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not load dwellers.json:\n{ex.Message}",
                    "Dweller Registry", MessageBoxButton.OK, MessageBoxImage.Warning);
                _all = new();
            }
        }

        // ── Lookups ───────────────────────────────────────────────────

        public static DwellerData? GetByIndex(int i) =>
            i >= 0 && i < _all.Count ? _all[i].Clone() : null;

        public static DwellerData? GetByFirstName(string firstName) =>
            _all.FirstOrDefault(d => d.FirstName.Equals(firstName, StringComparison.OrdinalIgnoreCase))?.Clone();

        /// <summary>Finds a dweller whose <see cref="DwellerData.DisplayName"/> matches (used by MapSerializer on import).</summary>
        public static DwellerData? FindByDisplayName(string displayName) =>
            _all.FirstOrDefault(d => d.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase))?.Clone();

        public static DwellerInstance Spawn(string firstName, int tileX, int tileY)
        {
            var data = GetByFirstName(firstName)
                ?? throw new ArgumentException($"No dweller with first name '{firstName}'.");
            return new DwellerInstance(data, tileX, tileY);
        }
    }
}
