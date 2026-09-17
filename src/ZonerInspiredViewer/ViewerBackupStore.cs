using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ZonerInspiredViewer
{
    [DataContract]
    internal sealed class ViewerBackupDocument
    {
        [DataMember] public string Format { get; set; }
        [DataMember] public int SchemaVersion { get; set; }
        [DataMember] public string AppVersion { get; set; }
        [DataMember] public DateTime SavedUtc { get; set; }
        [DataMember] public SessionState Workspace { get; set; }
        [DataMember] public BrowserPreferences Browser { get; set; }
        [DataMember] public List<NamedSessionDocument> SavedSessions { get; set; }
        public int MissingImageTabs { get; set; }
    }

    internal static class ViewerBackupStore
    {
        private const string FormatName = "ZonerInspiredViewer.Backup";
        private const long MaximumBytes = 64L * 1024 * 1024;

        public static ViewerBackupDocument Create(SessionState workspace, BrowserPreferences browser,
            IEnumerable<NamedSessionDocument> sessions)
        {
            return new ViewerBackupDocument
            {
                Format = FormatName, SchemaVersion = 1, AppVersion = BuildInfo.Version, SavedUtc = DateTime.UtcNow,
                Workspace = workspace, Browser = browser, SavedSessions = sessions.ToList()
            };
        }

        public static void Save(string path, ViewerBackupDocument document)
        {
            if (!String.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose a .json file for the viewer backup.");
            Validate(document, false);
            SessionStore.WriteObject(path, document, typeof(ViewerBackupDocument), MaximumBytes);
        }

        public static ViewerBackupDocument Load(string path)
        {
            ViewerBackupDocument document;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length == 0 || stream.Length > MaximumBytes)
                    throw new InvalidDataException("Backup files must be between 1 byte and 64 MB.");
                var serializer = new DataContractJsonSerializer(typeof(ViewerBackupDocument),
                    new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 2000000 });
                document = serializer.ReadObject(stream) as ViewerBackupDocument;
            }
            Validate(document, true);
            return document;
        }

        private static void Validate(ViewerBackupDocument document, bool removeMissingImages)
        {
            if (document == null || document.Format != FormatName || document.SchemaVersion != 1
                || document.Workspace == null || document.Browser == null || document.SavedSessions == null)
                throw new InvalidDataException("This is not a supported viewer backup (schema version 1).");
            if (document.SavedSessions.Count > 256) throw new InvalidDataException("The backup contains too many saved sessions.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int tabs = 0;
            document.MissingImageTabs = 0;
            ValidateSession(document.Workspace, document, removeMissingImages, ref tabs);
            foreach (NamedSessionDocument session in document.SavedSessions)
            {
                if (session == null || String.IsNullOrWhiteSpace(session.Name) || session.Name.Length > 80
                    || session.Name != session.Name.Trim() || !names.Add(session.Name) || session.State == null)
                    throw new InvalidDataException("The backup has an invalid or duplicate saved-session name.");
                ValidateSession(session.State, document, removeMissingImages, ref tabs);
            }
            if (tabs > 20000) throw new InvalidDataException("The backup contains too many tabs.");
            ValidatePaths(document.Browser.FavoriteFolders);
            ValidateStrings(document.Browser.ExpandedNodes, 4096);
            if (document.Browser.FavoriteDetails != null)
                foreach (FavoriteFolderDetails item in document.Browser.FavoriteDetails)
                {
                    if (item == null) throw new InvalidDataException("Invalid favorite details.");
                    ValidatePath(item.Path); ValidateString(item.Name, 200); ValidateString(item.Description, 2000);
                }
        }

        private static void ValidateSession(SessionState state, ViewerBackupDocument document, bool removeMissing, ref int tabCount)
        {
            if (state != null && state.BatchPresets != null && (state.BatchPresets.Count > 32 || state.BatchPresets.Any(preset => !BatchPreset.IsValid(preset))))
                throw new InvalidDataException("The backup contains invalid batch presets.");
            string shortcutError;
            if (!new ShortcutMap().Load(state.ShortcutOverrides, out shortcutError))
                throw new InvalidDataException("Invalid shortcut settings: " + shortcutError);
            ValidatePath(state.LastFolder); ValidatePath(state.ActiveTabPath); ValidatePath(state.BrowserSelectedPath);
            ValidateString(state.SearchText, 4096); ValidateString(state.ActiveTabId, 4096);
            if (state.SearchHistory != null)
            {
                if (state.SearchHistory.Count > 5) throw new InvalidDataException("The backup contains too many recent searches.");
                foreach (string query in state.SearchHistory) ValidateString(query, 4096);
            }
            ValidateString(state.ActiveNamedSessionName, 80);
            ValidateString(state.ProcessingGpuKey, 200);
            ValidateString(state.ImageGpuKey, 200);
            if (state.ImageGpuBudgetMb != 0 && (state.ImageGpuBudgetMb < 128 || state.ImageGpuBudgetMb > 24576))
                throw new InvalidDataException("The backup contains an invalid image VRAM budget.");
            if (state.LockedZoom != 0 && !MainWindow.IsValidLockedZoom(state.LockedZoom))
                throw new InvalidDataException("The backup contains an invalid locked zoom level.");
            if (!UpscaleModels.IsKnown(state.SuperResolutionModel)) throw new InvalidDataException("The backup contains an unknown AI upscale model.");
            if (state.SuperResolutionScale != 0 && Array.IndexOf(SuperResolution.Scales, state.SuperResolutionScale) < 0)
                throw new InvalidDataException("The backup contains an invalid AI upscale factor.");
            if ((!String.IsNullOrEmpty(state.ThumbnailGpuMode) && Array.IndexOf(GpuThumbnailProcessor.Modes, state.ThumbnailGpuMode) < 0)
                || (state.ProcessingGpuLimitMb != 0 && (state.ProcessingGpuLimitMb < 256 || state.ProcessingGpuLimitMb > 16384))
                || (state.ThumbnailGpuLimitMb != 0 && (state.ThumbnailGpuLimitMb < 16 || state.ThumbnailGpuLimitMb > 1024)))
                throw new InvalidDataException("The backup contains invalid GPU processing settings.");
            if (!String.IsNullOrEmpty(state.WheelFilmstripPosition) && Array.IndexOf(MainWindow.FilmstripPositions, state.WheelFilmstripPosition) < 0)
                throw new InvalidDataException("The backup contains an invalid wheel preview position.");
            if (state.WheelFilmstripSize != 0 && (state.WheelFilmstripSize < MainWindow.MinimumFilmstripSize || state.WheelFilmstripSize > MainWindow.MaximumFilmstripSize))
                throw new InvalidDataException("The backup contains an invalid wheel preview size.");
            if ((!String.IsNullOrEmpty(state.BackgroundPattern) && Array.IndexOf(ToolbarBackground.Patterns, state.BackgroundPattern) < 0)
                || (!String.IsNullOrEmpty(state.BackgroundFade) && Array.IndexOf(ToolbarBackground.Fades, state.BackgroundFade) < 0)
                || (!String.IsNullOrEmpty(state.BackgroundColor) && !MainWindow.IsValidTabColor(state.BackgroundColor))
                || (state.BackgroundIntensity.HasValue && (!ImageViewport.IsFinite(state.BackgroundIntensity.Value)
                    || state.BackgroundIntensity.Value < 0 || state.BackgroundIntensity.Value > 100)))
                throw new InvalidDataException("The backup contains invalid toolbar background settings.");
            if (!String.IsNullOrEmpty(state.PinnedTabColor) && !MainWindow.IsValidTabColor(state.PinnedTabColor))
                throw new InvalidDataException("The backup contains an invalid pinned tab color.");
            ValidatePaths(state.ForwardFolders); ValidatePaths(state.BrowserSelectedPaths);
            Finite(state.WindowLeft, state.WindowTop, state.WindowWidth, state.WindowHeight, state.MetadataPanelWidth,
                state.FolderPaneWidth, state.ThumbnailAspectRatio, state.BrowserScrollOffset, state.SlideshowSeconds);
            if (state.VramBudgetMb < 0 || state.VramBudgetMb > 24576 || state.MetadataPanelWidth > 50000
                || state.WindowWidth < 0 || state.WindowHeight < 0 || state.WindowWidth > 50000 || state.WindowHeight > 50000)
                throw new InvalidDataException("The backup contains settings outside the supported range.");
            if (state.LatestBrowserView != null) ValidateTab(state.LatestBrowserView);
            if (state.Tabs == null) throw new InvalidDataException("The backup is missing its tab list.");
            tabCount += state.Tabs.Count;
            if (tabCount > 20000) throw new InvalidDataException("The backup contains too many tabs.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SessionTabDto tab in state.Tabs)
            {
                if (tab == null) throw new InvalidDataException("The backup contains an invalid tab.");
                ValidateTab(tab);
                string id = tab.Id ?? tab.Path;
                if (String.IsNullOrWhiteSpace(id) || !ids.Add(id))
                    throw new InvalidDataException("The backup contains missing or duplicate tab IDs.");
                if (tab.IsBrowser ? String.IsNullOrWhiteSpace(tab.FolderPath)
                    : String.IsNullOrWhiteSpace(tab.Path) || !ImageExtensions.IsBrowsableImage(tab.Path))
                    throw new InvalidDataException("The backup contains a tab without a valid image or folder path.");
            }
            TabStacks.Validate(state);
            if (!removeMissing) return;
            document.MissingImageTabs += state.Tabs.RemoveAll(tab => !tab.IsBrowser && !File.Exists(tab.Path));
            TabStacks.RemoveEmpty(state);
            foreach (SessionTabDto tab in state.Tabs)
                if (tab.IsBrowser && !String.IsNullOrEmpty(tab.Path) && !File.Exists(tab.Path)) tab.Path = null;
            string active = state.ActiveTabId ?? state.ActiveTabPath;
            SessionTabDto selected = state.Tabs.FirstOrDefault(tab => String.Equals(tab.Id ?? tab.Path, active, StringComparison.OrdinalIgnoreCase));
            if (active != null && selected == null)
            {
                selected = state.Tabs.FirstOrDefault();
                state.ActiveTabId = selected == null ? null : selected.Id ?? selected.Path;
                state.ActiveTabPath = selected == null || selected.IsBrowser ? null : selected.Path;
            }
        }

        private static void ValidateTab(SessionTabDto tab)
        {
            if (!ManualAdjustments.IsValid(tab.ManualAdjustments)) throw new InvalidDataException("The backup contains invalid Enhance adjustments.");
            if (tab.SavedEnhanceRevision.HasValue && (tab.SavedEnhanceRevision.Value.Length < 0 || tab.SavedEnhanceRevision.Value.Modified < 0
                || tab.SavedEnhanceRevision.Value.Modified > DateTime.MaxValue.Ticks)) throw new InvalidDataException("The backup contains an invalid saved-image revision.");
            ValidatePath(tab.Path); ValidatePath(tab.FolderPath); ValidatePath(tab.SelectedPath);
            ValidateString(tab.Id, 4096); ValidateString(tab.SearchText, 4096);
            ValidatePaths(tab.SelectedPaths); ValidatePaths(tab.ForwardFolders);
            Finite(tab.Zoom, tab.OffsetX, tab.OffsetY, tab.ViewportWidth, tab.ViewportHeight, tab.BrowserScrollOffset);
            if (tab.Zoom < 0 || tab.Zoom > 64 || tab.ViewportWidth < 0 || tab.ViewportHeight < 0)
                throw new InvalidDataException("The backup contains an invalid image view.");
        }

        private static void Finite(params double[] values)
        {
            if (values.Any(value => !ImageViewport.IsFinite(value) || Math.Abs(value) > 1000000000))
                throw new InvalidDataException("The backup contains invalid numeric settings.");
        }

        private static void ValidatePaths(List<string> paths)
        {
            if (paths == null) return;
            if (paths.Count > 100000) throw new InvalidDataException("The backup contains too many paths.");
            foreach (string path in paths) ValidatePath(path);
        }

        private static void ValidatePath(string path)
        {
            if (String.IsNullOrEmpty(path)) return;
            bool absolute = path.StartsWith("\\\\", StringComparison.Ordinal)
                || (path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'));
            if (path.Length > 4096 || !absolute || !Path.IsPathRooted(path)
                || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new InvalidDataException("Backup paths must be absolute Windows paths.");
            Path.GetFullPath(path);
        }

        private static void ValidateStrings(List<string> values, int maximum)
        {
            if (values == null) return;
            if (values.Count > 100000) throw new InvalidDataException("The backup contains too many preferences.");
            foreach (string value in values) ValidateString(value, maximum);
        }

        private static void ValidateString(string value, int maximum)
        {
            if (value != null && value.Length > maximum) throw new InvalidDataException("The backup contains an oversized text field.");
        }
    }
}
