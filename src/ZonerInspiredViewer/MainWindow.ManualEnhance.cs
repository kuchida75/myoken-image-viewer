using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Canvas _manualImageSurface;
        private Border _manualEnhanceOverlay;
        private ScrollViewer _manualEnhanceScroll;
        private ToggleButton _manualEnhanceButton;
        private readonly List<Slider> _manualSliders = new List<Slider>();
        private readonly List<Button> _manualResets = new List<Button>();
        private bool _manualEnhanceVisible, _syncManualAdjustments;
        private ManualEnhanceEffect _manualEffect;
        private ManualAdjustments _appliedManualAdjustments;

        private static readonly ControlTemplate ManualSliderTemplate = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Slider}'>
 <Grid Height='26' Background='Transparent'>
  <Track x:Name='PART_Track'>
   <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='5' Background='#669A90' CornerRadius='2'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
   <Track.Thumb><Thumb Width='42' Height='24'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'>
    <Border Background='#304E4B' BorderBrush='#9BBDB5' BorderThickness='1' CornerRadius='3'><TextBlock Foreground='White' FontSize='12' HorizontalAlignment='Center' VerticalAlignment='Center' Text='{Binding Value, RelativeSource={RelativeSource AncestorType={x:Type Slider}}, StringFormat={}{0:0}}'/></Border>
   </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
   <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='5' Background='#63706F' CornerRadius='2'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
  </Track>
 </Grid>
