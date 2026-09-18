using System.Collections.Generic;

namespace Myoken.Core
{
    // A new portable DTO, not the legacy Windows profile format.
    public sealed class SessionState
    {
        public int SchemaVersion { get; set; } = 1;
        public string FolderPath { get; set; } = string.Empty;
        public List<string> ImagePaths { get; set; } = new List<string>();
        public string? ActiveImagePath { get; set; }
    }
}
