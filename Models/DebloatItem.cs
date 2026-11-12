using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace WinDebloat.Models
{
    public enum DebloatCategory
    {
        Startup,
        BloatwareApp,
        SystemTweak,
        Service,
        UserInstalledApp
    }

    public class DebloatItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _statusMessage = "Ready";
        private bool _isCompleted;
        private bool _hasError;

        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string EstimatedMemory { get; set; } = string.Empty;
        public DebloatCategory Category { get; set; }
        public bool IsRecommended { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool RequiresReboot { get; set; }
        public bool IsKnownSuspect { get; set; }
        public object? Tag { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set 
            { 
                _isCompleted = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(CanUndoAndCompleted));
            }
        }

        public bool HasError
        {
            get => _hasError;
            set { _hasError = value; OnPropertyChanged(); }
        }

        // Action delegate to apply this debloat item
        public Func<IProgress<string>, Task<bool>>? ApplyAction { get; set; }

        // Action delegate to revert/undo this debloat item
        public Func<IProgress<string>, Task<bool>>? UndoAction { get; set; }

        public bool CanUndo => UndoAction != null;

        public bool CanUndoAndCompleted => IsCompleted && CanUndo;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
