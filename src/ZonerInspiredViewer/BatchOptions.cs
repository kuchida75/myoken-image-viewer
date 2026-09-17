using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace ZonerInspiredViewer
{
    internal enum BatchKind { Rename, Resize, Convert }
    internal enum BatchConflict { KeepBoth, Skip, Stop, Overwrite }
    internal enum BatchResizeMode { Pixels, Percentage, PrintSize, LongEdge, ShortEdge }
    internal enum BatchDirection { Both, ReduceOnly, EnlargeOnly }
    internal enum BatchFit { WidthAndHeight, Width, Height }
    internal enum BatchCase { Unchanged, Lowercase, Uppercase, TitleCase }

    [DataContract]
    internal sealed class BatchOptions
    {
        [DataMember] public BatchKind Kind;
        [DataMember] public bool UseTemplate = true;
        [DataMember] public string Template = "*_##";
        [DataMember] public int Start = 1;
        [DataMember] public int Step = 1;
        [DataMember] public bool Letters;
        [DataMember] public string Find = "", Replace = "", Prefix = "", Suffix = "", RemoveText = "";
        [DataMember] public bool MatchCase, StripSpaces;
        [DataMember] public int RemoveStart, RemoveEnd;
        [DataMember] public BatchCase Case;
        [DataMember] public BatchResizeMode ResizeMode;
        [DataMember] public double Width = 1920, Height = 1080, Percent = 50, PrintWidth = 6, PrintHeight = 4, Dpi = 300;
        [DataMember] public bool PrintCentimeters, PreserveAspect = true;
        [DataMember] public BatchDirection Direction = BatchDirection.ReduceOnly;
        [DataMember] public BatchFit Fit;
        [DataMember] public string Format = ".jpg";
        [DataMember] public bool KeepFormat = true;
        [DataMember] public int Quality = 95;
        [DataMember] public bool Lossless, PreserveMetadata = true, PreserveDates = true;
        [DataMember] public string OutputFolder = "", Subfolder = "", OutputSuffix = "";
        [DataMember] public BatchConflict Conflict = BatchConflict.KeepBoth;

        internal BatchOptions Copy() { return (BatchOptions)MemberwiseClone(); }
        internal static BatchOptions Default(BatchKind kind)
        { return new BatchOptions { Kind = kind, Conflict = kind == BatchKind.Rename ? BatchConflict.Stop : BatchConflict.KeepBoth, OutputSuffix = kind == BatchKind.Resize ? "_resized" : "" }; }
        internal void Validate()
        {
            foreach (var value in new[] { Template, Find, Replace, Prefix, Suffix, RemoveText, OutputFolder, Subfolder, OutputSuffix, Format })
                if (value == null || value.Length > 512) throw new InvalidDataException("A batch option is missing or too long.");
            if (!Enum.IsDefined(typeof(BatchKind), Kind) || !Enum.IsDefined(typeof(BatchConflict), Conflict)
                || !Enum.IsDefined(typeof(BatchResizeMode), ResizeMode) || !Enum.IsDefined(typeof(BatchDirection), Direction)
                || !Enum.IsDefined(typeof(BatchFit), Fit) || !Enum.IsDefined(typeof(BatchCase), Case)) throw new InvalidDataException("Invalid batch option.");
            if (Start < 0 || Start > 1000000000 || Step < 1 || Step > 1000000 || RemoveStart < 0 || RemoveEnd < 0
                || RemoveStart > 255 || RemoveEnd > 255 || Quality < 1 || Quality > 100) throw new InvalidDataException("A number is outside its allowed range.");
            foreach (double size in new[] { Width, Height }) if (Double.IsNaN(size) || size < 1 || size > 65536) throw new InvalidDataException("Pixel dimensions must be 1-65536.");
            if (Double.IsNaN(Percent) || Percent < 1 || Percent > 1000 || Double.IsNaN(PrintWidth) || PrintWidth <= 0 || PrintWidth > 1000
                || Double.IsNaN(PrintHeight) || PrintHeight <= 0 || PrintHeight > 1000 || Double.IsNaN(Dpi) || Dpi < 1 || Dpi > 2400)
                throw new InvalidDataException("Invalid percentage, print size or resolution.");
            if (!String.IsNullOrWhiteSpace(OutputFolder) && !Path.IsPathRooted(OutputFolder)) throw new InvalidDataException("Choose an absolute output folder.");
            if (Subfolder.Length > 0) BatchPlan.ValidateName(Subfolder);
            if (OutputSuffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("Output suffix contains invalid filename characters.");
            if (Kind == BatchKind.Rename && Conflict == BatchConflict.Overwrite) throw new InvalidDataException("Batch rename never overwrites another item.");
            if (!BatchImages.Extensions.Contains(Format, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Unsupported output format.");
            Format = Format.ToLowerInvariant();
        }
    }

    [DataContract]
    internal sealed class BatchPreset
    {
        [DataMember] public string Name;
        [DataMember] public BatchOptions Options;
        public override string ToString() { return Name; }
        internal BatchPreset Copy() { return new BatchPreset { Name = Name, Options = Options.Copy() }; }
        internal static bool IsValid(BatchPreset preset)
        {
            try { if (preset == null || String.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 80 || preset.Options == null) return false; preset.Options.Validate(); return true; }
            catch (Exception) { return false; }
        }
    }

    internal sealed class BatchItem : INotifyPropertyChanged
    {
        public string Source { get; set; }
        public string Destination { get; set; }
        public string CurrentName { get { return Path.GetFileName(Source); } }
        public string NewName { get { return Destination == null ? "" : Path.GetFileName(Destination); } }
        public string Dimensions { get; set; }
        public bool IsDirectory, Ready, Overwrite;
        public int Width, Height, OutputWidth, OutputHeight;
        public FileRevision Revision, DestinationRevision;
        public DateTime DirectoryModified;
        private string _status;
        private bool _included = true;
        public bool Included { get { return _included; } set { _included = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Included")); } }
        public string Status { get { return _status; } set { _status = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Status")); } }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    internal sealed class BatchResult
    {
        public readonly List<TransferredItem> Completed = new List<TransferredItem>();
        public readonly List<string> Errors = new List<string>();
        public string Journal;
        public bool Canceled;
        public int Skipped;
    }
}
