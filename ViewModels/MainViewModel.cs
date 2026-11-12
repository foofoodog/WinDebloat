using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using WinDebloat.Models;
using WinDebloat.Services;

namespace WinDebloat.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ScannerService _scannerService = new();
        private bool _isBusy;
        private string _progressText = "Ready";
        private double _memoryTotalGB;
        private double _memoryAvailableGB;
        private double _memoryUsedGB;
        private int _memoryPercent;
        private bool _isAdmin;
        private int _selectedCount;

        public ObservableCollection<DebloatItem> AllItems { get; } = new();
        public ObservableCollection<DebloatItem> StartupItems { get; } = new();
        public ObservableCollection<DebloatItem> BloatwareItems { get; } = new();
        public ObservableCollection<DebloatItem> SystemTweakItems { get; } = new();
        public ObservableCollection<DebloatItem> ServiceItems { get; } = new();
        public ObservableCollection<DebloatItem> UserAppItems { get; } = new();
        public ObservableCollection<DebloatItem> RevertItems { get; } = new();

        public ObservableCollection<string> Logs { get; } = new();

        public bool HasRevertItems => RevertItems.Count > 0;
        public bool HasNoRevertItems => !HasRevertItems;

        public string RevertTabHeader => RevertItems.Count > 0
            ? $"↩️ Undo / Revert ({RevertItems.Count})"
            : "↩️ Undo / Revert";

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value; OnPropertyChanged(); }
        }

        public double MemoryTotalGB
        {
            get => _memoryTotalGB;
            set { _memoryTotalGB = value; OnPropertyChanged(); }
        }

        public double MemoryAvailableGB
        {
            get => _memoryAvailableGB;
            set { _memoryAvailableGB = value; OnPropertyChanged(); }
        }

        public double MemoryUsedGB
        {
            get => _memoryUsedGB;
            set { _memoryUsedGB = value; OnPropertyChanged(); }
        }

        public int MemoryPercent
        {
            get => _memoryPercent;
            set { _memoryPercent = value; OnPropertyChanged(); }
        }

        public bool IsAdmin
        {
            get => _isAdmin;
            set 
            { 
                _isAdmin = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(IsNotAdmin));
                OnPropertyChanged(nameof(AdminBadgeText));
            }
        }

        public bool IsNotAdmin => !IsAdmin;

        public string AdminBadgeText => IsAdmin ? "Administrator (Full Access)" : "Standard User";

        public int SelectedCount
        {
            get => _selectedCount;
            set { _selectedCount = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand SelectRecommendedCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand ApplySelectedCommand { get; }
        public ICommand RelaunchAdminCommand { get; }
        public ICommand CreateRestorePointCommand { get; }
        public ICommand UndoItemCommand { get; }
        public ICommand UndoSelectedCommand { get; }
        public ICommand SelectAllRevertCommand { get; }
        public ICommand DeselectAllRevertCommand { get; }

        public MainViewModel()
        {
            RefreshCommand = new RelayCommand(async () => await RefreshAsync());
            SelectRecommendedCommand = new RelayCommand(SelectRecommended);
            SelectAllCommand = new RelayCommand(SelectAll);
            DeselectAllCommand = new RelayCommand(DeselectAll);
            ApplySelectedCommand = new RelayCommand(async () => await ApplySelectedAsync(), () => !IsBusy && SelectedCount > 0);
            RelaunchAdminCommand = new RelayCommand(RelaunchAdmin);
            CreateRestorePointCommand = new RelayCommand(async () => await CreateRestorePointAsync());
            UndoItemCommand = new RelayCommand<DebloatItem>(async item => await UndoItemAsync(item));
            UndoSelectedCommand = new RelayCommand(async () => await UndoSelectedAsync());
            SelectAllRevertCommand = new RelayCommand(() =>
            {
                foreach (var item in RevertItems) item.IsSelected = true;
            });
            DeselectAllRevertCommand = new RelayCommand(() =>
            {
                foreach (var item in RevertItems) item.IsSelected = false;
            });

            IsAdmin = SystemHelper.IsAdministrator();
            UpdateMemoryStats();
        }

        private void RelaunchAdmin()
        {
            var selectedIds = AllItems.Where(i => i.IsSelected).Select(i => i.Id).ToList();
            string args = selectedIds.Count > 0 ? $"--selected-ids \"{string.Join(",", selectedIds)}\"" : "";
            SystemHelper.RelaunchAsAdministrator(args);
        }

        public async Task InitializeAsync()
        {
            await RefreshAsync();

            // Check command line arguments for preserved selections and auto-apply
            try
            {
                var args = Environment.GetCommandLineArgs();
                bool autoApply = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].Equals("--selected-ids", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        var requestedIds = new HashSet<string>(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                        foreach (var item in AllItems)
                        {
                            item.IsSelected = requestedIds.Contains(item.Id);
                        }
                        UpdateSelectedCount();
                        AddLog($"Restored selection of {SelectedCount} items after relaunch.");
                        i++;
                    }
                    else if (args[i].Equals("--auto-apply", StringComparison.OrdinalIgnoreCase))
                    {
                        autoApply = true;
                    }
                }

                if (autoApply && SelectedCount > 0)
                {
                    AddLog("Relaunched with Administrator privileges. Applying requested items automatically...");
                    await ApplySelectedAsync(promptForElevation: false);
                }
            }
            catch (Exception ex)
            {
                AddLog($"Error parsing startup arguments: {ex.Message}");
            }
        }

        public void UpdateMemoryStats()
        {
            var mem = SystemHelper.GetMemoryInfo();
            MemoryTotalGB = mem.TotalGB;
            MemoryAvailableGB = mem.AvailableGB;
            MemoryUsedGB = mem.UsedGB;
            MemoryPercent = mem.PercentUsed;
        }

        public async Task RefreshAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            ProgressText = "Scanning machine components, startup apps, and services...";
            AddLog("Starting full system scan...");

            try
            {
                UpdateMemoryStats();

                AllItems.Clear();
                StartupItems.Clear();
                BloatwareItems.Clear();
                SystemTweakItems.Clear();
                ServiceItems.Clear();
                UserAppItems.Clear();
                RevertItems.Clear();

                var scanned = await _scannerService.ScanAllAsync();

                foreach (var item in scanned)
                {
                    item.PropertyChanged += Item_PropertyChanged;
                    AllItems.Add(item);

                    if (item.IsCompleted)
                    {
                        // Completed items with undo support go directly to the Revert tab
                        if (item.CanUndo)
                        {
                            RevertItems.Add(item);
                        }
                    }
                    else
                    {
                        switch (item.Category)
                        {
                            case DebloatCategory.Startup:
                                StartupItems.Add(item);
                                break;
                            case DebloatCategory.BloatwareApp:
                                BloatwareItems.Add(item);
                                break;
                            case DebloatCategory.SystemTweak:
                                SystemTweakItems.Add(item);
                                break;
                            case DebloatCategory.Service:
                                ServiceItems.Add(item);
                                break;
                            case DebloatCategory.UserInstalledApp:
                                UserAppItems.Add(item);
                                break;
                        }
                    }
                }

                UpdateSelectedCount();
                OnPropertyChanged(nameof(RevertTabHeader));
                OnPropertyChanged(nameof(HasRevertItems));
                OnPropertyChanged(nameof(HasNoRevertItems));
                AddLog($"Scan complete. Found {AllItems.Count} total items ({StartupItems.Count} startup, {BloatwareItems.Count} bloatware, {SystemTweakItems.Count} tweaks, {ServiceItems.Count} services, {UserAppItems.Count} user apps, {RevertItems.Count} reversible in Undo tab).");
            }
            catch (Exception ex)
            {
                AddLog($"Error during scan: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                ProgressText = "Ready";
            }
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DebloatItem.IsSelected))
            {
                UpdateSelectedCount();
            }
        }

        private void UpdateSelectedCount()
        {
            SelectedCount = AllItems.Count(i => i.IsSelected && !i.IsCompleted);
        }

        private void SelectRecommended()
        {
            foreach (var item in AllItems.Where(i => !i.IsCompleted))
            {
                item.IsSelected = item.IsRecommended;
            }
            UpdateSelectedCount();
            AddLog("Selected all recommended safe debloat options.");
        }

        private void SelectAll()
        {
            foreach (var item in AllItems.Where(i => !i.IsCompleted))
            {
                item.IsSelected = true;
            }
            UpdateSelectedCount();
            AddLog("Selected all pending items.");
        }

        private void DeselectAll()
        {
            foreach (var item in AllItems.Where(i => !i.IsCompleted))
            {
                item.IsSelected = false;
            }
            UpdateSelectedCount();
            AddLog("Deselected all candidate items.");
        }

        public async Task ApplySelectedAsync(bool promptForElevation = true)
        {
            var selected = AllItems.Where(i => i.IsSelected && !i.IsCompleted).ToList();
            if (selected.Count == 0) return;

            // Prompt for elevation if any selected item requires Administrator privileges
            if (promptForElevation && !IsAdmin && selected.Any(i => i.RequiresAdmin))
            {
                var adminItems = selected.Where(i => i.RequiresAdmin).ToList();
                var adminNames = string.Join("\n • ", adminItems.Select(i => i.Title));

                var promptResult = ModernDialog.ShowPrompt(
                    "Administrator Elevation Required",
                    $"The following {adminItems.Count} selected item(s) require Administrator privileges to apply:\n\n • {adminNames}\n\nWould you like to relaunch as Administrator now to apply all selected items?",
                    primaryText: "🛡️ Relaunch as Admin",
                    secondaryText: "Apply User-Only Items",
                    cancelText: "Cancel",
                    type: ModernDialogType.Shield);

                if (promptResult == ModernDialogResult.Yes)
                {
                    string selectedIds = string.Join(",", selected.Select(i => i.Id));
                    AddLog("Requesting elevation to apply administrator-level debloat actions...");
                    bool launched = SystemHelper.RelaunchAsAdministrator($"--selected-ids \"{selectedIds}\" --auto-apply");
                    if (!launched)
                    {
                        AddLog("Elevation request was cancelled by the user.");
                    }
                    return;
                }
                else if (promptResult == ModernDialogResult.No)
                {
                    selected = selected.Where(i => !i.RequiresAdmin).ToList();
                    if (selected.Count == 0)
                    {
                        AddLog("Operation cancelled: all selected items require Administrator privileges.");
                        return;
                    }
                    AddLog($"Proceeding with {selected.Count} user-level items (skipping {adminItems.Count} administrator items).");
                }
                else
                {
                    AddLog("Operation cancelled.");
                    return;
                }
            }

            // Prompt to create an optional restore point before continuing removal
            var restoreChoice = ModernDialog.ShowPrompt(
                "Create System Restore Point?",
                $"You are about to optimize or remove {selected.Count} selected item(s).\n\n" +
                "Would you like to create an optional System Restore Point before proceeding?\n\n" +
                "Creating a snapshot allows Windows to restore your previous configuration if needed.",
                primaryText: "🛡️ Create Restore Point & Continue",
                secondaryText: "Skip & Continue",
                cancelText: "Cancel",
                type: ModernDialogType.Shield);

            if (restoreChoice == ModernDialogResult.Cancel)
            {
                AddLog("Debloat execution cancelled by user.");
                return;
            }

            if (restoreChoice == ModernDialogResult.Yes)
            {
                AddLog("Creating pre-removal System Restore Point...");
                ProgressText = "Creating System Restore Point...";
                var (rpOk, rpMsg) = await SystemHelper.CreateRestorePoint("WinDebloat Pre-Debloat Snapshot");
                if (rpOk)
                {
                    AddLog("System Restore Point created successfully.");
                }
                else
                {
                    AddLog($"System Restore Point notification: {rpMsg}. Proceeding with removal...");
                }
            }

            IsBusy = true;
            ProgressText = "Applying selected actions...";
            AddLog($"Beginning execution of {selected.Count} actions...");

            int successCount = 0;
            int failCount = 0;

            var progress = new Progress<string>(msg =>
            {
                AddLog(msg);
                ProgressText = msg;
            });

            foreach (var item in selected)
            {
                if (item.ApplyAction != null)
                {
                    try
                    {
                        item.StatusMessage = "Processing...";
                        bool ok = await item.ApplyAction(progress);
                        if (ok)
                        {
                            item.IsCompleted = true;
                            item.HasError = false;
                            item.StatusMessage = "Completed";
                            item.IsSelected = false;
                            successCount++;

                            // Remove from active candidate tabs and move to Undo / Revert tab if supported
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                switch (item.Category)
                                {
                                    case DebloatCategory.Startup:
                                        StartupItems.Remove(item);
                                        break;
                                    case DebloatCategory.BloatwareApp:
                                        BloatwareItems.Remove(item);
                                        break;
                                    case DebloatCategory.SystemTweak:
                                        SystemTweakItems.Remove(item);
                                        break;
                                    case DebloatCategory.Service:
                                        ServiceItems.Remove(item);
                                        break;
                                    case DebloatCategory.UserInstalledApp:
                                        UserAppItems.Remove(item);
                                        break;
                                }

                                if (item.CanUndo && !RevertItems.Contains(item))
                                {
                                    RevertItems.Add(item);
                                }
                            });
                        }
                        else
                        {
                            item.HasError = true;
                            item.StatusMessage = "Failed";
                            failCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        item.HasError = true;
                        item.StatusMessage = $"Error: {ex.Message}";
                        AddLog($"Error processing '{item.Title}': {ex.Message}");
                        failCount++;
                    }
                }
            }

            UpdateMemoryStats();
            UpdateSelectedCount();
            OnPropertyChanged(nameof(RevertTabHeader));
            OnPropertyChanged(nameof(HasRevertItems));
            OnPropertyChanged(nameof(HasNoRevertItems));
            IsBusy = false;
            ProgressText = "Done";

            bool rebootRequired = selected.Any(i => i.IsCompleted && i.RequiresReboot);
            AddLog($"Finished execution. Success: {successCount}, Failed: {failCount}. New Free RAM: {MemoryAvailableGB} GB (Used: {MemoryUsedGB} GB).");
            if (rebootRequired)
            {
                AddLog("NOTICE: A system reboot or sign-out is required for Windows Explorer and Start Menu tweaks to take full visual and memory effect.");
            }

            if (rebootRequired)
            {
                var choice = ModernDialog.ShowPrompt(
                    "Optimization Complete — Restart Required",
                    $"Successfully processed and optimized {successCount} item(s)!" +
                    (failCount > 0 ? $"\nFailed: {failCount}" : "") +
                    $"\n\n• Current Available RAM: {MemoryAvailableGB} GB\n\n" +
                    "⚠️ REBOOT REQUIRED:\nOne or more applied items (such as Windows Explorer, Start Menu search, or shell policies) require a system restart or sign-out to take full visual and memory effect.",
                    primaryText: "🔄 Restart Computer Now",
                    secondaryText: "Restart Later",
                    type: ModernDialogType.Reboot);

                if (choice == ModernDialogResult.Yes)
                {
                    AddLog("Initiating system restart requested by user...");
                    await SystemHelper.RunProcessAsync("shutdown.exe", "/r /t 5 /c \"Restarting computer to apply Windows debloat changes...\"");
                }
            }
            else
            {
                ModernDialog.ShowInfo(
                    "Optimization Complete",
                    $"Successfully processed {successCount} item(s)!" +
                    (failCount > 0 ? $"\nFailed: {failCount}" : "") +
                    $"\n\nCurrent Available RAM: {MemoryAvailableGB} GB (Used: {MemoryUsedGB} GB).",
                    type: ModernDialogType.Success);
            }
        }

        public async Task CreateRestorePointAsync()
        {
            if (!IsAdmin)
            {
                var res = ModernDialog.ShowPrompt(
                    "Administrator Required",
                    "Creating a System Restore Point requires Administrator privileges.\n\nWould you like to relaunch as Administrator now?",
                    primaryText: "🛡️ Relaunch as Admin",
                    secondaryText: "Cancel",
                    type: ModernDialogType.Shield);
                if (res == ModernDialogResult.Yes)
                {
                    RelaunchAdmin();
                }
                return;
            }

            ProgressText = "Creating System Restore Point...";
            AddLog("Creating System Restore Point 'WinDebloat Pre-Debloat Snapshot'...");
            var (ok, msg) = await SystemHelper.CreateRestorePoint("WinDebloat Snapshot");
            if (ok)
            {
                AddLog("System Restore Point 'WinDebloat Snapshot' created successfully.");
                ModernDialog.ShowInfo("Restore Point Created", "System Restore Point 'WinDebloat Snapshot' was successfully created.\n\nYou can restore your machine to this snapshot at any time via Windows System Restore.", ModernDialogType.Success);
            }
            else
            {
                AddLog($"Restore point result: {msg}");
                ModernDialog.ShowInfo("System Restore Point", $"Restore Point notification:\n\n{msg}\n\nEnsure System Protection is enabled in Windows System Properties.", ModernDialogType.Info);
            }
            ProgressText = "Ready";
        }

        public async Task UndoItemAsync(DebloatItem? item)
        {
            if (item == null || item.UndoAction == null) return;

            bool confirm = ModernDialog.ShowConfirm(
                "Confirm Revert",
                $"Are you sure you want to revert changes for '{item.Title}' and restore it to its active configuration?",
                confirmText: "↩️ Revert Changes",
                cancelText: "Cancel",
                type: ModernDialogType.Warning);

            if (!confirm) return;

            IsBusy = true;
            ProgressText = $"Reverting {item.Title}...";
            AddLog($"Reverting '{item.Title}'...");

            var progress = new Progress<string>(msg =>
            {
                AddLog(msg);
                ProgressText = msg;
            });

            try
            {
                bool ok = await item.UndoAction(progress);
                if (ok)
                {
                    item.IsCompleted = false;
                    item.StatusMessage = "Ready";
                    item.IsSelected = false;

                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        RevertItems.Remove(item);
                        switch (item.Category)
                        {
                            case DebloatCategory.Startup:
                                StartupItems.Add(item);
                                break;
                            case DebloatCategory.BloatwareApp:
                                BloatwareItems.Add(item);
                                break;
                            case DebloatCategory.SystemTweak:
                                SystemTweakItems.Add(item);
                                break;
                            case DebloatCategory.Service:
                                ServiceItems.Add(item);
                                break;
                            case DebloatCategory.UserInstalledApp:
                                UserAppItems.Add(item);
                                break;
                        }
                    });

                    AddLog($"Successfully reverted '{item.Title}'.");
                    ModernDialog.ShowInfo("Item Restored", $"'{item.Title}' was successfully restored to its previous configuration.", ModernDialogType.Success);
                }
                else
                {
                    AddLog($"Failed to revert '{item.Title}'.");
                }
            }
            catch (Exception ex)
            {
                AddLog($"Error reverting '{item.Title}': {ex.Message}");
            }
            finally
            {
                UpdateMemoryStats();
                UpdateSelectedCount();
                OnPropertyChanged(nameof(RevertTabHeader));
                OnPropertyChanged(nameof(HasRevertItems));
                OnPropertyChanged(nameof(HasNoRevertItems));
                IsBusy = false;
                ProgressText = "Ready";
            }
        }

        public async Task UndoSelectedAsync()
        {
            var toUndo = RevertItems.Where(i => i.IsSelected && i.CanUndo).ToList();
            if (toUndo.Count == 0)
            {
                ModernDialog.ShowInfo("No Items Selected", "Please select one or more items in the Undo / Revert tab to revert.", ModernDialogType.Info);
                return;
            }

            bool confirm = ModernDialog.ShowConfirm(
                "Confirm Revert",
                $"Are you sure you want to revert {toUndo.Count} selected item(s) back to their active configuration?",
                confirmText: "↩️ Revert Selected",
                cancelText: "Cancel",
                type: ModernDialogType.Warning);

            if (!confirm) return;

            IsBusy = true;
            ProgressText = "Reverting selected items...";
            int reverted = 0;

            var progress = new Progress<string>(msg =>
            {
                AddLog(msg);
                ProgressText = msg;
            });

            foreach (var item in toUndo)
            {
                if (item.UndoAction != null)
                {
                    try
                    {
                        bool ok = await item.UndoAction(progress);
                        if (ok)
                        {
                            item.IsCompleted = false;
                            item.StatusMessage = "Ready";
                            item.IsSelected = false;

                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                RevertItems.Remove(item);
                                switch (item.Category)
                                {
                                    case DebloatCategory.Startup:
                                        StartupItems.Add(item);
                                        break;
                                    case DebloatCategory.BloatwareApp:
                                        BloatwareItems.Add(item);
                                        break;
                                    case DebloatCategory.SystemTweak:
                                        SystemTweakItems.Add(item);
                                        break;
                                    case DebloatCategory.Service:
                                        ServiceItems.Add(item);
                                        break;
                                    case DebloatCategory.UserInstalledApp:
                                        UserAppItems.Add(item);
                                        break;
                                }
                            });

                            reverted++;
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"Error reverting '{item.Title}': {ex.Message}");
                    }
                }
            }

            UpdateMemoryStats();
            UpdateSelectedCount();
            OnPropertyChanged(nameof(RevertTabHeader));
            OnPropertyChanged(nameof(HasRevertItems));
            OnPropertyChanged(nameof(HasNoRevertItems));
            IsBusy = false;
            ProgressText = "Done";
            AddLog($"Finished reverting {reverted} item(s).");
            ModernDialog.ShowInfo("Revert Complete", $"Successfully reverted {reverted} item(s) back to their active configuration.", ModernDialogType.Success);
        }

        private void AddLog(string message)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Logs.Insert(0, $"[{time}] {message}");
                if (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}