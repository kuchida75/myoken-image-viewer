using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private ToggleButton _verticalHeightButton;
        private CheckBox _configureVerticalHeight;
        private bool _isVerticallyMaximized, _changingVerticalHeight, _verticalUpdateQueued;
        private Rect _beforeVerticalHeight, _appliedVerticalHeight;
        private HwndSource _heightSource;

        private ToggleButton BuildVerticalHeightButton()
        {
            _verticalHeightButton = new ToggleButton { Content = "\u2195", FontSize = 19, Width = 32, Height = 30,
                Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(0), ToolTip = "Maximize window height" };
            AutomationProperties.SetName(_verticalHeightButton, "Maximize window height");
            _verticalHeightButton.Click += delegate { SetVerticalHeight(!_isVerticallyMaximized); };
            return _verticalHeightButton;
        }

        private void InitializeVerticalSizing()
        {
            _heightSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (_heightSource != null) _heightSource.AddHook(VerticalHeightMessage);
        }

        private IntPtr VerticalHeightMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            // Work-area, display and DPI changes can alter usable height, including taskbar settings.
            if ((message == 0x001A || message == 0x007E || message == 0x02E0) && !_verticalUpdateQueued)
            {
                _verticalUpdateQueued = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                {
                    _verticalUpdateQueued = false;
                    if (!_isClosed && _isVerticallyMaximized && !_isFullscreen && WindowState == WindowState.Normal)
                        ApplyVerticalBounds(WindowPlacement.VerticalBounds(new Rect(Left, Top, ActualWidth, ActualHeight), WindowPlacement.CurrentWorkArea(this)));
                }));
            }
            return IntPtr.Zero;
        }

        private void SetVerticalHeight(bool enabled)
        {
            if (_isClosed || _isFullscreen || WindowState != WindowState.Normal) { UpdateVerticalHeightControls(); return; }
            if (_isVerticallyMaximized == enabled) return;
            EndImagePan(); SaveActiveViewState();
            Rect current = new Rect(Left, Top, ActualWidth, ActualHeight);
            Rect area = WindowPlacement.CurrentWorkArea(this);
            Rect target;
            if (enabled)
            {
                _beforeVerticalHeight = current;
                target = WindowPlacement.VerticalBounds(current, area);
            }
            else
            {
                double height = Math.Min(area.Height, _beforeVerticalHeight.Height);
                target = new Rect(current.Left, Math.Max(area.Top, Math.Min(_beforeVerticalHeight.Top, area.Bottom - height)), current.Width, height);
            }
            _isVerticallyMaximized = enabled;
            ApplyVerticalBounds(target);
        }

        private void ApplyVerticalBounds(Rect target)
        {
            _changingVerticalHeight = true;
            _appliedVerticalHeight = target;
            try { MinHeight = Math.Min(MinHeight, target.Height); Top = target.Top; Height = target.Height; UpdateLayout(); }
            finally { _changingVerticalHeight = false; }
            UpdateVerticalHeightControls(); QueueImageView(); RememberWindowPlacement();
        }

        private void ObserveVerticalHeight()
        {
            if (_changingVerticalHeight) return;
            if (!_isFullscreen && _isVerticallyMaximized && (WindowState != WindowState.Normal
                || Math.Abs(Top - _appliedVerticalHeight.Top) > 2
                || Math.Abs(ActualHeight - _appliedVerticalHeight.Height) > 2))
                _isVerticallyMaximized = false;
            UpdateVerticalHeightControls();
        }

        private void UpdateVerticalHeightControls()
        {
            if (_verticalHeightButton == null) return;
            bool available = !_isFullscreen && WindowState == WindowState.Normal;
            _verticalHeightButton.IsEnabled = available;
            _verticalHeightButton.IsChecked = _isVerticallyMaximized;
            string label = _isVerticallyMaximized ? "Restore previous window height" : "Maximize window height";
            CommandPresentation.Describe(_verticalHeightButton, label, null, "Fill the monitor's available height while keeping the window width. Press again to restore. Respects the taskbar.");
            AutomationProperties.SetName(_verticalHeightButton, label);
            if (_configureVerticalHeight != null)
            {
                _configureVerticalHeight.IsEnabled = available;
                _configureVerticalHeight.IsChecked = _isVerticallyMaximized;
            }
        }
    }
}
