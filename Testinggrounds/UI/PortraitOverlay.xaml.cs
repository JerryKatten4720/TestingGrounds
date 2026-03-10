using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using IsometricWPF.Dwellers;

namespace IsometricWPF.UI;

/// <summary>
///     Borderless popup showing a large portrait + full stats for a dweller.
///     Click anywhere to dismiss.
/// </summary>
public partial class PortraitOverlay : Window {
    public PortraitOverlay(DwellerInstance d, Window owner) {
        Owner = owner;
        InitializeComponent();
        Populate(d);
    }

    private void Populate(DwellerInstance d) {
        // Portrait image
        try {
            var path = DwellerVisualFactory.TextureBasePath + d.Data.Texture;
            var bmp = new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute));
            bmp.Freeze();
            PortraitImage.Source = bmp;
        }
        catch { /* No image — empty border is fine */
        }

        NameLabel.Text = d.Data.DisplayName;
        RarityLabel.Text = $"{d.Data.RarityEnum}  ·  Team {d.TeamId + 1}  ·  Level {d.Level}";

        // HP bar
        HpLabel.Text = $"HP  {d.HP} / {d.MaxHP}";
        HpBar.Value = d.MaxHP > 0 ? (double)d.HP / d.MaxHP : 0;

        // XP bar
        XpLabel.Text = $"XP  {d.XP} / {d.XPToNext}  (Lv {d.Level})";
        XpBar.Value = d.XPToNext > 0 ? (double)d.XP / d.XPToNext : 0;

        // SPECIAL
        SpecialLabel.Text =
            $"STR  {d.EffectiveS,2}     PER  {d.EffectiveP,2}\n" +
            $"END  {d.EffectiveE,2}     CHA  {d.EffectiveC,2}\n" +
            $"INT  {d.EffectiveI,2}     AGI  {d.EffectiveA,2}\n" +
            $"LCK  {d.EffectiveL,2}";

        // Equipment summary
        var eq = new StringBuilder();
        if (d.MeleeWeapon != null) eq.AppendLine($"⚔ {d.MeleeWeapon.Name}");
        if (d.RangedWeapon != null) eq.AppendLine($"🏹 {d.RangedWeapon.Name}");
        if (d.EquippedArmor != null) eq.AppendLine($"🛡 {d.EquippedArmor.Name}");
        if (d.Pet != null) eq.AppendLine($"🐾 {d.Pet.Name}");
        if (eq.Length > 0)
            SpecialLabel.Text += "\n\n" + eq.ToString().TrimEnd();

        // Inventory
        if (d.Inventory.Count > 0) {
            var inv = string.Join(", ", d.Inventory.Select(i => i.Name));
            SpecialLabel.Text += $"\n📦 {inv}";
        }

        // Backstory
        if (!string.IsNullOrWhiteSpace(d.Data.Backstory)) {
            BackstoryLabel.Text = $"\"{d.Data.Backstory}\"";
            BackstoryLabel.Visibility = Visibility.Visible;
        }

        if (d.PendingSpecialPoints > 0)
            SpecialLabel.Text += $"\n\n⬆ {d.PendingSpecialPoints} point(s) ready to spend!";
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        Close();
    }
}