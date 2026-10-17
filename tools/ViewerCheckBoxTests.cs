using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunCheckBoxChecks()
    {
        Directory.CreateDirectory(Root);
        var panel = new StackPanel { Margin = new Thickness(16) };
        var boxes = new[] {
            new CheckBox { Content = "Auto columns", IsChecked = true },
            new CheckBox { Content = "Fit reference height to faces", IsChecked = true },
            new CheckBox { Content = "Include subfolders", IsChecked = false },
            new CheckBox { Content = "Mixed selection", IsThreeState = true, IsChecked = null },
            new CheckBox { Content = "Selected, temporarily unavailable", IsChecked = true, IsEnabled = false }
        };
        foreach (var box in boxes) { box.Height = 30; panel.Children.Add(box); }
        var window = new Window { Content = panel, Width = 420, Height = 235 }; ThemeManager.PrepareWindow(window); window.Show(); Pump();
        try
        {
            foreach (bool dark in new[] { true, false })
            {
                ThemeManager.SetDarkTheme(dark); Pump();
                foreach (var check in boxes)
                {
                    var box = (Border)check.Template.FindName("CheckBoxBox", check);
                    var tick = (System.Windows.Shapes.Path)check.Template.FindName("CheckMark", check);
                    var mixed = (Rectangle)check.Template.FindName("MixedMark", check);
                    Assert(box.ActualWidth == 16 && box.ActualHeight == 16, "Checkbox dimensions do not shift with state");
                    Assert((tick.Visibility == Visibility.Visible) == (check.IsChecked == true)
                        && (mixed.Visibility == Visibility.Visible) == (check.IsChecked == null), "Check and mixed marks track the actual selection");
                    if (!SystemParameters.HighContrast && check.IsChecked != false)
                    {
                        Assert(((SolidColorBrush)box.Background).Color == Colors.White, "Selected boxes stay white, including when disabled");
                        Color ink = ((SolidColorBrush)tick.Stroke).Color;
                        Assert(1.05 / (BackgroundLuminance(ink.R, ink.G, ink.B) + .05) > 10, "Tick has strong contrast against white");
                        foreach (int dpi in new[] { 96, 144, 192 })
                        {
                            var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new VisualBrush(check) { Stretch = Stretch.Fill }, null, new Rect(check.RenderSize));
                            double scale = dpi / 96.0; int width = (int)Math.Ceiling(check.ActualWidth * scale), height = (int)Math.Ceiling(check.ActualHeight * scale);
                            var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32); bitmap.Render(drawing); byte[] pixels = BitmapBytes(bitmap);
                            Point at = box.TranslatePoint(new Point(), check); int white = 0, inkPixels = 0;
                            for (int y = (int)((at.Y + 2) * scale); y < (int)((at.Y + 14) * scale); y++)
                            for (int x = (int)((at.X + 2) * scale); x < (int)((at.X + 14) * scale); x++)
                            {
                                int offset = (y * width + x) * 4;
                                if (pixels[offset] > 245 && pixels[offset + 1] > 245 && pixels[offset + 2] > 245 && pixels[offset + 3] > 245) white++;
                                if (pixels[offset] < 80 && pixels[offset + 1] < 80 && pixels[offset + 2] < 80 && pixels[offset + 3] > 245) inkPixels++;
                            }
                            Assert(white > 45 * scale * scale && inkPixels > 5 * scale * scale, "Rendered white box and dark selection mark remain visible at " + dpi + " DPI");
                        }
                    }
                }
                Assert(boxes[0].Focus(), "Checkbox remains keyboard focusable"); Pump();
                Assert(((Border)boxes[0].Template.FindName("FocusRing", boxes[0])).BorderThickness.Left == 1, "Keyboard focus has a visible indicator");
                foreach (var trigger in boxes[0].Template.Triggers.OfType<Trigger>().Where(t => t.Property == UIElement.IsMouseOverProperty || t.Property == System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty))
                    Assert(trigger.Setters.OfType<Setter>().All(s => s.Property != Border.BackgroundProperty), "Hover and press never darken selected boxes");
                Render(window, dark ? "checkbox-contrast-dark.png" : "checkbox-contrast-light.png");
            }
            var peer = new CheckBoxAutomationPeer(boxes[0]); var provider = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
            int changes = 0; boxes[0].Checked += delegate { changes++; }; boxes[0].Unchecked += delegate { changes++; };
            provider.Toggle(); Assert(boxes[0].IsChecked == false && provider.ToggleState == ToggleState.Off, "Native checkbox toggle still clears selection");
            provider.Toggle(); Assert(boxes[0].IsChecked == true && changes == 2 && provider.ToggleState == ToggleState.On, "Native toggle and binding events remain intact");
        }
        finally { window.Close(); ThemeManager.SetDarkTheme(true); Pump(); }
        Console.WriteLine("PASS: checkbox selection contrast, persistent white checked/disabled boxes, checked/mixed states, 96/144/192-DPI pixels, hover/press invariants, keyboard focus and native toggle semantics in dark/light themes.");
    }
}
