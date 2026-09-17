using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ZonerInspiredViewer
{
    internal sealed class ImageFileItem
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string Extension { get; set; }
        public bool IsDirectory { get; set; }
        public long Length { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime LastWriteUtc { get; set; }

        public static ImageFileItem FromPath(string path)
        {
            var info = new System.IO.FileInfo(path);
            return new ImageFileItem
            {
                Path = path,
                Name = info.Name,
                Extension = info.Extension.ToLowerInvariant(),
                Length = info.Exists ? info.Length : 0,
                CreatedUtc = info.Exists ? info.CreationTimeUtc : DateTime.MinValue,
                LastWriteUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue
            };
        }

        public static ImageFileItem FromDirectory(string path)
        {
            var info = new System.IO.DirectoryInfo(path);
            return new ImageFileItem
            {
                Path = info.FullName,
                Name = info.Name,
                Extension = "",
                IsDirectory = true,
                CreatedUtc = info.Exists ? info.CreationTimeUtc : DateTime.MinValue,
                LastWriteUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue
            };
        }
    }

    internal sealed class ImageTabState
    {
        public string StackId { get; set; }
        public FileRevision? SavedEnhanceRevision { get; set; }
        public ManualAdjustments ManualAdjustments { get; set; }
        public bool IsPinned { get; set; }
        public List<string> SelectedPaths { get; set; }
        public List<string> ForwardFolders { get; set; }

        public ImageTabState()
        {
            ForwardFolders = new List<string>();
            SelectedPaths = new List<string>();
        }

        public BrowserSortField SortField { get; set; }
        public bool SortDescending { get; set; }
        public bool IsBrowser { get; set; }
        public string FolderPath { get; set; }
        public string SearchText { get; set; }
        public string SelectedPath { get; set; }
        public double BrowserScrollOffset { get; set; }
        public string Path { get; set; }
        public double Zoom { get; set; }
        public int RotationQuarterTurns { get; set; }
        public bool QuickEnhanceEnabled { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public bool HasCustomView { get; set; }
        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }
        public DateTime LastAccessUtc { get; set; }

        public ImageTabState Copy()
        {
            var copy = (ImageTabState)MemberwiseClone();
            copy.ForwardFolders = new List<string>(ForwardFolders);
            copy.SelectedPaths = new List<string>(SelectedPaths);
            copy.ManualAdjustments = ZonerInspiredViewer.ManualAdjustments.Copy(ManualAdjustments);
            return copy;
        }
    }

    internal sealed class MetadataRow
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    internal sealed class ImageMetadata
    {
        public string Path { get; set; }
        public string Decoder { get; set; }
        public string FileType { get; set; }
        public string BitDepth { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
        public long FileSize { get; set; }
        public DateTime ModifiedUtc { get; set; }
        public List<MetadataRow> Rows { get; private set; }

        public ImageMetadata()
        {
            Rows = new List<MetadataRow>();
        }

        public void Add(string name, object value)
        {
            if (value == null)
            {
                return;
            }

            string text = Convert.ToString(value);
            if (String.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Rows.Add(new MetadataRow { Name = name, Value = text });
        }
    }

    [DataContract]
    internal sealed class SessionState
    {
        [DataMember] public List<TabStackDto> TabStacks { get; set; }
        [DataMember] public List<BatchPreset> BatchPresets { get; set; }
        [DataMember] public bool PerformanceDetailsVisible { get; set; }
        [DataMember] public bool? GpuImagesEnabled { get; set; }
        [DataMember] public bool? GpuJpegEnabled { get; set; }
        [DataMember] public string ImageGpuKey { get; set; }
        [DataMember] public int ImageGpuBudgetMb { get; set; }
        [DataMember] public bool ZoomLocked { get; set; }
        [DataMember] public double LockedZoom { get; set; }
        [DataMember] public double SuperResolutionScale { get; set; }
        [DataMember] public string SuperResolutionModel { get; set; }
        [DataMember] public string ProcessingGpuKey { get; set; }
        [DataMember] public int ProcessingGpuLimitMb { get; set; }
        [DataMember] public string ThumbnailGpuMode { get; set; }
        [DataMember] public int ThumbnailGpuLimitMb { get; set; }
        [DataMember] public bool? WheelFilmstripEnabled { get; set; }
        [DataMember] public string WheelFilmstripPosition { get; set; }
        [DataMember] public int WheelFilmstripSize { get; set; }
        [DataMember] public List<string> SearchHistory { get; set; }
        [DataMember] public bool ManualEnhanceVisible { get; set; }
        [DataMember] public List<ShortcutOverride> ShortcutOverrides { get; set; }
        [DataMember] public string BackgroundPattern { get; set; }
        [DataMember] public string BackgroundColor { get; set; }
        [DataMember] public string BackgroundFade { get; set; }
        [DataMember] public double? BackgroundIntensity { get; set; }
        [DataMember] public bool ImageMetadataOverlayVisible { get; set; }
        [DataMember] public bool? ImageNavigatorEnabled { get; set; }
        [DataMember] public bool AutoSaveRotations { get; set; }
        [DataMember] public bool? ConfirmEnhanceOverwrite { get; set; }
        [DataMember] public string PinnedTabColor { get; set; }
        [DataMember] public bool FolderTreeHidden { get; set; }
        [DataMember] public double FolderPaneWidth { get; set; }
        [DataMember] public bool? QuickEnhanceEnabled { get; set; }
        [DataMember] public bool AdvancedQuickEnhance { get; set; }
        [DataMember] public List<string> BrowserSelectedPaths { get; set; }
        [DataMember] public double SlideshowSeconds { get; set; }
        [DataMember] public bool SlideshowShuffle { get; set; }
        [DataMember]
        public List<string> ForwardFolders { get; set; }

        [DataMember]
        public SessionTabDto LatestBrowserView { get; set; }

        [DataMember]
        public BrowserSortField SortField { get; set; }

        [DataMember]
        public bool SortDescending { get; set; }

        [DataMember]
        public string LastFolder { get; set; }

        [DataMember]
        public string SearchText { get; set; }

        [DataMember]
        public string ActiveTabPath { get; set; }

        [DataMember]
        public string ActiveTabId { get; set; }

        [DataMember]
        public double BrowserScrollOffset { get; set; }

        [DataMember]
        public string BrowserSelectedPath { get; set; }

        [DataMember]
        public int VramBudgetMb { get; set; }

        [DataMember]
        public bool LowPriorityIccEnabled { get; set; }

        [DataMember]
        public string Theme { get; set; }

        [DataMember]
        public int ThumbnailSizePixels { get; set; }

        [DataMember]
        public double ThumbnailAspectRatio { get; set; }

        [DataMember]
        public bool ThumbnailMinimapVisible { get; set; }

        [DataMember]
        public bool MetadataPanelVisible { get; set; }

        [DataMember]
        public double MetadataPanelWidth { get; set; }

        [DataMember]
        public string ActiveNamedSessionName { get; set; }

        [DataMember]
        public double WindowLeft { get; set; }

        [DataMember]
        public double WindowTop { get; set; }

        [DataMember]
        public double WindowWidth { get; set; }

        [DataMember]
        public double WindowHeight { get; set; }

        [DataMember]
        public bool WindowMaximized { get; set; }

        [DataMember]
        public bool WindowPositionSaved { get; set; }

        [DataMember]
        public List<SessionTabDto> Tabs { get; set; }

        public SessionState()
        {
            VramBudgetMb = 1024;
            Theme = "Dark";
            ThumbnailSizePixels = 128;
            ThumbnailAspectRatio = 1;
            MetadataPanelVisible = false;
            MetadataPanelWidth = 330;
            WindowWidth = 1500;
            WindowHeight = 920;
            Tabs = new List<SessionTabDto>();
        }
    }

    [DataContract]
    internal sealed class SessionTabDto
    {
        [DataMember] public string StackId { get; set; }
        [DataMember] public FileRevision? SavedEnhanceRevision { get; set; }
        [DataMember] public ManualAdjustments ManualAdjustments { get; set; }
        [DataMember] public bool IsPinned { get; set; }
        [DataMember] public List<string> SelectedPaths { get; set; }
        [DataMember]
        public List<string> ForwardFolders { get; set; }

        [DataMember]
        public bool QuickEnhanceEnabled { get; set; }

        [DataMember]
        public int RotationQuarterTurns { get; set; }

        [DataMember]
        public BrowserSortField SortField { get; set; }

        [DataMember]
        public bool SortDescending { get; set; }

        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public bool IsBrowser { get; set; }

        [DataMember]
        public string FolderPath { get; set; }

        [DataMember]
        public string SearchText { get; set; }

        [DataMember]
        public string SelectedPath { get; set; }

        [DataMember]
        public double BrowserScrollOffset { get; set; }

        [DataMember]
        public string Path { get; set; }

        [DataMember]
        public double Zoom { get; set; }

        [DataMember]
        public double OffsetX { get; set; }

        [DataMember]
        public double OffsetY { get; set; }

        [DataMember]
        public bool HasCustomView { get; set; }

        [DataMember]
        public double ViewportWidth { get; set; }

        [DataMember]
        public double ViewportHeight { get; set; }
    }

    internal sealed class NamedSessionInfo
    {
        public string Name { get; set; }
        public DateTime SavedUtc { get; set; }
    }

    [DataContract]
    internal sealed class BrowserPreferences
    {
        [DataMember]
        public List<FavoriteFolderDetails> FavoriteDetails { get; set; }
        [DataMember]
        public List<string> FavoriteFolders { get; set; }

        [DataMember]
        public List<string> ExpandedNodes { get; set; }

        public BrowserPreferences()
        {
            FavoriteFolders = new List<string>();
            FavoriteDetails = new List<FavoriteFolderDetails>();
            ExpandedNodes = new List<string> { "favorites" };
        }
    }

    [DataContract]
    internal sealed class FavoriteFolderDetails
    {
        [DataMember] public string Path { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Description { get; set; }

        public FavoriteFolderDetails Copy()
        {
            return (FavoriteFolderDetails)MemberwiseClone();
        }
    }

    [DataContract]
    internal sealed class NamedSessionDocument
    {
        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public DateTime SavedUtc { get; set; }

        [DataMember]
        public SessionState State { get; set; }
    }
}
