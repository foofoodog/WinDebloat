using System;

namespace WinDebloat.Models
{
    public class UndoRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public DateTime AppliedAt { get; set; } = DateTime.Now;
        public string RevertType { get; set; } = string.Empty;

        // Payload data for restoring configuration
        public string? TargetKey { get; set; }
        public string? TargetValueName { get; set; }
        public string? OriginalStringData { get; set; }
        public int? OriginalDWordData { get; set; }
        public string? ServiceName { get; set; }
        public string? OriginalServiceStartup { get; set; }
    }
}
