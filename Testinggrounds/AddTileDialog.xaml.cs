using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace IsometricWPF.Dialogs;

public partial class AddTileDialog : Window {
    private bool _loaded;

    public AddTileDialog() {
        InitializeComponent();

        Loaded += (_, __) => {
            _loaded = true;
            DrawPreview();
        };
    }

    public string TileName { get; private set; }
    public Color TopColor { get; private set; } = Color.FromRgb(56, 142, 60);
    public Color LeftColor { get; private set; } = Color.FromRgb(27, 94, 32);
    public Color RightColor { get; private set; } = Color.FromRgb(10, 61, 10);

    private void AddTile_Click(object sender, RoutedEventArgs e) {
        var name = TileNameBox.Text.Trim();

        if (string.IsNullOrEmpty(name)) {
            ShowError("Please enter a tile name.");
            return;
        }

        if (TileRegistry.All.ContainsKey(name)) {
            ShowError($"'{name}' already exists.");
            return;
        }

        TileName = name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) {
        DialogResult = false;
    }

    private void ShowError(string msg) {
        ErrorLabel.Text = msg;
        ErrorLabel.Visibility = Visibility.Visible;
    }


    private void TopColor_Changed(object sender, TextChangedEventArgs e) {
        if (!_loaded) return;
        if (TryParseHex(TopColorBox.Text, out var c)) {
            TopColor = c;
            TopColorSwatch.Fill = new SolidColorBrush(c);
            DrawPreview();
        }
    }

    private void LeftColor_Changed(object sender, TextChangedEventArgs e) {
        if (!_loaded) return;
        if (TryParseHex(LeftColorBox.Text, out var c)) {
            LeftColor = c;
            LeftColorSwatch.Fill = new SolidColorBrush(c);
            DrawPreview();
        }
    }

    private void RightColor_Changed(object sender, TextChangedEventArgs e) {
        if (!_loaded) return;
        if (TryParseHex(RightColorBox.Text, out var c)) {
            RightColor = c;
            RightColorSwatch.Fill = new SolidColorBrush(c);
            DrawPreview();
        }
    }


    private void TopSwatch_Click(object sender, MouseButtonEventArgs e) {
        PickColorInto(TopColorBox);
    }

    private void LeftSwatch_Click(object sender, MouseButtonEventArgs e) {
        PickColorInto(LeftColorBox);
    }

    private void RightSwatch_Click(object sender, MouseButtonEventArgs e) {
        PickColorInto(RightColorBox);
    }

    private void PickColorInto(TextBox target) {
        var dlg = new ColorInputDialog(target.Text) { Owner = this };
        if (dlg.ShowDialog() == true) target.Text = dlg.HexResult;
    }


    private void DrawPreview() {
        PreviewCanvas.Children.Clear();

        const double w = 64, h = 32, hs = 14;
        var cx = PreviewCanvas.Width / 2.0;
        var sx = cx - w / 2.0;
        var sy = 6.0;

        DrawFace(new PointCollection {
            new Point(sx + w / 2, sy), new Point(sx + w, sy + h / 2),
            new Point(sx + w / 2, sy + h), new Point(sx, sy + h / 2)
        }, TopColor);

        DrawFace(new PointCollection {
            new Point(sx, sy + h / 2), new Point(sx + w / 2, sy + h),
            new Point(sx + w / 2, sy + h + hs), new Point(sx, sy + h / 2 + hs)
        }, LeftColor);

        DrawFace(new PointCollection {
            new Point(sx + w / 2, sy + h), new Point(sx + w, sy + h / 2),
            new Point(sx + w, sy + h / 2 + hs), new Point(sx + w / 2, sy + h + hs)
        }, RightColor);
    }

    private void DrawFace(PointCollection pts, Color color) {
        PreviewCanvas.Children.Add(new Polygon {
            Points = pts,
            Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
            StrokeThickness = 0.5
        });
    }

    private static bool TryParseHex(string text, out Color color) {
        color = Colors.Gray;
        try {
            text = text.Trim().TrimStart('#');
            if (text.Length != 6) return false;
            color = Color.FromRgb(
                Convert.ToByte(text[..2], 16),
                Convert.ToByte(text[2..4], 16),
                Convert.ToByte(text[4..6], 16));
            return true;
        }
        catch {
            return false;
        }
    }
}

public class ColorInputDialog : Window {
    private readonly TextBox _box;

    public ColorInputDialog(string current) {
        Title = "Enter Hex Color";
        Width = 280;
        Height = 120;
        Background = new SolidColorBrush(Color.FromRgb(22, 22, 42));
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var stack = new StackPanel { Margin = new Thickness(14) };

        var label = new TextBlock {
            Text = "Hex color (e.g. #3A7D44):",
            Foreground = new SolidColorBrush(Colors.LightGray),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11, Margin = new Thickness(0, 0, 0, 6)
        };

        _box = new TextBox {
            Text = current,
            Background = new SolidColorBrush(Color.FromRgb(42, 42, 64)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            FontFamily = new FontFamily("Consolas"),
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 0, 0, 10),
            CaretBrush = new SolidColorBrush(Colors.White)
        };
        _box.SelectAll();
        _box.KeyDown += (s, e) => {
            if (e.Key == Key.Enter) Confirm();
        };

        var btnRow = new StackPanel
            { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = MakeBtn("OK", () => Confirm());
        var can = MakeBtn("Cancel", () => DialogResult = false);
        btnRow.Children.Add(can);
        btnRow.Children.Add(ok);

        stack.Children.Add(label);
        stack.Children.Add(_box);
        stack.Children.Add(btnRow);
        Content = stack;

        Loaded += (_, __) => _box.Focus();
    }

    public string HexResult { get; private set; }

    private void Confirm() {
        HexResult = _box.Text.Trim();
        DialogResult = true;
    }

    private static Button MakeBtn(string text, Action click) {
        var b = new Button {
            Content = text,
            Width = 70, Height = 26,
            Margin = new Thickness(4, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(42, 42, 64)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11, Cursor = Cursors.Hand
        };
        b.Click += (_, __) => click();
        return b;
    }
}