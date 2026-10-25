using System;

namespace AeroSpot.Core.Search
{
    public sealed class IndexedItem
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string Category { get; set; } = "File";
        public string Extension { get; set; } = string.Empty;
        public string ExecutionTarget { get; set; } = string.Empty;
        public long LastModifiedUtcTicks { get; set; }
        public bool IsApp { get; set; }
        public int SearchScore { get; set; }

        public IndexedItem() { }

        public IndexedItem(string title, string fullPath, string category, string executionTarget, bool isApp = false, string? extension = null, long lastModifiedUtcTicks = 0)
        {
            Title = title;
            FullPath = fullPath;
            Category = category;
            ExecutionTarget = executionTarget;
            IsApp = isApp;
            Extension = extension ?? (fullPath.Contains('.') ? System.IO.Path.GetExtension(fullPath).ToLowerInvariant() : string.Empty);
            LastModifiedUtcTicks = lastModifiedUtcTicks;
        }

        public override string ToString() => $"[{Category}] {Title} ({FullPath})";
    }
}
