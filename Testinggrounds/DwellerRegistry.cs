using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace IsometricWPF.Dwellers
{
    public static class DwellerRegistry
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            IncludeFields            = true,
            PropertyNameCaseInsensitive = true
        };

        private static List<DwellerData> _all = new();
        public  static IReadOnlyList<DwellerData> All => _all;

        // Call once at startup. Looks for dwellers.json next to the exe,
        // or as a WPF pack resource (pack://application:,,,/dwellers.json).
        public static void Initialize(string jsonPathOrNull = null)
        {
            try
            {
                string json;

                if (jsonPathOrNull != null && File.Exists(jsonPathOrNull))
                {
                    json = File.ReadAllText(jsonPathOrNull);
                }
                else
                {
                    // Try beside the exe first
                    string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                    string local  = Path.Combine(exeDir, "dwellers.json");
                    if (File.Exists(local))
                    {
                        json = File.ReadAllText(local);
                    }
                    else
                    {
                        // Fall back to WPF pack resource
                        var uri    = new Uri("pack://application:,,,/dwellers.json");
                        var stream = Application.GetResourceStream(uri)
                            ?? throw new FileNotFoundException("dwellers.json not found. Place it beside the exe or set Build Action to Resource.");
                        using var reader = new StreamReader(stream.Stream);
                        json = reader.ReadToEnd();
                    }
                }

                _all = JsonSerializer.Deserialize<List<DwellerData>>(json, JsonOpts) ?? new();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load dwellers.json:\n{ex.Message}", "Dweller Registry",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                _all = new();
            }
        }

        public static DwellerData GetByName(string firstName) =>
            _all.FirstOrDefault(d => d.FirstName.Equals(firstName, StringComparison.OrdinalIgnoreCase))?.Clone();

        public static DwellerData GetByIndex(int i) => i >= 0 && i < _all.Count ? _all[i].Clone() : null;

        public static DwellerInstance Spawn(string firstName, int tileX, int tileY)
        {
            var data = GetByName(firstName) ?? throw new ArgumentException($"No dweller named {firstName}");
            return new DwellerInstance(data, tileX, tileY);
        }
    }
}
