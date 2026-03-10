using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IsometricWPF.Dwellers;

public static class DwellerVisualFactory {
    private static readonly Dictionary<string, Drawing> _cache = new();

    public static string TextureBasePath = "pack://application:,,,/Assets/dwellers/";

    private static readonly Color[] TeamColors = {
        Color.FromRgb(0, 182, 255),
        Color.FromRgb(255, 60, 60),
        Color.FromRgb(80, 220, 80),
        Color.FromRgb(255, 200, 0),
        Color.FromRgb(220, 80, 220),
        Color.FromRgb(255, 140, 0),
        Color.FromRgb(0, 220, 200),
        Color.FromRgb(200, 200, 200)
    };

    public static Color TeamColor(int teamId) {
        return TeamColors[Math.Clamp(teamId, 0, TeamColors.Length - 1)];
    }

    public static void InvalidateCache() {
        _cache.Clear();
    }


    public static Drawing? Create(DwellerInstance dweller) {
        var key =
            $"{dweller.Data.Texture}|{dweller.TeamId}|{dweller.State}|{dweller.MovementPoints}|{dweller.Data.DisplayName}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var group = new DrawingGroup();
        var source = LoadImage(dweller.Data.Texture);
        if (source == null) return group;


        var shadowBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
        shadowBrush.Freeze();
        group.Children.Add(new GeometryDrawing(shadowBrush, null,
            new EllipseGeometry(new Point(0, 0), 16, 6)));


        var ratio = (double)source.PixelHeight / source.PixelWidth;
        double w = ratio >= 2.2 ? 32 : 40;
        double h = ratio >= 2.2 ? 78 : 82;
        group.Children.Add(new ImageDrawing(source, new Rect(-w / 2.0, -h, w, h)));


        if (dweller.State == DwellerState.Selected) {
            var teamColor = TeamColor(dweller.TeamId);
            var selPen = new Pen(new SolidColorBrush(teamColor), 2.2);
            selPen.Freeze();
            group.Children.Add(new GeometryDrawing(null, selPen,
                new EllipseGeometry(new Point(0, 0), 20, 8)));
            AddNameTag(group, dweller.Data.DisplayName, h, teamColor);
        }


        AddPmDots(group, dweller.MovementPoints, dweller.MaxMovementPoints, h);

        group.Freeze();
        _cache[key] = group;
        return group;
    }


    public static void DrawHpBar(DrawingContext dc, DwellerInstance d, Point tileCenter) {
        if (!d.ShowHpBar || d.IsDead || d.MaxHP <= 0) return;

        const double barW = 36.0;
        const double barH = 4.0;
        const double yOff = 88.0;
        var ratio = (double)d.HP / d.MaxHP;

        var x0 = tileCenter.X - barW / 2.0;
        var y0 = tileCenter.Y - yOff;


        var trackBrush = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
        trackBrush.Freeze();
        dc.DrawRectangle(trackBrush, null, new Rect(x0, y0, barW, barH));


        var fill = ratio > 0.6 ? Color.FromRgb(80, 220, 80)
            : ratio > 0.3 ? Color.FromRgb(240, 200, 0)
            : Color.FromRgb(255, 60, 60);
        var fillBrush = new SolidColorBrush(fill);
        fillBrush.Freeze();
        dc.DrawRectangle(fillBrush, null, new Rect(x0, y0, barW * ratio, barH));
    }


    private static void AddNameTag(DrawingGroup group, string name, double spriteH, Color teamColor) {
        var formatted = new FormattedText(name,
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 10, Brushes.White, 1.0);

        double tw = formatted.Width, th = formatted.Height;
        var tagY = -spriteH - th - 10;

        var tagBrush = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
        tagBrush.Freeze();
        group.Children.Add(new GeometryDrawing(tagBrush, null,
            new RectangleGeometry(new Rect(-tw / 2 - 4, tagY - 2, tw + 8, th + 4), 3, 3)));


        var borderBrush = new SolidColorBrush(Color.FromArgb(180, teamColor.R, teamColor.G, teamColor.B));
        borderBrush.Freeze();
        var borderPen = new Pen(borderBrush, 1.0);
        borderPen.Freeze();
        group.Children.Add(new GeometryDrawing(null, borderPen,
            new RectangleGeometry(new Rect(-tw / 2 - 4, tagY - 2, tw + 8, th + 4), 3, 3)));

        group.Children.Add(new GeometryDrawing(Brushes.White, null,
            formatted.BuildGeometry(new Point(-tw / 2, tagY))));
    }

    private static void AddPmDots(DrawingGroup group, int current, int max, double spriteH) {
        if (max <= 0) return;
        const double dotR = 3.0, gap = 8.0;
        var totalW = max * gap - (gap - dotR * 2);
        var startX = -totalW / 2 + dotR;
        var y = -spriteH - 4;

        for (var i = 0; i < max; i++) {
            var full = i < current;
            var fill = new SolidColorBrush(
                full
                    ? Color.FromRgb(80, 220, 80)
                    : Color.FromArgb(80, 255, 255, 255));
            fill.Freeze();
            group.Children.Add(new GeometryDrawing(fill, null,
                new EllipseGeometry(new Point(startX + i * gap, y), dotR, dotR)));
        }
    }

    private static BitmapImage? LoadImage(string textureName) {
        if (string.IsNullOrWhiteSpace(textureName)) return null;
        try {
            var sep = TextureBasePath.EndsWith('/') || TextureBasePath.EndsWith('\\') ? "" : "/";
            var img = new BitmapImage(new Uri(TextureBasePath + sep + textureName, UriKind.RelativeOrAbsolute));
            img.Freeze();
            return img;
        }
        catch {
            return null;
        }
    }
}