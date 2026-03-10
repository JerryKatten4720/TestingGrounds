using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using IsometricWPF.Dwellers;

namespace IsometricWPF.UI;

/// <summary>
///     Modal dialog for spending accumulated SPECIAL points after levelling up.
///     Shows current effective stats and lets the player add exactly
///     <see cref="DwellerInstance.PendingSpecialPoints" /> points across the 7 stats.
/// </summary>
public partial class SpecialAllocationDialog : Window {
    private readonly DwellerInstance _dweller;
    private int _remaining;

    public SpecialAllocationDialog(DwellerInstance dweller) {
        _dweller = dweller;
        _remaining = dweller.PendingSpecialPoints;
        InitializeComponent();

        TitleLabel.Text = $"⬆  {dweller.Data.DisplayName}  —  Level {dweller.Level}";
        SubtitleLabel.Text = $"Allocate {_remaining} SPECIAL point{(_remaining == 1 ? "" : "s")}";
        PointsLabel.Text = _remaining.ToString();

        Rows.Add(new StatRow("S", "Strength", "Melee damage", dweller.EffectiveS));
        Rows.Add(new StatRow("P", "Perception", "Hit chance & vision", dweller.EffectiveP));
        Rows.Add(new StatRow("E", "Endurance", "Max HP & rad resist", dweller.EffectiveE));
        Rows.Add(new StatRow("C", "Charisma", "Team PA pool bonus", dweller.EffectiveC));
        Rows.Add(new StatRow("I", "Intelligence", "XP bonus per kill", dweller.EffectiveI));
        Rows.Add(new StatRow("A", "Agility", "Movement & PA reduction", dweller.EffectiveA));
        Rows.Add(new StatRow("L", "Luck", "Crit chance", dweller.EffectiveL));

        StatRows.ItemsSource = Rows;
        RefreshCanIncrease();
    }

    public ObservableCollection<StatRow> Rows { get; } = new();

    private void StatPlus_Click(object sender, RoutedEventArgs e) {
        if (_remaining <= 0) return;
        var stat = (string)((Button)sender).Tag;
        if (_dweller.SpendSpecialPoint(stat)) {
            _remaining--;
            PointsLabel.Text = _remaining.ToString();

            // Update the matching row value
            foreach (var row in Rows)
                if (row.Letter == stat)
                    row.Value = GetEffective(stat);

            RefreshCanIncrease();
        }
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
        Close();
    }

    private void RefreshCanIncrease() {
        var canSpend = _remaining > 0;
        foreach (var row in Rows)
            row.CanIncrease = canSpend && GetEffective(row.Letter) < 10;
    }

    private int GetEffective(string letter) {
        return letter switch {
            "S" => _dweller.EffectiveS,
            "P" => _dweller.EffectiveP,
            "E" => _dweller.EffectiveE,
            "C" => _dweller.EffectiveC,
            "I" => _dweller.EffectiveI,
            "A" => _dweller.EffectiveA,
            "L" => _dweller.EffectiveL,
            _ => 0
        };
    }
}

/// <summary>Observable row item bound to the SPECIAL list.</summary>
public class StatRow : INotifyPropertyChanged {
    private bool _canIncrease;

    private int _value;

    public StatRow(string letter, string name, string desc, int value) {
        Letter = letter;
        Name = name;
        Desc = desc;
        _value = value;
        _canIncrease = true;
    }

    public string Letter { get; }
    public string Name { get; }
    public string Desc { get; }

    public int Value {
        get => _value;
        set {
            _value = value;
            OnPropertyChanged();
        }
    }

    public bool CanIncrease {
        get => _canIncrease;
        set {
            _canIncrease = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? p = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}