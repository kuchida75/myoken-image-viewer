using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private string ImageAfterDeletion(string path)
        {
            return SurvivingNeighbor(_navigationImages, path)
                ?? SurvivingNeighbor(_allItems.Where(item => !item.IsDirectory).ToArray(), path);
        }

        private static string SurvivingNeighbor(IList<ImageFileItem> images, string path)
        {
            int index = -1;
            for (int i = 0; i < images.Count; i++)
                if (String.Equals(images[i].Path, path, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
            if (index < 0) return null;
            for (int i = index + 1; i < images.Count; i++)
                if (File.Exists(images[i].Path)) return images[i].Path;
            for (int i = index - 1; i >= 0; i--)
                if (File.Exists(images[i].Path)) return images[i].Path;
            return null;
        }

        private void RecycleConfirmedImage(string path)
        {
            if (_fileTransferBusy || !File.Exists(path)) return;
            StopSlideshow();
            string next = ImageAfterDeletion(path);
            ImageTabState active = CurrentTabState();
            bool activeViewer = !active.IsBrowser && String.Equals(active.Path, path, StringComparison.OrdinalIgnoreCase);
            // A canceled shell operation must leave tabs and selection untouched.
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
            SaveWorkspaceState();
            _services.VramCache.Remove(path);
            foreach (ImageTabState state in _tabs.Values.Concat(new[] { _homeBrowser, _latestBrowserView }).Where(state => state != null))
            {
                state.SelectedPaths.RemoveAll(value => String.Equals(value, path, StringComparison.OrdinalIgnoreCase));
                if (String.Equals(state.SelectedPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    state.SelectedPath = next;
                    if (next != null && !state.SelectedPaths.Contains(next, StringComparer.OrdinalIgnoreCase)) state.SelectedPaths.Add(next);
                }
                if (!String.Equals(state.Path, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (!state.IsBrowser) state.FolderPath = Path.GetDirectoryName(path);
                state.Path = null;
                state.ManualAdjustments = null;
                state.IsBrowser = true;
                state.HasCustomView = false;
                state.RotationQuarterTurns = 0;
                state.Zoom = 1;
                state.OffsetX = state.OffsetY = state.ViewportWidth = state.ViewportHeight = 0;
            }
            if (activeViewer && next != null)
            {
                active.Path = next;
                active.IsBrowser = false;
                active.FolderPath = Path.GetDirectoryName(next);
                active.SelectedPath = next;
                active.SelectedPaths = new List<string> { next };
            }
            else if (activeViewer && _isFullscreen) ToggleFullscreen();
            _allItems.RemoveAll(item => String.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
            RebuildImageOrdinals();
            DisplayTab();
            RefreshFolder();
            ScheduleSessionSave();
        }
    }
}