</ControlTemplate>");

        private UIElement BuildManualEnhanceButton()
        {
            _manualEnhanceButton = new ToggleButton { Content = CommandPresentation.Label("\uE9E9", "Enhance"),
                Width = 100, Height = 30, Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
            CommandPresentation.Describe(_manualEnhanceButton, "Enhance adjustments", "E", "Show or hide live, non-destructive image sliders. Left/Right adjust the focused slider. Closing the panel keeps the adjustments.");
            _manualEnhanceButton.Click += delegate { SetManualEnhanceVisible(_manualEnhanceButton.IsChecked == true); };
            return _manualEnhanceButton;
        }

        private void BuildManualEnhanceOverlay()
        {
            var panel = new DockPanel();
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var close = ManualOverlayButton("\uE711", "Close Enhance", "Keep adjustments and close this panel.", delegate { SetManualEnhanceVisible(false); });
            DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
            var reset = ManualOverlayButton("\uE72C", "Reset all adjustments", "Return all ten manual values to zero. Quick Enhance is unchanged.", ResetManualAdjustments);
            DockPanel.SetDock(reset, Dock.Right); heading.Children.Add(reset);
            _manualSaveAsButton = ManualOverlayButton("\uE792", "Save adjusted image as", "Export the full adjusted image to another file. The original stays unchanged.", delegate { BeginEnhanceSave(true); });
            _manualSaveAsButton.Content = CommandPresentation.Label("\uE792", "Save As"); _manualSaveAsButton.Width = 82;
            DockPanel.SetDock(_manualSaveAsButton, Dock.Right); heading.Children.Add(_manualSaveAsButton);
            _manualSaveButton = ManualOverlayButton("\uE74E", "Save adjusted image", "Overwrite this file with the full-resolution result, including visible Quick Enhance and rotation. An original backup is kept.", delegate { BeginEnhanceSave(false); });
            _manualSaveButton.Content = CommandPresentation.Label("\uE74E", "Save"); _manualSaveButton.Width = 64;
            DockPanel.SetDock(_manualSaveButton, Dock.Right); heading.Children.Add(_manualSaveButton);
            heading.Children.Add(new TextBlock { Text = "Enhance", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
            _enhanceSaveStatus = new TextBlock { FontSize = 11, Foreground = Brushes.White, Opacity = 0.85,
                Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
            DockPanel.SetDock(_enhanceSaveStatus, Dock.Bottom); panel.Children.Add(_enhanceSaveStatus);
            var rows = new StackPanel();
            for (int i = 0; i < ManualAdjustments.Names.Length; i++)
            {
                int index = i; string name = ManualAdjustments.Names[i];
                var row = new Grid { Height = 30 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                row.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                var slider = new Slider { Minimum = ManualAdjustments.Minimum(i), Maximum = 100, Value = 0, TickFrequency = 1,
                    IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, SmallChange = 1, LargeChange = 10,
                    Height = 26, MinWidth = 64, Margin = new Thickness(0, 0, 6, 0), Template = ManualSliderTemplate };
                AutomationProperties.SetName(slider, name); slider.ToolTip = name + " (" + slider.Minimum + " to 100). Left/Right: adjust by 1. Neutral: 0.";
                slider.ValueChanged += delegate { ChangeManualAdjustment(index, slider.Value); };
                Grid.SetColumn(slider, 1); row.Children.Add(slider); _manualSliders.Add(slider);
                var button = ManualOverlayButton("\uE7A7", "Reset " + name, "Reset " + name.ToLowerInvariant() + " to 0.", delegate { _manualSliders[index].Value = 0; });
                Grid.SetColumn(button, 2); row.Children.Add(button); _manualResets.Add(button); rows.Children.Add(row);
            }
            _manualEnhanceScroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, Focusable = false };
            panel.Children.Add(_manualEnhanceScroll);
            _manualEnhanceOverlay = new Border { Width = 420, Padding = new Thickness(10), Margin = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.FromArgb(190, 15, 18, 21)), BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = panel, Visibility = Visibility.Collapsed };
            AutomationProperties.SetName(_manualEnhanceOverlay, "Enhance adjustments");
            _manualEnhanceOverlay.SizeChanged += delegate { LayoutImageOverlays(); };
            _manualEnhanceOverlay.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            { _manualEnhanceScroll.ScrollToVerticalOffset(_manualEnhanceScroll.VerticalOffset - e.Delta / 3.0); e.Handled = true; };
            _manualEnhanceOverlay.GotKeyboardFocus += delegate { _slideshowTimer.Stop(); };
            _manualEnhanceOverlay.LostKeyboardFocus += delegate { Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ArmSlideshow)); };
            Panel.SetZIndex(_manualEnhanceOverlay, 20); _imageView.Children.Add(_manualEnhanceOverlay);
        }

        private Button ManualOverlayButton(string glyph, string name, string description, Action action)
        {
            var button = CommandPresentation.Button(glyph, name, null, description, action, false);
            button.Width = 26; button.MinWidth = 0; button.Height = 26; button.Margin = new Thickness(0);
            button.Background = Brushes.Transparent; button.Foreground = Brushes.White;
            button.Resources[ThemeKeys.ControlHover] = new SolidColorBrush(Color.FromRgb(65, 73, 79));
            button.Resources[ThemeKeys.ControlPressed] = new SolidColorBrush(Color.FromRgb(39, 88, 84));
            return button;
        }

        private void SetManualEnhanceVisible(bool visible)
        { _manualEnhanceVisible = visible; UpdateManualEnhance(); ScheduleSessionSave(); }

        private void ToggleManualEnhanceFromKeyboard()
        {
            SetManualEnhanceVisible(!_manualEnhanceVisible);
            if (!_manualEnhanceVisible) return;
            string tab = _activeTabId; IInputElement focus = Keyboard.FocusedElement;
            // Opening the overlay can trigger viewport layout, which temporarily releases its focus.
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
            {
                if (!_isClosed && IsActive && _activeTabId == tab && _manualEnhanceOverlay.IsVisible
                    && (Keyboard.FocusedElement == focus || _imageCanvas.IsKeyboardFocusWithin)) _manualSliders[0].Focus();
            }));
        }

        private void ChangeManualAdjustment(int index, double value)
        {
            if (_syncManualAdjustments || !HasCurrentImage() || !_imageView.IsVisible) return;
            ImageTabState state = CurrentTabState();
            if (state.ManualAdjustments == null) state.ManualAdjustments = new ManualAdjustments();
            state.ManualAdjustments.Values[index] = Math.Round(value);
            _appliedManualAdjustments = null; UpdateManualEnhance(); ScheduleSessionSave();
        }

        private void ResetManualAdjustments()
        {
            if (!HasCurrentImage() || !_imageView.IsVisible) return;
            CurrentTabState().ManualAdjustments = new ManualAdjustments();
            UpdateManualEnhance(); ScheduleSessionSave();
        }

        private void HideManualEnhance()
        {
            if (_manualEnhanceOverlay == null) return;
            if (_manualEnhanceOverlay.IsMouseCaptureWithin) Mouse.Capture(null);
            if (_manualEnhanceOverlay.IsKeyboardFocusWithin) _imageCanvas.Focus();
            _manualEnhanceOverlay.Visibility = Visibility.Collapsed;
            _manualImageSurface.Effect = null; _appliedManualAdjustments = null;
            LayoutImageOverlays();
        }

        private void UpdateManualEnhance()
        {
            if (_manualEnhanceOverlay == null) return;
            bool ready = !_isClosed && _imageView.IsVisible && HasCurrentImage();
            _manualEnhanceButton.IsEnabled = ready; _manualEnhanceButton.IsChecked = _manualEnhanceVisible;
            UpdateEnhanceSaveButtons();
            if (!ready) { HideManualEnhance(); return; }
            if (!_manualEnhanceVisible)
            {
                if (_manualEnhanceOverlay.IsMouseCaptureWithin) Mouse.Capture(null);
                if (_manualEnhanceOverlay.IsKeyboardFocusWithin) _imageCanvas.Focus();
            }
            _manualEnhanceOverlay.Visibility = _manualEnhanceVisible ? Visibility.Visible : Visibility.Collapsed;
            ManualAdjustments value = CurrentTabState().ManualAdjustments;
            _syncManualAdjustments = true;
            for (int i = 0; i < _manualSliders.Count; i++)
            { _manualSliders[i].Value = value == null ? 0 : value.Values[i]; _manualResets[i].IsEnabled = _manualSliders[i].Value != 0; }
            _syncManualAdjustments = false;
            if (value == null || value.IsNeutral) { _manualImageSurface.Effect = null; _appliedManualAdjustments = null; }
            else if (_appliedManualAdjustments != value)
            {
                if (_manualEffect == null) _manualEffect = new ManualEnhanceEffect();
                _manualEffect.Apply(value); _manualImageSurface.Effect = _manualEffect; _appliedManualAdjustments = value;
            }
            LayoutImageOverlays();
        }

        private void LayoutManualEnhance(double width, double height, double reserved)
        {
            if (_manualEnhanceOverlay == null) return;
            double below = _imageMetadataOverlay.IsVisible ? _imageMetadataOverlay.Margin.Bottom - 16 + _imageMetadataOverlay.ActualHeight + 8 : reserved;
            _manualEnhanceOverlay.MaxWidth = width;
            Thickness space = FilmstripSpace();
            _manualEnhanceOverlay.Margin = new Thickness(16 + space.Left, 16, 16 + space.Right, 16 + below);
            _manualEnhanceOverlay.MaxHeight = Math.Max(0, height - below);
        }
    }
}
