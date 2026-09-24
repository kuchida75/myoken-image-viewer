using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private readonly ShortcutMap _shortcuts = new ShortcutMap();
        private ComboBox _shortcutCommand, _shortcutKeys;
        private TextBox _shortcutCapture;
        private TextBlock _shortcutStatus;
        private int _shortcutCaptureIndex = -2;
        private KeyboardShortcutView _keyboardShortcuts;
        private ShortcutKeyEditor _keyboardKeyEditor;

        private void MainWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_configureWindow != null && _configureWindow.IsKeyboardFocusWithin) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                if (_folderLocation != null && _folderLocation.IsVisible && _folderLocation.IsEditing)
                { _folderLocation.CancelEdit(); _thumbnailGrid.Focus(); e.Handled = true; return; }
                if (_searchBox.IsKeyboardFocusWithin && !String.IsNullOrEmpty(_searchBox.Text))
                { StopSlideshow(); ClearFolderSearch(); e.Handled = true; return; }
                _thumbnailMinimap.EndDrag(); _imageNavigator.EndDrag(); StopSlideshow();
                if (_isFullscreen) { ToggleFullscreen(); e.Handled = true; }
                if (_imageView.Visibility == Visibility.Visible) { BrowseActiveTab(); e.Handled = true; }
                return;
            }
            if (RunShortcut(key, Keyboard.Modifiers, e.IsRepeat)) e.Handled = true;
        }

        private bool RunShortcut(Key key, ModifierKeys modifiers, bool repeat)
        {
            bool browser = _browserView.Visibility == Visibility.Visible;
            var command = _shortcuts.Match(key, modifiers, browser);
            if (command == null) return false;
            if (_filmstripSizeSlider != null && _filmstripSizeSlider.IsKeyboardFocusWithin && modifiers == ModifierKeys.None
                && IsThumbnailNavigationKey(key)) return false;
            if (_upscaleSizeSlider != null && _upscaleSizeSlider.IsKeyboardFocusWithin && modifiers == ModifierKeys.None
                && IsThumbnailNavigationKey(key)) return false;
            if (_manualEnhanceOverlay != null && _manualEnhanceOverlay.IsKeyboardFocusWithin && modifiers == ModifierKeys.None
                && (IsThumbnailNavigationKey(key) || key == Key.Enter || key == Key.Space || key == Key.Delete
                    || key == Key.Add || key == Key.Subtract || key == Key.OemPlus || key == Key.OemMinus)) return false;
            if (Keyboard.FocusedElement is TextBox)
            {
                bool folderButton = (key == Key.BrowserBack || key == Key.BrowserForward) && (command.Id == "parent" || command.Id == "forward");
                if (!folderButton && (!command.TextInput || ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0 && (key < Key.F1 || key > Key.F24)))) return false;
                if (modifiers == ModifierKeys.Control && new[] { Key.A, Key.C, Key.X, Key.V, Key.Z, Key.Y }.Contains(key)) return false;
            }
            if ((_slideshowOverlay != null && _slideshowOverlay.IsKeyboardFocusWithin || _enhanceMode.IsKeyboardFocusWithin)
                && (key == Key.Enter || key == Key.Space || IsThumbnailNavigationKey(key))) return false;
            if (!CanNavigateThumbnails() && (command.Selection || command.Scope != 3 || command.Id == "selectAll")) return false;
            if (repeat && !command.Repeat) return true;
            if (command.Selection)
                return _folderScanPending || _thumbnailGrid.HandleNavigationKey(command.NavigationKey, modifiers & (ModifierKeys.Control | ModifierKeys.Shift));
            switch (command.Id)
            {
                case "folderAddress": FocusFolderAddress(); break;
                case "openFolder": OpenFolderPicker(); break;
                case "help": ShowHelp(); break;
                case "configure": ShowConfigure(); break;
                case "search": SetCompactMode(false); _searchBox.Focus(); _searchBox.SelectAll(); break;
                case "advancedSearch": ShowAdvancedSearch(); break;
                case "newTab": OpenLatestBrowserTab(); break;
                case "nextTab": CycleTab(); break;
                case "closeTab": CloseTab(_activeTabId); break;
                case "fullscreen": ToggleFullscreen(); break;
                case "compact": SetCompactMode(!_isCompactMode); break;
                case "tree": ToggleFolderTree(); break;
                case "minimap": SetThumbnailMinimap(!_minimapVisible, true); break;
                case "metadata": SetImageMetadataOverlay(!_imageMetadataOverlayEnabled); break;
                case "navigator": SetImageNavigator(!_imageNavigatorEnabled); break;
                case "manualEnhance": ToggleManualEnhanceFromKeyboard(); break;
                case "upscale": ToggleAutomaticUpscale(); break;
                case "print": ShowPrintPreview(); break;
                case "open": ActivateBrowserItem(_thumbnailGrid.SelectedItem); break;
                case "browse": if (_isFullscreen) ToggleFullscreen(); BrowseActiveTab(); break;
                case "next": OpenRelativeImage(1); if (IsImageArrow(key) && !_folderScanPending) ShowFilmstrip(); break;
                case "previous": OpenRelativeImage(-1); if (IsImageArrow(key) && !_folderScanPending) ShowFilmstrip(); break;
                case "first": OpenBoundaryImage(false); break;
                case "last": OpenBoundaryImage(true); break;
                case "zoomIn": ZoomFromCenter(1.15); break;
                case "zoomOut": ZoomFromCenter(1 / 1.15); break;
                case "fit": FitImageToView(true); break;
                case "actual": SetZoomOneToOne(); break;
                case "fitWidth": FitImageAxis(true); break;
                case "fitHeight": FitImageAxis(false); break;
                case "zoomLock": ToggleZoomLock(); break;
                case "rotateLeft": RotateImage(-1); break;
                case "rotateRight": RotateImage(1); break;
                case "rename": RenameCurrent(); break;
                case "delete": DeleteCurrent(); break;
                case "copy": CopySelectedFiles(false); break;
                case "cut": CopySelectedFiles(true); break;
                case "paste": PasteFiles(_currentFolder); break;
                case "selectAll": _thumbnailGrid.SelectAll(); break;
                case "parent": case "browserParent": NavigateUp(); break;
                case "forward": NavigateDown(); break;
                case "enhance": _enhanceButton.IsChecked = !_quickEnhanceEnabled; ToggleQuickEnhance(); break;
                case "slideshow": if (_slideshowPlaying) StopSlideshow(); else StartSlideshow(); break;
                case "pauseSlideshow": ToggleSlideshowPause(); break;
            }
            return true;
        }

        private static bool IsImageArrow(Key key)
        {
            return key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down;
        }

        private void BuildShortcutConfiguration()
        {
            SetValue(CommandPresentation.ShortcutResolverProperty, new Func<string, string>(_shortcuts.Presentation));
            _configurationContent.SetValue(CommandPresentation.ShortcutResolverProperty, new Func<string, string>(_shortcuts.Presentation));
            _shortcutCommand = new ComboBox { MinHeight = 30, ItemsSource = _shortcuts.Commands.OrderBy(c => c.Name).ToList() };
            AutomationProperties.SetName(_shortcutCommand, "Shortcut command");
            _shortcutCommand.SelectionChanged += delegate { CancelShortcutCapture(); RefreshShortcutEditor(); };
            _shortcutKeys = new ComboBox { MinHeight = 30 };
            AutomationProperties.SetName(_shortcutKeys, "Assigned keys");
            _shortcutCapture = new TextBox { IsReadOnly = true, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(_shortcutCapture, "Record shortcut");
            _shortcutCapture.LostKeyboardFocus += delegate { CancelShortcutCapture(); };
            _shortcutStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            _keyboardShortcuts = new KeyboardShortcutView(_shortcuts, delegate { ResetAllShortcuts(true); });
            _keyboardShortcuts.EditRequested += EditKeyboardKey;
            _keyboardShortcuts.LayoutChanged += delegate
            {
                if (_shortcutCaptureIndex == -2) RefreshShortcutEditor();
                if (_keyboardKeyEditor != null) _keyboardKeyEditor.RefreshLayout();
                if (_helpWindow != null) _helpWindow.RefreshShortcuts(_shortcuts);
            };
            _configurationPages[5].Children.Add(_keyboardShortcuts);
            AddConfigurationRow(5, "Command", _shortcutCommand);
            AddConfigurationRow(5, "Assigned keys", _shortcutKeys);
            var buttons = new WrapPanel();
            buttons.Children.Add(CommandPresentation.Button("\uE710", "Add", null, "Record an additional key combination.", delegate { BeginShortcutCapture(false); }, true));
            buttons.Children.Add(CommandPresentation.Button("\uE70F", "Replace", null, "Replace the selected key combination.", delegate { BeginShortcutCapture(true); }, true));
            buttons.Children.Add(CommandPresentation.Button("\uE74D", "Remove", null, "Remove the selected key combination.", RemoveShortcut, true));
            AddConfigurationRow(5, "Assignment", buttons);
            AddConfigurationRow(5, "New keys", _shortcutCapture);
            var reset = new WrapPanel();
            reset.Children.Add(CommandPresentation.Button("\uE777", "Reset command", null, "Restore this command's default keys.", ResetShortcutCommand, true));
            AddConfigurationRow(5, "Defaults", reset);
            _configurationPages[5].Children.Add(_shortcutStatus);
            _shortcutCommand.SelectedIndex = 0;
        }

        private ShortcutCommand SelectedShortcut { get { return _shortcutCommand.SelectedItem as ShortcutCommand; } }

        private void RefreshShortcutEditor()
        {
            if (_shortcutKeys == null || SelectedShortcut == null) return;
            _shortcutKeys.ItemsSource = _shortcuts.Keys(SelectedShortcut.Id);
            _shortcutKeys.SelectedIndex = _shortcutKeys.Items.Count > 0 ? 0 : -1;
            if (_shortcutStatus != null) _shortcutStatus.Text = "";
        }

        private void BeginShortcutCapture(bool replace)
        {
            if (SelectedShortcut == null) return;
            if (replace && _shortcutKeys.SelectedIndex < 0) { _shortcutStatus.Text = "Select an assignment to replace, or choose Add."; return; }
            _shortcutCaptureIndex = replace ? _shortcutKeys.SelectedIndex : -1;
            _shortcutCapture.Text = "Press a key combination";
            _shortcutStatus.Text = "Escape cancels recording. Escape and Windows shortcuts cannot be reassigned.";
            _shortcutCapture.Focus();
            _shortcutCapture.BringIntoView();
        }

        private void CancelShortcutCapture()
        {
            _shortcutCaptureIndex = -2;
            if (_shortcutCapture != null) _shortcutCapture.Text = "";
        }

        private bool CaptureShortcut(Key key, ModifierKeys modifiers, bool repeat)
        {
            if (_shortcutCaptureIndex == -2) return false;
            if (key == Key.Escape) { CancelShortcutCapture(); _shortcutStatus.Text = "Recording cancelled."; return true; }
            if (repeat || ShortcutMap.IsModifier(key)) return true;
            var keys = _shortcuts.Keys(SelectedShortcut.Id);
            var gesture = ShortcutMap.Gesture(key, modifiers);
            if (_shortcutCaptureIndex < 0) keys.Add(gesture); else keys[_shortcutCaptureIndex] = gesture;
            string error;
            if (!_shortcuts.Set(SelectedShortcut.Id, keys, out error)) { _shortcutStatus.Text = error; return true; }
            ShortcutsChanged(); _shortcutStatus.Text = "Assigned " + gesture + "."; return true;
        }

        private void RemoveShortcut()
        {
            if (_shortcutKeys.SelectedIndex < 0) return;
            var keys = _shortcuts.Keys(SelectedShortcut.Id); keys.RemoveAt(_shortcutKeys.SelectedIndex);
            string error;
            if (_shortcuts.Set(SelectedShortcut.Id, keys, out error)) ShortcutsChanged(); else _shortcutStatus.Text = error;
        }

        private void ResetShortcutCommand()
        {
            if (SelectedShortcut == null) return;
            string error;
            if (_shortcuts.Set(SelectedShortcut.Id, SelectedShortcut.Defaults, out error)) ShortcutsChanged(); else _shortcutStatus.Text = error;
        }

        private void ShortcutsChanged()
        {
            CancelShortcutCapture(); RefreshShortcutEditor(); ScheduleSessionSave();
            if (_keyboardShortcuts != null) _keyboardShortcuts.RefreshBindings();
            if (_helpWindow != null) _helpWindow.RefreshShortcuts(_shortcuts);
        }

        private void RestoreShortcuts(SessionState state)
        {
            string error;
            if (!_shortcuts.Load(state.ShortcutOverrides, out error)) _shortcuts.ResetAll();
            CancelShortcutCapture(); RefreshShortcutEditor();
            if (_keyboardShortcuts != null) _keyboardShortcuts.RefreshBindings();
            if (_helpWindow != null) _helpWindow.RefreshShortcuts(_shortcuts);
        }

        private void ResetAllShortcuts(bool confirm)
        {
            if (confirm && MessageBox.Show(_configureWindow, "Restore all default shortcuts?", "Reset shortcuts", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _shortcuts.ResetAll(); ShortcutsChanged();
        }

        private void EditKeyboardKey(Key key)
        {
            string reserved = KeyboardShortcutView.Reserved(key);
            if (reserved != null) { _shortcutStatus.Text = reserved; return; }
            if (_keyboardKeyEditor != null) { _keyboardKeyEditor.Activate(); return; }
            CancelShortcutCapture();
            var editor = new ShortcutKeyEditor(_shortcuts, key, _keyboardShortcuts.Scope, SelectedShortcut) { Owner = _configureWindow };
            editor.BindingsChanged += ShortcutsChanged; _keyboardKeyEditor = editor;
            try { editor.ShowDialog(); }
            finally { _keyboardKeyEditor = null; _keyboardShortcuts.RefreshLayout(); }
        }
    }
}
