using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace IsometricWPF.Dwellers
{
    public static class DwellerVisualFactory
    {
        private static readonly Dictionary<string, Drawing> _cache = new();
        public static string TextureBasePath = "pack://application:,,,/Assets/dwellers/";

        private static readonly Color[] TeamColors =
        {
            Color.FromRgb(0,   182, 255),
            Color.FromRgb(255, 60,  60),
            Color.FromRgb(80,  220, 80),
            Color.FromRgb(255, 200, 0),
        };

        public static void InvalidateCache() => _cache.Clear();

        public static Drawing Create(DwellerInstance dweller)
        {
            string key = $"{dweller.Data.Texture}_{dweller.TeamId}_{dweller.State}_{dweller.Data.DisplayName}";
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var drawingGroup = new DrawingGroup();
            var imageSource = LoadImage(dweller.Data.Texture);
            if (imageSource == null) return drawingGroup;


            var shadowBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
            shadowBrush.Freeze();
            drawingGroup.Children.Add(new GeometryDrawing(shadowBrush, null, new EllipseGeometry(new Point(0, 0), 16, 6)));
            
            double spriteWidth = imageSource.Width;
            double spriteHeight = imageSource.Height;
            double ratio = spriteHeight / spriteWidth;
            double width = 40, height = 82;

            if (ratio >= 2.2) { width = 32; height = 78; }

            // MessageBox.Show("Ratio : " + ratio);

            var spriteRect = new Rect(-width / 2.0, -height, width, height);


            drawingGroup.Children.Add(new ImageDrawing(imageSource, spriteRect));


            if (dweller.State == DwellerState.Selected)
            {
                var selectionPen = new Pen(Brushes.White, 2);
                selectionPen.Freeze();
                drawingGroup.Children.Add(new GeometryDrawing(null, selectionPen, new EllipseGeometry(new Point(0, 0), 20, 8)));

                var formattedText = new FormattedText(
                    dweller.Data.DisplayName,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Consolas"),
                    10,
                    Brushes.White,
                    1.0);

                double textWidth = formattedText.Width, textHeight = formattedText.Height;
                var tagRect = new Rect(-textWidth / 2 - 4, -height - textHeight - 10, textWidth + 8, textHeight + 4);
                
                var tagBrush = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
                tagBrush.Freeze();
                drawingGroup.Children.Add(new GeometryDrawing(tagBrush, null, new RectangleGeometry(tagRect, 3, 3)));
                drawingGroup.Children.Add(new GeometryDrawing(Brushes.White, null, formattedText.BuildGeometry(new Point(-textWidth / 2, -height - textHeight - 8))));
            }

            drawingGroup.Freeze();
            _cache[key] = drawingGroup;
            return drawingGroup;
        }

        private static BitmapImage LoadImage(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName)) return null;
            try
            {
                string path = TextureBasePath.EndsWith("/") || TextureBasePath.EndsWith("\\")
                    ? TextureBasePath + textureName
                    : TextureBasePath + "/" + textureName;

                var img = new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute));
                img.Freeze();
                return img;
            }
            catch { return null; }
        }
    }
}
