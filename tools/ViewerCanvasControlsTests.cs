using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunCanvasControlsChecks()
    {
        string root = Path.Combine(Root, "canvas-controls-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Original.png"); MakeImage(path, 800, 600, 96);
        var source = FaceImages.Inspect(path, CancellationToken.None); var review = new FaceScanResult { Backend = "GPU 0 / NVIDIA GeForce RTX 5090 / DirectML (verified RTX inference)" };
        for (int i = 0; i < 7; i++) review.Faces.Add(FaceFixture(source, FaceSolid(128, 128, (byte)(40 + i * 20), (byte)(160 - i * 10), (byte)(70 + i * 15))));
        var window = new FacesWindow(root, 4, "Auto", 4096); window.Show(); window.SetReview(review); Pump();
        try
        {
            var fields = Field<object>(window, "_reference"); var width = Field<TextBox>(fields, "Width"); var height = Field<TextBox>(fields, "Height");
            var widthSlider = Field<Slider>(fields, "WidthSlider"); var heightSlider = Field<Slider>(fields, "HeightSlider"); var aspect = Field<Slider>(fields, "AspectSlider");
            var autoHeight = Field<CheckBox>(fields, "AutoHeight");
            var sizeButton = Field<ToggleButton>(fields, "SizeButton"); var sizeMenu = Field<Popup>(fields, "SizePopup");
            Assert(!sizeMenu.IsOpen && !widthSlider.IsVisible && Field<FrameworkElement>(fields, "Panel").ActualHeight <= 48, "Canvas dimensions start in a closed compact drop-down");
            sizeButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent }); Pump();
            Assert(sizeMenu.IsOpen && widthSlider.IsVisible && heightSlider.IsVisible && aspect.IsVisible, "Keyboard opens all canvas size controls");
            widthSlider.Value = 1024; Wait(delegate { return LiveCanvasIdle(window); }, "Drop-down slider live update");
            Assert(sizeMenu.IsOpen && Field<Size>(window, "_canvasSize").Width == 1024, "Live preview updates while the menu remains open");
            Render((FrameworkElement)sizeMenu.Child, "face-size-menu-dark.png");
            PressKey(window, Key.Escape); Pump(); Assert(!sizeMenu.IsOpen && window.IsVisible && Field<Image>(window, "_preview").Source != null, "Escape dismisses size menu without closing Faces or clearing the canvas");
            sizeButton.IsChecked = true; Pump(); Assert(sizeMenu.IsOpen, "Size menu can reopen after dismissal"); sizeButton.IsChecked = false;
            widthSlider.Value = 1536;
            Assert(width.Text == "1536" && autoHeight.IsChecked == true && heightSlider.Value == Int32.Parse(height.Text), "Width slider updates pixels and automatic height together");
            width.Text = "1001"; Assert(widthSlider.Value == 1001, "Exact width input synchronizes its slider");
            heightSlider.Value = 900;
            Assert(height.Text == "900" && autoHeight.IsChecked == false && !height.IsReadOnly, "Height slider switches to editable manual height");
            aspect.Value = 2;
            Assert(Math.Abs(Int32.Parse(width.Text) - Int32.Parse(height.Text) * 2) <= 1 && autoHeight.IsChecked == false,
                "Aspect slider sets overall canvas proportions with pixel rounding");
            Assert(Field<System.Windows.Controls.ComboBox>(window, "_cropShape").SelectedIndex == 0, "Canvas aspect does not change face crop proportions");
            height.Text = "800"; Assert(heightSlider.Value == 800 && Math.Abs(aspect.Value - 1001.0 / 800) < .0001, "Exact height input updates aspect and height sliders without recursion");
            autoHeight.IsChecked = true; Assert(height.IsReadOnly && height.Text != "800", "Auto height recomputes dimensions and locks numeric height again");
            widthSlider.Value = 16000;
            Assert((long)Int32.Parse(width.Text) * Int32.Parse(height.Text) <= 64000000 && Int32.Parse(height.Text) <= 16000, "Automatic slider sizing stays inside canvas area/side limits");
            heightSlider.Value = 16000; widthSlider.Value = 16000;
            Assert((long)Int32.Parse(width.Text) * Int32.Parse(height.Text) <= 64000000, "Manual dimension sliders respect the 64 MP budget");
            width.Text = ""; widthSlider.Value = 512; Assert(width.Text == "512", "Slider recovers an incomplete numeric edit");
            aspect.Value = .25; Assert(Math.Abs(Int32.Parse(width.Text) / (double)Int32.Parse(height.Text) - .25) < .001, "Portrait canvas ratios work without changing crops");
            aspect.Value = 4; Assert(Math.Abs(Int32.Parse(width.Text) / (double)Int32.Parse(height.Text) - 4) < .001, "Landscape canvas ratios preserve dimensions at the minimum side");
            width.Text = "invalid"; Invoke(window, "UpdateCanvas");
            Assert(Field<TextBlock>(window, "_status").Text.Contains("whole numbers"), "Invalid exact dimensions remain safely validated"); width.Text = "512";
            sizeButton.IsChecked = true; Pump();
            var tabs = Field<TabControl>(window, "_tabs"); tabs.SelectedIndex = 1; Pump(); Assert(!sizeMenu.IsOpen, "Changing Faces mode dismisses the old menu"); fields = Field<object>(window, "_interactive");
            Field<Slider>(fields, "WidthSlider").Value = 1024; Field<Slider>(fields, "AspectSlider").Value = 1.6;
            Assert(Field<TextBox>(fields, "Width").Text == "1024" && Field<TextBox>(fields, "Height").Text == "640", "Each canvas mode has independent synchronized sliders");
            Field<ToggleButton>(fields, "SizeButton").IsChecked = true; Pump();
            Invoke(window, "UpdateCanvas"); Assert(!Field<Popup>(fields, "SizePopup").IsOpen && !Field<Slider>(fields, "WidthSlider").IsEnabled && !Field<CheckBox>(window, "_includeSubfolders").IsEnabled, "Size menu closes and editing locks during work");
            Wait(delegate { return window.PendingWork.IsCompleted; }, "Slider-sized canvas renders"); Pump();
            Assert(Field<Size>(window, "_canvasSize") == new Size(1024, 640), "Rendered canvas uses slider dimensions");
            string saved = Path.Combine(root, "Sized.myokenfaces"); window.SaveCanvasFile(saved); Wait(delegate { return window.PendingWork.IsCompleted; }, "Save slider layout");
            window.OpenCanvasFile(saved); Wait(delegate { return window.PendingWork.IsCompleted; }, "Restore slider layout"); Pump();
            Assert(Field<Slider>(fields, "WidthSlider").Value == 1024 && Field<Slider>(fields, "HeightSlider").Value == 640
                && Math.Abs(Field<Slider>(fields, "AspectSlider").Value - 1.6) < .0001 && Field<CheckBox>(fields, "AutoHeight").IsChecked == false,
                "Saved canvas restores manual dimensions/aspect and every slider");
            var scope = Field<CheckBox>(window, "_includeSubfolders"); var backend = Field<TextBlock>(window, "_backend"); var status = Field<TextBlock>(window, "_status");
            Assert(scope.Parent == backend.Parent && scope.Parent == status.Parent && scope.TransformToAncestor(window).Transform(new Point()).Y > window.ActualHeight - 90,
                "Include subfolders and GPU backend share the bottom status row");
            backend.Text = review.Backend; Render(window, "face-canvas-controls-dark.png");
            foreach (bool dark in new[] { true, false })
            {
                ThemeManager.SetDarkTheme(dark); window.Width = 780; window.Height = 540; Pump();
                foreach (var control in new FrameworkElement[] { scope, backend, status, Field<ToggleButton>(fields, "SizeButton"), Field<Button>(window, "_saveCanvas") }) AssertInside(control, (FrameworkElement)window.Content);
                Assert(Field<ScrollViewer>(window, "_viewport").ActualHeight > 140, "Compact size controls leave more canvas space at minimum window size");
                Render(window, dark ? "face-canvas-controls-narrow-dark.png" : "face-canvas-controls-narrow-light.png");
                Field<ToggleButton>(fields, "SizeButton").IsChecked = true; Pump();
                var popup = Field<Popup>(fields, "SizePopup"); var frame = (FrameworkElement)popup.Child;
                Assert(popup.IsOpen && frame.ActualWidth <= window.ActualWidth, "Canvas size drop-down fits the narrow window");
                var color = ((SolidColorBrush)((Border)frame).Background).Color; Assert(dark ? color.R < 80 : color.R > 200, "Canvas size menu follows app theme");
                foreach (var control in new FrameworkElement[] { Field<Slider>(fields, "WidthSlider"), Field<Slider>(fields, "HeightSlider"), Field<Slider>(fields, "AspectSlider"), Field<TextBox>(fields, "Width"), Field<TextBox>(fields, "Height") }) AssertInside(control, frame);
                Render(frame, dark ? "face-size-menu-narrow-dark.png" : "face-size-menu-narrow-light.png");
                PressKey(window, Key.Escape); Assert(!popup.IsOpen && window.IsVisible, "Size menu Escape works after reopen in each theme");
            }
            var original = Field<Image>(window, "_preview").Source;
            scope.IsChecked = true; Assert(Field<ListBox>(window, "_review").Items.Count == 0 && Field<Image>(window, "_preview").Source == null && original != null,
                "Relocated recursive-scan checkbox still invalidates the old review");
        }
        finally { window.Close(); Pump(); ThemeManager.SetDarkTheme(true); }
        Console.WriteLine("PASS: compact Canvas size drop-down, keyboard/reopen/Escape, live sliders, exact-value synchronization, auto/manual height, pixel limits, independent modes, tab/work guards, rendering/save/open, bottom scan/GPU row and narrow dark/light layouts.");
    }
}
