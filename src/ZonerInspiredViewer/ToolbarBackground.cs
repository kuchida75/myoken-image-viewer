using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed class ToolbarBackground : FrameworkElement
    {
        internal static readonly string[] Patterns = { "None", "Color fade", "Contours", "Facets", "Ribbons", "Crosshatch" };
        internal static readonly string[] Fades = { "Right edge", "Upper right", "Left edge" };
        internal const string DefaultPattern = "Crosshatch";
        internal const string DefaultColor = "#6ABFB5";
        internal const double DefaultIntensity = 100;
        private static readonly Dictionary<string, Geometry> Motifs = CreateMotifs();
        internal static readonly DependencyProperty BaseBrushProperty = DependencyProperty.Register("BaseBrush", typeof(Brush),
            typeof(ToolbarBackground), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
        internal static readonly DependencyProperty HighContrastProperty = DependencyProperty.Register("HighContrast", typeof(bool),
            typeof(ToolbarBackground), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush BaseBrush { get { return (Brush)GetValue(BaseBrushProperty); } set { SetValue(BaseBrushProperty, value); } }
        public bool HighContrast { get { return (bool)GetValue(HighContrastProperty); } set { SetValue(HighContrastProperty, value); } }
        internal string Pattern { get; private set; }
        internal string ColorHex { get; private set; }
        internal string Fade { get; private set; }
        internal double Intensity { get; private set; }

        internal ToolbarBackground()
        {
            IsHitTestVisible = false; Focusable = false; ClipToBounds = true;
            ThemeManager.Bind(this, BaseBrushProperty, ThemeKeys.ToolbarBackground);
            SetResourceReference(HighContrastProperty, SystemParameters.HighContrastKey);
            Configure(DefaultPattern, DefaultColor, "Right edge", DefaultIntensity);
        }

        internal void Configure(string pattern, string color, string fade, double intensity)
        {
            pattern = Array.IndexOf(Patterns, pattern) >= 0 ? pattern : DefaultPattern;
            fade = Array.IndexOf(Fades, fade) >= 0 ? fade : "Right edge";
            color = MainWindow.IsValidTabColor(color) ? color.ToUpperInvariant() : DefaultColor;
            intensity = ImageViewport.IsFinite(intensity) ? Math.Max(0, Math.Min(100, intensity)) : DefaultIntensity;
            if (Pattern == pattern && ColorHex == color && Fade == fade && Intensity == intensity) return;
            Pattern = pattern; ColorHex = color; Fade = fade; Intensity = intensity;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double width = ActualWidth, height = ActualHeight;
            if (width <= 0 || height <= 0) return;
            var bounds = new Rect(0, 0, width, height);
            dc.DrawRectangle(BaseBrush, null, bounds);
            if (Pattern == "None" || Intensity <= 0 || HighContrast) return;

            var baseColor = BaseBrush as SolidColorBrush;
            bool dark = baseColor == null || baseColor.Color.R < 128;
            Color color = (Color)ColorConverter.ConvertFromString(ColorHex);
            if (!dark) color = Color.FromRgb((byte)(color.R * 0.4), (byte)(color.G * 0.4), (byte)(color.B * 0.4));
            var ink = new SolidColorBrush(color); ink.Freeze();
            double span = Math.Min(width, Math.Max(300, Math.Min(900, width * 0.7)));
            bool left = Fade == "Left edge";
            var mask = new LinearGradientBrush(Colors.Transparent, Colors.Black,
                left ? new Point(span, 0) : new Point(width - span, 0), left ? new Point(0, 0) : new Point(width, 0))
                { MappingMode = BrushMappingMode.Absolute };
            mask.Freeze();
            dc.PushClip(new RectangleGeometry(bounds));
            dc.PushOpacity(Intensity / 100);
            dc.PushOpacityMask(mask);
            if (Fade == "Upper right")
                dc.PushOpacityMask(new LinearGradientBrush(Colors.Black, Colors.Transparent, new Point(0, 0), new Point(0, height))
                    { MappingMode = BrushMappingMode.Absolute });

            // Opacity is deliberately bounded so muted status text remains readable even with a white custom color.
            dc.PushOpacity(Pattern == "Color fade" ? 0.14 : 0.05);
            dc.DrawRectangle(ink, null, bounds);
            dc.Pop();
            Geometry motif;
            if (Motifs.TryGetValue(Pattern, out motif))
            {
                dc.PushOpacity(0.095);
                dc.PushTransform(new TranslateTransform(left ? 0 : width - 900, 0));
                dc.DrawGeometry(null, new Pen(ink, 1.25), motif);
                dc.Pop(); dc.Pop();
            }
            if (Fade == "Upper right") dc.Pop();
            dc.Pop(); dc.Pop(); dc.Pop();
        }

        private static Dictionary<string, Geometry> CreateMotifs()
        {
            // Original, frozen line art. Geometry and work stay bounded on ultrawide/high-DPI windows; no animation timer.
            var result = new Dictionary<string, Geometry>();
            foreach (string name in new[] { "Contours", "Facets", "Ribbons", "Crosshatch" })
            {
                var geometry = new StreamGeometry();
                using (StreamGeometryContext path = geometry.Open())
                {
                    if (name == "Contours")
                    {
                        for (int row = -9; row < 24; row++)
                        {
                            double y = row * 11;
                            path.BeginFigure(new Point(-20, y), false, false);
                            path.BezierTo(new Point(80, y - 40), new Point(130, y + 58), new Point(250, y + 15), true, false);
                            path.BezierTo(new Point(380, y - 65), new Point(400, y + 72), new Point(550, y + 12), true, false);
                            path.BezierTo(new Point(685, y - 54), new Point(780, y + 80), new Point(940, y - 8), true, false);
                        }
                    }
                    else if (name == "Facets")
                    {
                        for (int row = -1; row < 7; row++) for (int col = -1; col < 15; col++)
                        {
                            double x = col * 70 + (row % 2 == 0 ? 0 : 35), y = row * 42;
                            path.BeginFigure(new Point(x, y), false, false);
                            path.LineTo(new Point(x + 70, y), true, false);
                            path.LineTo(new Point(x + 35, y + 42), true, false);
                            path.LineTo(new Point(x, y), true, false);
                        }
                    }
                    else if (name == "Ribbons")
                    {
                        for (int col = -2; col < 12; col++) for (int line = 0; line < 3; line++)
                        {
                            double x = col * 105 + line * 9;
                            path.BeginFigure(new Point(x, -60), false, false);
                            path.BezierTo(new Point(x + 170, 18), new Point(x - 50, 92), new Point(x + 130, 160), true, false);
                            path.BezierTo(new Point(x + 260, 218), new Point(x + 70, 260), new Point(x + 240, 320), true, false);
                        }
                    }
                    else
                    {
                        for (int row = -1; row < 8; row++) for (int col = -1; col < 24; col++)
                        {
                            double x = col * 42, y = row * 36;
                            for (int line = 0; line < 3; line++)
                            {
                                bool rising = (row + col) % 2 == 0;
                                path.BeginFigure(new Point(x + line * 7, rising ? y + 26 : y), false, false);
                                path.LineTo(new Point(x + line * 7 + 24, rising ? y : y + 26), true, false);
                            }
                        }
                    }
                }
                geometry.Freeze(); result.Add(name, geometry);
            }
            return result;
        }
    }
}
