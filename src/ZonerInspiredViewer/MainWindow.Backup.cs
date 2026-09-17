using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private bool _backupBusy;

        private async void ExportBackupClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export viewer backup", Filter = "Viewer backup (*.json)|*.json", DefaultExt = ".json",
                AddExtension = true, OverwritePrompt = true, FileName = "Viewer-backup-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json"
            };
            if (dialog.ShowDialog(this) != true) return;
            Exception error = null;
            try { await ExportBackupFileAsync(dialog.FileName).ConfigureAwait(false); }
            catch (Exception ex) { error = ex; }
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                MessageBox.Show(this, error == null ? "Viewer backup exported.\n\n" + dialog.FileName : error.Message,
                    error == null ? "Backup exported" : "Unable to export backup", MessageBoxButton.OK,
                    error == null ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }));
        }

        private async void ImportBackupClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import viewer backup", Filter = "Viewer backup (*.json)|*.json", CheckFileExists = true, Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            Exception error = null;
            int missing = -1;
            try { missing = await ImportBackupFileAsync(dialog.FileName, true).ConfigureAwait(false); }
            catch (Exception ex) { error = ex; }
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                if (error == null && missing < 0) return;
                MessageBox.Show(this, error == null ? "Viewer backup imported.\n\n" + missing
                    + " missing image tab(s) skipped across the workspace and saved sessions.\nA recovery backup was saved in your profile's backups folder."
                    : error.Message, error == null ? "Backup imported" : "Unable to import backup", MessageBoxButton.OK,
                    error == null ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }));
        }

        private ViewerBackupDocument CaptureBackupSnapshot()
        {
            return ViewerBackupStore.Create(CaptureSessionState(), _folderNavigation.CapturePreferences(),
                new List<NamedSessionDocument>());
        }

        private void BeginBackupOperation()
        {
            if (_backupBusy || _fileTransferBusy || _rotationSaving || _enhancementSaving) throw new InvalidOperationException("Another operation is still in progress.");
            StopSlideshow();
            EndImagePan();
            _placementSaveTimer.Stop();
            _backupBusy = true;
            IsEnabled = false;
        }

        private void EndBackupOperation()
        {
            _backupBusy = false;
            IsEnabled = true;
            UpdateStatus();
            ScheduleSessionSave();
        }

        private Task ExportBackupFileAsync(string path)
        {
            BeginBackupOperation();
            ViewerBackupDocument document;
            try { document = CaptureBackupSnapshot(); }
            catch { EndBackupOperation(); throw; }
            return Task.Run(delegate
            {
                try
                {
                    document.SavedSessions = new List<NamedSessionDocument>(_services.Sessions.CaptureNamedSessions());
                    ViewerBackupStore.Save(path, document);
                }
                finally { Dispatcher.Invoke(new Action(EndBackupOperation)); }
            });
        }

        private Task<int> ImportBackupFileAsync(string path, bool confirm)
        {
            BeginBackupOperation();
            return Task.Run(delegate
            {
                string recovery = null;
                try
                {
                    ViewerBackupDocument document = ViewerBackupStore.Load(path);
                    bool approved = !confirm || Dispatcher.Invoke(new Func<bool>(delegate
                    {
                        return MessageBox.Show(this,
                            "Replace this window's tabs, settings and favorites with the backup?\n\n"
                            + "Saved sessions with matching names will be replaced; other saved sessions are kept.\n"
                            + document.MissingImageTabs + " missing image tab(s) will be skipped.\n"
                            + "A recovery backup will be created first. No image files will be changed.",
                            "Import viewer backup", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                    }));
                    if (!approved) return -1;
                    ViewerBackupDocument previous = Dispatcher.Invoke(new Func<ViewerBackupDocument>(CaptureBackupSnapshot));
                    previous.SavedSessions = new List<NamedSessionDocument>(_services.Sessions.CaptureNamedSessions());
                    string backupFolder = Path.Combine(_services.AppDataRoot, "backups");
                    Directory.CreateDirectory(backupFolder);
                    string candidate = Path.Combine(backupFolder, "before-import-" + DateTime.UtcNow.Ticks + ".json");
                    ViewerBackupStore.Save(candidate, previous);
                    recovery = candidate;
                    foreach (NamedSessionDocument saved in document.SavedSessions)
                        _services.Sessions.SaveNamed(saved.Name, saved.State);
                    _services.Sessions.ReplaceBrowserPreferences(document.Browser);
                    Dispatcher.Invoke(new Action(delegate { ApplyImportedBackup(document); }));
                    SessionState imported = Dispatcher.Invoke(new Func<SessionState>(CaptureSessionState));
                    _services.Sessions.SaveOrThrow(imported);
                    return document.MissingImageTabs;
                }
                catch (Exception ex)
                {
                    if (recovery != null)
                        throw new InvalidOperationException("Import did not finish. Some settings may have changed.\n\n"
                            + ex.Message + "\n\nRestore the previous workspace from:\n" + recovery, ex);
                    throw;
                }
                finally { Dispatcher.Invoke(new Action(EndBackupOperation)); }
            });
        }

        private void ApplyImportedBackup(ViewerBackupDocument document)
        {
            if (_isFullscreen) ToggleFullscreen();
            SetCompactMode(false);
            var body = (Grid)_folderNavigation.Parent;
            int index = body.Children.IndexOf(_folderNavigation);
            _folderNavigation.Dispose();
            body.Children.Remove(_folderNavigation);
            UIElement folderPane = BuildFolderPane();
            Grid.SetColumn(folderPane, 0);
            body.Children.Insert(index, folderPane);
            ApplySessionState(document.Workspace, true);
            WindowState = WindowState.Normal;
            _normalWindowBounds = WindowPlacement.Restore(this, document.Workspace);
            _lastWindowState = WindowState;
        }
    }
}
