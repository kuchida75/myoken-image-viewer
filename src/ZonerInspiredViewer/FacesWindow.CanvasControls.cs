using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed partial class FacesWindow
    {
        private CanvasFields Fields()
        {
            var fields = new CanvasFields(); var panel = new StackPanel { Margin = new Thickness(8) }; fields.Panel = panel;
            fields.Width = Number("2048", 64, "Canvas width in pixels"); fields.Height = Number("2048", 64, "Canvas height in pixels"); fields.Columns = Number("8", 44, "Canvas columns");
            fields.WidthSlider = DimensionSlider("Canvas width"); fields.HeightSlider = DimensionSlider("Canvas height");
            fields.AspectSlider = new Slider { Minimum = .25, Maximum = 4, Value = 1, SmallChange = .01, LargeChange = .25, TickFrequency = .01, IsSnapToTickEnabled = true };
            fields.AspectValue = new TextBlock { Text = "1:1", Width = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            CommandPresentation.Describe(fields.AspectSlider, "Canvas aspect ratio", null, "Change the overall canvas width-to-height ratio, not the face crop shape. Sets height from width and turns off Auto height. The canvas updates automatically after a short pause.");
            var modes = new WrapPanel(); panel.Children.Add(modes);
            modes.Children.Add(CanvasSizeMenu(fields));
            fields.AutoColumns = new CheckBox { Content = "Auto columns", IsChecked = true, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            fields.Columns.IsReadOnly = true; modes.Children.Add(fields.AutoColumns);
            CommandPresentation.Describe(fields.AutoColumns, "Auto columns", null, "Balance the reviewed faces across populated rows with no empty cells. Disable to choose a fixed column count.");
            fields.AutoHeight = new CheckBox { Content = "Auto height", IsChecked = true, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            fields.Height.IsReadOnly = true; modes.Children.Add(fields.AutoHeight);
            CommandPresentation.Describe(fields.AutoHeight, "Auto height", null, "Fit canvas height to the face count, columns and crop shape. Moving Height or Aspect ratio switches to a manual height.");
            var columns = new StackPanel { Orientation = Orientation.Horizontal }; columns.Children.Add(new TextBlock { Text = "Columns", Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            columns.Children.Add(fields.Columns); modes.Children.Add(columns);
            fields.WidthSlider.ValueChanged += delegate { ChangeCanvasSlider(fields, 0); };
            fields.HeightSlider.ValueChanged += delegate { ChangeCanvasSlider(fields, 1); };
            fields.AspectSlider.ValueChanged += delegate { ChangeCanvasSlider(fields, 2); };
            foreach (var box in new[] { fields.Width, fields.Height, fields.Columns }) box.TextChanged += delegate { CanvasFieldsChanged(fields); };
            RoutedEventHandler columnsChanged = delegate
            {
                fields.Columns.IsReadOnly = fields.AutoColumns.IsChecked == true;
                if (!fields.Syncing) { UpdateReferenceColumns(); CanvasFieldsChanged(fields); }
            };
            fields.AutoColumns.Checked += columnsChanged; fields.AutoColumns.Unchecked += columnsChanged;
            RoutedEventHandler heightChanged = delegate { fields.Height.IsReadOnly = fields.AutoHeight.IsChecked == true; CanvasFieldsChanged(fields); };
            fields.AutoHeight.Checked += heightChanged; fields.AutoHeight.Unchecked += heightChanged;
            return fields;
        }
        private FrameworkElement CanvasSizeMenu(CanvasFields fields)
        {
            var host = new Grid { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            fields.SizeButton = new ToggleButton { Width = 146, Height = 30, Content = CommandPresentation.Label("\uE740", "Canvas size") };
            ((StackPanel)fields.SizeButton.Content).Children.Add(new TextBlock { Text = "\uE70D", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            CommandPresentation.Describe(fields.SizeButton, "Canvas size", null, "Open width, height and aspect-ratio sliders and exact pixel values. Slider changes update the canvas automatically. Esc closes this menu.");
            host.Children.Add(fields.SizeButton);
            var content = new StackPanel { Margin = new Thickness(14) };
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            fields.SizeSummary = new TextBlock { Text = "2048 x 2048 px", HorizontalAlignment = HorizontalAlignment.Right };
            ThemeManager.Bind(fields.SizeSummary, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            DockPanel.SetDock(fields.SizeSummary, Dock.Right); heading.Children.Add(fields.SizeSummary);
            heading.Children.Add(new TextBlock { Text = "Canvas size", FontWeight = FontWeights.SemiBold }); content.Children.Add(heading);
            content.Children.Add(CanvasSliderRow("Width (px)", fields.WidthSlider, fields.Width));
            content.Children.Add(CanvasSliderRow("Height (px)", fields.HeightSlider, fields.Height));
            content.Children.Add(CanvasSliderRow("Aspect ratio", fields.AspectSlider, fields.AspectValue));
            KeyboardNavigation.SetTabNavigation(content, KeyboardNavigationMode.Cycle);
            var frame = new Border { Width = 440, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Child = content };
            ThemeManager.Bind(frame, Border.BackgroundProperty, ThemeKeys.ControlBackground);
            ThemeManager.Bind(frame, Border.BorderBrushProperty, ThemeKeys.Border);
            frame.SetBinding(IsEnabledProperty, new Binding("IsEnabled") { Source = fields.SizeButton });
            TextElement.SetFontFamily(frame, new FontFamily("Segoe UI")); TextElement.SetFontSize(frame, 12);
            frame.SetResourceReference(TextElement.ForegroundProperty, ThemeKeys.Text);
            fields.SizePopup = new Popup { Child = frame, PlacementTarget = fields.SizeButton, Placement = PlacementMode.Bottom,
                VerticalOffset = 4, AllowsTransparency = true, StaysOpen = false };
            fields.SizePopup.SetBinding(Popup.IsOpenProperty, new Binding("IsChecked") { Source = fields.SizeButton, Mode = BindingMode.TwoWay });
            host.Children.Add(fields.SizePopup);
            fields.SizePopup.Opened += delegate { fields.WidthSlider.Focus(); };
            fields.SizePopup.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Escape) { fields.SizeButton.IsChecked = false; fields.SizeButton.Focus(); e.Handled = true; } };
            fields.SizeButton.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Enter || e.Key == Key.Down) { fields.SizeButton.IsChecked = true; e.Handled = true; } };
            fields.SizeButton.IsVisibleChanged += delegate { if (!fields.SizeButton.IsVisible) fields.SizeButton.IsChecked = false; };
            fields.SizeButton.IsEnabledChanged += delegate { if (!fields.SizeButton.IsEnabled) fields.SizeButton.IsChecked = false; };
            return host;
        }
        private bool CloseCanvasSizeMenus(bool restoreFocus = false)
        {
            bool closed = false;
            foreach (var fields in new[] { _reference, _interactive })
                if (fields != null && fields.SizePopup != null && fields.SizePopup.IsOpen)
                { fields.SizeButton.IsChecked = false; closed = true; if (restoreFocus) fields.SizeButton.Focus(); }
            return closed;
        }
        private static Slider DimensionSlider(string label)
        {
            var slider = new Slider { Minimum = 128, Maximum = 16000, Value = 2048, SmallChange = 1, LargeChange = 256, TickFrequency = 1, IsSnapToTickEnabled = true };
            CommandPresentation.Describe(slider, label, null, "128-16000 pixels, within the 64-megapixel canvas limit. Moving the slider updates the canvas automatically after a short pause. Numeric values allow exact sizes."); return slider;
        }
        private static FrameworkElement CanvasSliderRow(string label, Slider slider, FrameworkElement value)
        {
            var row = new Grid { Height = 30, Margin = new Thickness(0, 0, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            slider.VerticalAlignment = VerticalAlignment.Center; slider.Margin = new Thickness(0, 0, 8, 0); Grid.SetColumn(slider, 1); row.Children.Add(slider);
            Grid.SetColumn(value, 2); value.HorizontalAlignment = HorizontalAlignment.Right; row.Children.Add(value); return row;
        }
        private FaceCanvasOptions CanvasFieldOptions(CanvasFields fields, int width)
        {
            int height, columns;
            if (!Int32.TryParse(fields.Height.Text, out height) || !Int32.TryParse(fields.Columns.Text, out columns)) throw new InvalidDataException("Enter whole canvas dimensions and columns.");
            return new FaceCanvasOptions { Width = width, Height = height, Columns = columns, AutoHeight = fields.AutoHeight.IsChecked == true,
                AutoColumns = fields.AutoColumns.IsChecked == true, CellAspect = _cropShape == null ? 1 : SelectedFraming().Aspect };
        }
        private void SyncCanvasFields(CanvasFields fields)
        {
            if (fields == null || fields.Syncing) return; fields.Syncing = true;
            try
            {
                int width, height;
                if (!Int32.TryParse(fields.Width.Text, out width) || width < 128 || width > 16000) return;
                fields.WidthSlider.Value = width;
                if (fields.AutoHeight.IsChecked == true && _faces.Count > 0)
                {
                    try { var o = CanvasFieldOptions(fields, width); o.Validate(_faces.Count); fields.Height.Text = o.Height.ToString(CultureInfo.InvariantCulture); }
                    catch (InvalidDataException) { }
                }
                if (!Int32.TryParse(fields.Height.Text, out height) || height < 128 || height > 16000) return;
                fields.HeightSlider.Value = height; double ratio = width / (double)height;
                fields.AspectSlider.Minimum = Math.Min(fields.AspectSlider.Minimum, ratio); fields.AspectSlider.Maximum = Math.Max(fields.AspectSlider.Maximum, ratio);
                fields.AspectSlider.Value = ratio; fields.AspectValue.Text = ratio >= 1 ? ratio.ToString("0.##", CultureInfo.InvariantCulture) + ":1" : "1:" + (1 / ratio).ToString("0.##", CultureInfo.InvariantCulture);
                fields.AspectValue.ToolTip = width + " x " + height + " px";
                fields.SizeSummary.Text = width + " x " + height + " px";
            }
            finally { fields.Syncing = false; }
        }
        private void CanvasFieldsChanged(CanvasFields fields, bool live = false)
        {
            if (fields.Syncing) return; SyncCanvasFields(fields);
            if (fields != (_tabs.SelectedIndex == 1 ? _interactive : _reference)) return;
            if (live) QueueCanvasResize(fields); else InvalidatePreview();
        }

        private int LimitAutoWidth(CanvasFields fields, int requested)
        {
            if (_faces.Count == 0) return requested;
            try
            {
                var o = CanvasFieldOptions(fields, requested); int columns = o.AutoColumns ? FaceCanvasOptions.AutoRowCounts(_faces.Count).Max() : o.Columns;
                int low = Math.Max(128, (int)Math.Ceiling(columns * 8 * Math.Max(1, o.CellAspect))), high = Math.Max(low, requested);
                o.Width = low; o.Validate(_faces.Count);
                while (low < high)
                {
                    int mid = (low + high + 1) / 2; o.Width = mid;
                    try { o.Validate(_faces.Count); low = mid; } catch (InvalidDataException) { high = mid - 1; }
                }
                return low;
            }
            catch (InvalidDataException) { return requested; }
        }
        private void ChangeCanvasSlider(CanvasFields fields, int axis)
        {
            if (fields.Syncing || _busy || _closed) return;
            int width, height; if (!Int32.TryParse(fields.Width.Text, out width) || width < 128 || width > 16000) width = 2048;
            if (!Int32.TryParse(fields.Height.Text, out height) || height < 128 || height > 16000) height = 2048;
            fields.Syncing = true;
            try
            {
                if (axis == 0)
                {
                    width = (int)Math.Round(fields.WidthSlider.Value);
                    width = fields.AutoHeight.IsChecked == true ? LimitAutoWidth(fields, width) : Math.Min(width, 64000000 / height);
                }
                else
                {
                    fields.AutoHeight.IsChecked = false;
                    if (axis == 1) height = Math.Min((int)Math.Round(fields.HeightSlider.Value), 64000000 / width);
                    else
                    {
                        double ratio = fields.AspectSlider.Value;
                        double minWidth = Math.Max(128, 128 * ratio), maxWidth = Math.Min(Math.Min(16000, 16000 * ratio), Math.Sqrt(64000000 * ratio));
                        width = (int)Math.Floor(Math.Max(minWidth, Math.Min(maxWidth, width))); height = Math.Max(128, Math.Min(16000, (int)Math.Round(width / ratio)));
                        if ((long)width * height > 64000000) height--;
                    }
                }
                fields.Width.Text = width.ToString(CultureInfo.InvariantCulture); fields.Height.Text = height.ToString(CultureInfo.InvariantCulture);
            }
            finally { fields.Syncing = false; }
            CanvasFieldsChanged(fields, true);
        }
        private void SetCanvasFields(CanvasFields fields, FaceCanvasOptions options)
        {
            fields.Syncing = true;
            try
            {
                fields.Width.Text = options.Width.ToString(CultureInfo.InvariantCulture); fields.Height.Text = options.Height.ToString(CultureInfo.InvariantCulture);
                fields.Columns.Text = options.Columns.ToString(CultureInfo.InvariantCulture); fields.AutoColumns.IsChecked = options.AutoColumns; fields.AutoHeight.IsChecked = options.AutoHeight;
            }
            finally { fields.Syncing = false; }
            SyncCanvasFields(fields);
        }
    }
}
