using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private int _cacheGeneration;

        private async void ShowResetConfirmation()
        {
            var dialog = new Window { Title = "Reset viewer", Owner = _configureWindow ?? this, Width = 460,
                SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner };
            ThemeManager.PrepareWindow(dialog);
            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(new TextBlock { Text = "Reset all settings, open tabs, saved sessions, favorites and search history?",
                TextWrapping = TextWrapping.Wrap, FontSize = 16, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = "Original photos are not changed. A recovery backup is created first. Close other viewer windows before resetting.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 16) });
            var cache = new CheckBox { Content = "Also clear thumbnail cache", IsChecked = false };
            panel.Children.Add(cache);
            panel.Children.Add(new TextBlock { Text = "Cached thumbnails are otherwise kept. Cleared thumbnails rebuild as you browse.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 18) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            var reset = new Button { Content = "Reset viewer", MinWidth = 106 };
            reset.Click += delegate { dialog.DialogResult = true; };
            buttons.Children.Add(cancel); buttons.Children.Add(reset); panel.Children.Add(buttons); dialog.Content = panel;
            if (dialog.ShowDialog() != true) return;
            try
            {
                string recovery = await ResetViewerAsync(cache.IsChecked == true);
                MessageBox.Show(this, "Viewer reset. Original photos are unchanged.\n\nRecovery backup:\n" + recovery,
                    "Reset complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Reset did not finish", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private Task<string> ResetViewerAsync(bool clearThumbnails)
        {
            if (_thumbnailRebuildRequest != null) throw new InvalidOperationException("Wait for thumbnail rebuilding to finish, or cancel it before resetting.");
            if (_configureWindow != null) _configureWindow.Close();
            if (_advancedSearchWindow != null) _advancedSearchWindow.Close();
            _searchHistoryInput.Commit();
            BeginBackupOperation();
            ViewerBackupDocument previous;
            try { previous = CaptureBackupSnapshot(); }
            catch { EndBackupOperation(); throw; }
            return Task.Run(delegate
            {
                string recovery = null;
                try
                {
                    using (_services.AcquireMaintenance())
                    {
                        string folder = Path.Combine(_services.AppDataRoot, "backups", "before-reset-" + DateTime.UtcNow.Ticks);
                        ProfileMaintenance.CheckPath(_services.AppDataRoot, folder);
                        Directory.CreateDirectory(folder);
                        previous.SavedSessions = new List<NamedSessionDocument>(_services.Sessions.CaptureNamedSessions());
                        string path = Path.Combine(folder, "viewer-backup.json");
                        ViewerBackupStore.Save(path, previous); recovery = path;
                        var defaults = new SessionState();
                        _services.Sessions.ResetProfile(folder, defaults);
                        Dispatcher.Invoke(new Action(delegate
                        {
                            _cacheGeneration++; _preloading.Clear(); _services.VramCache.Clear();
                            SetVerticalHeight(false);
                            _searchHistoryInput.DiscardPending();
                            ApplyImportedBackup(ViewerBackupStore.Create(defaults, new BrowserPreferences(), new List<NamedSessionDocument>()));
                            _thumbnailRebuildStatus.Text = "";
                        }));
                        if (clearThumbnails) _services.Thumbnails.Clear();
                        _services.Sessions.SaveOrThrow(Dispatcher.Invoke(new Func<SessionState>(CaptureSessionState)));
                        return recovery;
                    }
                }
                catch (Exception error)
                {
                    if (recovery != null) throw new InvalidOperationException("Reset did not finish. Some settings may have changed.\n\n"
                        + error.Message + "\n\nRecovery backup:\n" + recovery, error);
                    throw;
                }
                finally { Dispatcher.Invoke(new Action(EndBackupOperation)); }
            });
        }

        private async void ClearThumbnailCacheClick()
        {
            if (MessageBox.Show(_configureWindow ?? this, "Clear all cached thumbnail sizes?\n\nOriginal photos and viewer settings are kept. Thumbnails rebuild as you browse.",
                "Clear thumbnail cache", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                await ClearThumbnailCacheAsync();
                MessageBox.Show(this, "Thumbnail cache cleared. Visible previews may already be regenerating.", "Thumbnail cache");
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Unable to clear cache", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private Task ClearThumbnailCacheAsync()
        {
            if (_thumbnailRebuildRequest != null) throw new InvalidOperationException("Wait for thumbnail rebuilding to finish, or cancel it before clearing the cache.");
            if (_configureWindow != null) _configureWindow.Close();
            BeginBackupOperation();
            return Task.Run(delegate
            {
                try { using (_services.AcquireMaintenance()) _services.Thumbnails.Clear(); }
                finally { Dispatcher.Invoke(new Action(EndBackupOperation)); }
            });
        }
    }
}
