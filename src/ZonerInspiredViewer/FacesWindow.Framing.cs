using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class FacesWindow
    {
        private Slider _surroundings;
        private ComboBox _cropShape;
        private TextBlock _surroundingsValue;
        private Button _resetFraming;
        private DispatcherTimer _framingTimer;
        private FaceCropOptions _appliedFraming = new FaceCropOptions();
        private bool _syncFraming, _framingPending;
        private static readonly string[] ShapeLabels = { "Square (1:1)", "Portrait (4:5)", "Portrait (2:3)", "Landscape (4:3)", "Landscape (3:2)", "Wide (16:9)" };
        private static readonly double[] ShapeAspects = { 1, 4.0 / 5, 2.0 / 3, 4.0 / 3, 3.0 / 2, 16.0 / 9 };

        private FrameworkElement BuildFramingControls()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = "Face surroundings", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            _surroundingsValue = new TextBlock { Text = "30%", VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 6, 0) };
            Grid.SetColumn(_surroundingsValue, 1); header.Children.Add(_surroundingsValue);
            _resetFraming = CommandPresentation.Button("\uE777", "Reset face framing", null, "Restore square crops with 30% extra surroundings.", delegate { SyncFraming(new FaceCropOptions()); FramingChanged(); }, false);
            _resetFraming.Margin = new Thickness(0); Grid.SetColumn(_resetFraming, 2); header.Children.Add(_resetFraming); panel.Children.Add(header);
            _surroundings = new Slider { Minimum = 0, Maximum = 200, Value = 30, TickFrequency = 5, IsSnapToTickEnabled = true, SmallChange = 5, LargeChange = 25, Height = 24 };
            CommandPresentation.Describe(_surroundings, "Face surroundings", null, "0% tightly frames the detected face. 30% is the default; 200% triples the crop width and height. Crops fit inside the original image without stretching. Changes update crops/canvases without rerunning detection.");
            panel.Children.Add(_surroundings);
            _cropShape = new ComboBox { Height = 30, ItemsSource = ShapeLabels, SelectedIndex = 0, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(_cropShape, "Face crop shape");
            CommandPresentation.Describe(_cropShape, "Face crop shape", null, "Choose square, portrait or landscape crops. Automatic canvas height follows the selected ratio. Image edges may limit the available surroundings.");
            panel.Children.Add(_cropShape);
            _framingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _framingTimer.Tick += delegate
            {
                if (_surroundings.IsMouseCaptureWithin && Mouse.LeftButton == MouseButtonState.Pressed) return;
                _framingTimer.Stop(); ApplyFraming();
            };
            _surroundings.ValueChanged += delegate
            {
                _surroundingsValue.Text = _surroundings.Value.ToString("0", CultureInfo.InvariantCulture) + "%";
                FramingChanged();
            };
            _cropShape.SelectionChanged += delegate { FramingChanged(); };
            return panel;
        }
        private FaceCropOptions SelectedFraming()
        { return new FaceCropOptions { Surroundings = (int)Math.Round(_surroundings.Value), Aspect = ShapeAspects[Math.Max(0, _cropShape.SelectedIndex)] }; }
        private void SyncFraming(FaceCropOptions options)
        {
            _syncFraming = true;
            try
            {
                _surroundings.Value = options.Surroundings;
                _cropShape.SelectedIndex = Array.FindIndex(ShapeAspects, a => Math.Abs(a - options.Aspect) < .000001);
                if (_cropShape.SelectedIndex < 0) _cropShape.SelectedIndex = 0;
            }
            finally { _syncFraming = false; }
        }
        private void FramingChanged()
        {
            if (_syncFraming || _closed || _framingTimer == null) return;
            _framingTimer.Stop();
            if (_faces.Count == 0) { _appliedFraming = SelectedFraming(); _framingPending = false; return; }
            _framingPending = !SelectedFraming().SameAs(_appliedFraming);
            if (_framingPending) CancelCanvasResize();
            if (_framingPending) _framingTimer.Start();
            UpdateControls();
        }
        private void StopWork()
        {
            if (_busy) { _cancel.Cancel(); return; }
            if (_framingPending)
            {
                _framingTimer.Stop(); _framingPending = false; SyncFraming(_appliedFraming); CancelCanvasResize(true);
                UpdateControls(); _status.Text = "Framing change canceled."; return;
            }
            if (_canvasResizePending || _canvasResizeRunning)
            {
                CancelCanvasResize(true); UpdateControls(); _status.Text = "Canvas resize canceled."; return;
            }
        }
        private void ApplyFraming()
        {
            if (_busy || _closed || !_framingPending || _canvasSnapshot != null) return;
            var wanted = SelectedFraming(); var oldFaces = _faces.ToArray();
            var selected = oldFaces.Select((face, index) => new { Face = face, Index = index }).Where(x => _review.SelectedItems.Contains(x.Face)).Select(x => x.Index).ToArray();
            FaceCanvasOptions options = null;
            try { if (_preview.Source != null) options = ReadOptions(false); }
            catch (Exception error) { StopWork(); _status.Text = error.Message; return; }
            BeginWork(async token =>
            {
                bool committed = false;
                try
                {
                    var crops = await Task.Run(() => FaceImages.Reframe(oldFaces, wanted, _workers, token, Progress), token);
                    BitmapSource bitmap = options == null ? null : await Task.Run(() => FaceImages.Preview(crops, options, token), token);
                    token.ThrowIfCancellationRequested();
                    var review = new FaceScanResult { Backend = _backend.Text, ExecutionEvidence = _backend.ToolTip as string }; review.Faces.AddRange(crops); SetReview(review);
                    foreach (int index in selected) _review.SelectedItems.Add(crops[index]);
                    if (bitmap != null) ApplyCanvas(bitmap, options, crops);
                    _status.Text = "Face framing updated: " + wanted.Surroundings + "% / " + ShapeLabels[_cropShape.SelectedIndex] + ".";
                    committed = true;
                }
                finally
                {
                    if (!committed) { _framingPending = false; SyncFraming(_appliedFraming); }
                }
            }, "Updating face framing...");
        }
    }
}
