using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Win32;
using WinDebloat.Models;

namespace WinDebloat.Services
{
    public class ScannerService
    {
        public async Task<List<DebloatItem>> ScanAllAsync()
        {
            var items = new List<DebloatItem>();

            // 1. Startup Items
            items.AddRange(ScanStartupItems());

            // 2. Windows Apps (Bloatware)
            items.AddRange(await ScanAppxPackagesAsync());

            // 3. System Tweaks
            items.AddRange(await ScanSystemTweaksAsync());

            // 4. Windows Services
            items.AddRange(ScanWindowsServices());

            // 5. User Installed Software
            items.AddRange(ScanUserInstalledApps());

            return items;
        }

        public List<DebloatItem> ScanStartupItems()
        {
            var list = new List<DebloatItem>();

            // HKCU Run
            const string hkcuRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using (var key = Registry.CurrentUser.OpenSubKey(hkcuRunKey))
            {
                if (key != null)
                {
                    foreach (var valName in key.GetValueNames())
                    {
                        var command = key.GetValue(valName)?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(command) && string.IsNullOrWhiteSpace(valName)) continue;

                        string appName = valName;
                        string estRam = "~40 - 150 MB";
                        bool recommended = true;

                        if (valName.StartsWith("GoogleChromeAutoLaunch", StringComparison.OrdinalIgnoreCase))
                        {
                            appName = "Google Chrome (Silent Auto-launch)";
                            estRam = "~200 MB";
                        }
                        else if (valName.StartsWith("MicrosoftEdgeAutoLaunch", StringComparison.OrdinalIgnoreCase))
                        {
                            appName = "Microsoft Edge (Silent Auto-launch)";
                            estRam = "~150 MB";
                        }
                        else if (valName.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                        {
                            appName = "Steam Client";
                            estRam = "~120 MB";
                            recommended = false; // user might game often
                        }
                        else if (valName.Equals("GogGalaxy", StringComparison.OrdinalIgnoreCase))
                        {
                            appName = "GOG Galaxy Client";
                            estRam = "~100 MB";
                            recommended = false;
                        }
                        else if (valName.Equals("Teams", StringComparison.OrdinalIgnoreCase))
                        {
                            appName = "Microsoft Teams";
                            estRam = "~250 MB";
                            recommended = true;
                        }
                        else if (valName.Equals("GarminExpress", StringComparison.OrdinalIgnoreCase))
                        {
                            appName = "Garmin Express";
                            estRam = "~60 MB";
                            recommended = true;
                        }
                        else if (valName.Equals("Docker Desktop", StringComparison.OrdinalIgnoreCase))
                        {
                            appName = "Docker Desktop";
                            estRam = "~400+ MB";
                            recommended = false;
                        }
                        else if (valName.Equals("Studio.QuickLook", StringComparison.OrdinalIgnoreCase))
                        {
                            appName = "Insta360 Studio QuickLook";
                            estRam = "~50 MB";
                            recommended = true;
                        }

                        string currentValName = valName;
                        string originalCmd = command;

                        string startupStatus = "Enabled";
                        try
                        {
                            using var approvedKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
                            if (approvedKey != null)
                            {
                                var bytes = approvedKey.GetValue(valName) as byte[];
                                if (bytes != null && bytes.Length > 0 && (bytes[0] == 0x03 || bytes[0] == 0x01))
                                {
                                    startupStatus = "Disabled in Task Manager";
                                }
                            }
                        }
                        catch { }

                        // Consistent with Services: If already disabled in Task Manager / Settings, it uses 0 MB on boot; skip it
                        if (startupStatus == "Disabled in Task Manager")
                        {
                            continue;
                        }

                        list.Add(new DebloatItem
                        {
                            Id = $"startup_hkcu_{currentValName}",
                            Category = DebloatCategory.Startup,
                            Title = appName,
                            Description = "Starts automatically in the background at user login.",
                            Details = $"Registry: HKCU\\...\\Run -> \"{currentValName}\" = {command}",
                            EstimatedMemory = estRam,
                            StartupStatus = startupStatus,
                            Consequences = "Prevents this application from starting automatically when Windows boots. You can still launch it manually from the Start menu or desktop at any time.",
                            HelpUrl = "https://support.microsoft.com/en-us/windows/experience/startup-boot/configure-startup-applications-in-windows",
                            IsRecommended = recommended,
                            IsSelected = recommended,
                            Tag = originalCmd,
                            UndoHistoryEntry = new UndoRecord
                            {
                                Id = $"startup_hkcu_{currentValName}",
                                Title = appName,
                                Description = "Removed from login startup entries.",
                                Category = nameof(DebloatCategory.Startup),
                                RevertType = "StartupRegistry",
                                TargetKey = hkcuRunKey,
                                TargetValueName = currentValName,
                                OriginalStringData = originalCmd
                            },
                            ApplyAction = async (progress) =>
                            {
                                progress.Report($"Removing startup entry: {currentValName}...");
                                bool deleted = SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, hkcuRunKey, currentValName);
                                if (deleted)
                                {
                                    progress.Report($"Successfully removed {currentValName} from startup.");
                                    return true;
                                }
                                progress.Report($"Failed or entry {currentValName} was already removed.");
                                return true;
                            },
                            UndoAction = async (progress) =>
                            {
                                progress.Report($"Restoring startup entry: {currentValName}...");
                                bool restored = SystemHelper.SetRegistryString(RegistryHive.CurrentUser, hkcuRunKey, currentValName, originalCmd);
                                progress.Report(restored ? $"Restored {currentValName} to startup." : "Failed to restore entry.");
                                return restored;
                            }
                        });
                    }
                }
            }

            // User Startup Folder
            string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(userStartup))
            {
                foreach (var file in Directory.GetFiles(userStartup, "*.lnk"))
                {
                    string fileName = Path.GetFileName(file);
                    list.Add(new DebloatItem
                    {
                        Id = $"startup_userfolder_{fileName}",
                        Category = DebloatCategory.Startup,
                        Title = Path.GetFileNameWithoutExtension(fileName),
                        Description = "Shortcut located in User Startup folder.",
                        Details = file,
                        EstimatedMemory = "~50 MB",
                        Consequences = "Deletes the startup shortcut from your user Startup folder so this program won't start automatically on boot. The installed application is not deleted and can still be run manually.",
                        HelpUrl = "https://support.microsoft.com/en-us/windows/experience/startup-boot/configure-startup-applications-in-windows",
                        IsRecommended = true,
                        IsSelected = true,
                        ApplyAction = async (progress) =>
                        {
                            progress.Report($"Removing startup shortcut: {fileName}...");
                            try
                            {
                                File.Delete(file);
                                progress.Report($"Deleted {fileName}.");
                                return true;
                            }
                            catch (Exception ex)
                            {
                                progress.Report($"Error deleting {fileName}: {ex.Message}");
                                return false;
                            }
                        }
                    });
                }
            }

            // Common Startup Folder
            string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            if (Directory.Exists(commonStartup))
            {
                foreach (var file in Directory.GetFiles(commonStartup, "*.lnk"))
                {
                    string fileName = Path.GetFileName(file);
                    list.Add(new DebloatItem
                    {
                        Id = $"startup_commonfolder_{fileName}",
                        Category = DebloatCategory.Startup,
                        Title = Path.GetFileNameWithoutExtension(fileName) + " (All Users)",
                        Description = "Shortcut located in Common Startup folder.",
                        Details = file,
                        EstimatedMemory = "~70 MB",
                        Consequences = "Deletes the startup shortcut from the All-Users Common Startup folder so this program won't start automatically for any user on boot. The installed application is not deleted.",
                        HelpUrl = "https://support.microsoft.com/en-us/windows/experience/startup-boot/configure-startup-applications-in-windows",
                        IsRecommended = false,
                        IsSelected = false,
                        RequiresAdmin = true,
                        ApplyAction = async (progress) =>
                        {
                            progress.Report($"Removing startup shortcut: {fileName}...");
                            try
                            {
                                File.Delete(file);
                                progress.Report($"Deleted {fileName}.");
                                return true;
                            }
                            catch
                            {
                                var (ok, outMsg) = await SystemHelper.RunPowerShellCommandAsync($"Remove-Item -Path '{file}' -Force", elevated: true);
                                progress.Report(ok ? $"Deleted {fileName}." : $"Failed: {outMsg}");
                                return ok;
                            }
                        }
                    });
                }
            }

            return list;
        }

        public async Task<List<DebloatItem>> ScanAppxPackagesAsync()
        {
            var list = new List<DebloatItem>();

            var candidateApps = new[]
            {
                // Windows Telemetry & Feeds
                new { Id = "Microsoft.BingSearch", Title = "Bing Search App", EstRam = "~150 MB", Recommended = true, Desc = "Background Bing search client." },
                new { Id = "Microsoft.StartExperiencesApp", Title = "Start Experiences App", EstRam = "~45 MB", Recommended = true, Desc = "Pushes news, weather and suggestions into Start menu." },
                new { Id = "MicrosoftWindows.Client.WebExperience", Title = "Windows Web Experience (Widgets)", EstRam = "~90 MB", Recommended = true, Desc = "Powers Windows 11 Widgets and MSN Feed." },
                new { Id = "Microsoft.YourPhone", Title = "Phone Link (Your Phone)", EstRam = "~60 MB", Recommended = true, Desc = "Syncs Android/iOS phone data in the background." },
                new { Id = "Microsoft.GetHelp", Title = "Get Help", EstRam = "~20 MB", Recommended = true, Desc = "Microsoft Help support app." },
                new { Id = "Microsoft.Getstarted", Title = "Tips (Get Started)", EstRam = "~25 MB", Recommended = true, Desc = "Windows tips and introductory tour app." },
                new { Id = "Microsoft.WindowsFeedbackHub", Title = "Feedback Hub", EstRam = "~35 MB", Recommended = true, Desc = "Sends user feedback and usage logs to Microsoft." },
                new { Id = "Microsoft.549981C3F5F10", Title = "Cortana App", EstRam = "~50 MB", Recommended = true, Desc = "Legacy Cortana voice assistant app." },
                new { Id = "Microsoft.Copilot", Title = "Microsoft Copilot", EstRam = "~80 MB", Recommended = true, Desc = "Embedded Copilot web app." },

                // News, Finance, Sports
                new { Id = "Microsoft.BingNews", Title = "Microsoft News", EstRam = "~60 MB", Recommended = true, Desc = "MSN News app preinstalled by Windows." },
                new { Id = "Microsoft.BingWeather", Title = "MSN Weather", EstRam = "~45 MB", Recommended = false, Desc = "Live weather tracker and forecast feed." },
                new { Id = "Microsoft.BingFinance", Title = "MSN Money / Finance", EstRam = "~40 MB", Recommended = true, Desc = "Financial news and stock tracker." },
                new { Id = "Microsoft.BingSports", Title = "MSN Sports", EstRam = "~40 MB", Recommended = true, Desc = "Sports feed and updates." },

                // Preinstalled Media & Hubs
                new { Id = "Microsoft.ZuneMusic", Title = "Media Player (Legacy Groove)", EstRam = "~30 MB", Recommended = false, Desc = "Built-in media player." },
                new { Id = "Microsoft.ZuneVideo", Title = "Films & TV / Movies & TV", EstRam = "~35 MB", Recommended = false, Desc = "Video player app with Microsoft Store integration." },
                new { Id = "Clipchamp.Clipchamp", Title = "Clipchamp Video Editor", EstRam = "~60 MB", Recommended = false, Desc = "Preloaded cloud video editor." },
                new { Id = "Microsoft.MicrosoftOfficeHub", Title = "Microsoft 365 (Office Hub)", EstRam = "~40 MB", Recommended = true, Desc = "Office trial and promo launcher app." },
                new { Id = "Microsoft.People", Title = "Microsoft People", EstRam = "~30 MB", Recommended = true, Desc = "Contacts and address book client." },
                new { Id = "Microsoft.SkypeApp", Title = "Skype", EstRam = "~100 MB", Recommended = true, Desc = "Preinstalled Skype client." },
                new { Id = "Microsoft.WindowsMaps", Title = "Windows Maps", EstRam = "~40 MB", Recommended = true, Desc = "Built-in offline maps application." },
                new { Id = "Microsoft.MicrosoftSolitaireCollection", Title = "Microsoft Solitaire Collection", EstRam = "~70 MB", Recommended = false, Desc = "Preinstalled casual card games." },
                new { Id = "Microsoft.Windows.DevHome", Title = "Windows Dev Home", EstRam = "~50 MB", Recommended = false, Desc = "Developer Dashboard application." },

                // Preinstalled Sponsored Stubs
                new { Id = "SpotifyAB.SpotifyMusic", Title = "Spotify Music (Preload)", EstRam = "~120 MB", Recommended = false, Desc = "Preinstalled Spotify desktop client." },
                new { Id = "ByteDance.TikTok", Title = "TikTok (Preload)", EstRam = "~60 MB", Recommended = true, Desc = "Sponsored TikTok web stub." },
                new { Id = "Facebook.Instagram", Title = "Instagram (Preload)", EstRam = "~50 MB", Recommended = true, Desc = "Sponsored Instagram stub." },
                new { Id = "Disney.DisneyPlus", Title = "Disney+ (Preload)", EstRam = "~50 MB", Recommended = true, Desc = "Sponsored Disney+ streaming stub." },

                // Gaming Overlays
                new { Id = "Microsoft.XboxGamingOverlay", Title = "Xbox Game Bar Overlay", EstRam = "~80 MB", Recommended = false, Desc = "In-game overlay toolbar (Win+G)." },
                new { Id = "Microsoft.XboxSpeechToTextOverlay", Title = "Xbox Speech-To-Text", EstRam = "~30 MB", Recommended = true, Desc = "Voice-to-text overlay service for games." },
                new { Id = "Microsoft.XboxApp", Title = "Xbox App", EstRam = "~90 MB", Recommended = false, Desc = "Xbox PC gaming hub." },

                // OEM Preinstalled Bloatware
                new { Id = "AD2F1837.HPPrinterControl", Title = "HP Smart / Printer Control", EstRam = "~60 MB", Recommended = false, Desc = "HP printer dashboard and background services." },
                new { Id = "AD2F1837.HPPrinterDriver", Title = "HP Print Driver App", EstRam = "~40 MB", Recommended = false, Desc = "HP companion driver helper." },
                new { Id = "DellInc.DellCommandUpdate", Title = "Dell Command Update", EstRam = "~50 MB", Recommended = false, Desc = "Dell updater client." },
                new { Id = "DellInc.SupportAssist", Title = "Dell SupportAssist", EstRam = "~80 MB", Recommended = false, Desc = "Dell telemetry and diagnostics assistant." },
                new { Id = "LenovoCorporation.LenovoVantage", Title = "Lenovo Vantage", EstRam = "~110 MB", Recommended = false, Desc = "Lenovo companion hardware manager." },
                new { Id = "ASUSTeKComputerInc.MyASUS", Title = "MyASUS", EstRam = "~90 MB", Recommended = false, Desc = "ASUS diagnostics and promo dashboard." }
            };

            // Query installed packages for current user
            var (success, output) = await SystemHelper.RunPowerShellCommandAsync("Get-AppxPackage | Select-Object -ExpandProperty Name");
            var installedNames = success
                ? new HashSet<string>(output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var app in candidateApps)
            {
                if (installedNames.Contains(app.Id))
                {
                    string targetId = app.Id;
                    list.Add(new DebloatItem
                    {
                        Id = $"appx_{targetId}",
                        Category = DebloatCategory.BloatwareApp,
                        Title = app.Title,
                        Description = app.Desc,
                        Details = $"Package: {targetId}",
                        EstimatedMemory = app.EstRam,
                        IsBloatware = true,
                        Consequences = "Uninstalls this application for the current user and unprovisions it from the system. You can easily reinstall it anytime from the Microsoft Store or restore it using WinDebloat's Undo center.",
                        HelpUrl = "https://support.microsoft.com/en-us/windows/apps/uninstall-or-remove-apps-and-programs-in-windows",
                        IsRecommended = app.Recommended,
                        IsSelected = app.Recommended,
                        ApplyAction = async (progress) =>
                        {
                            progress.Report($"Uninstalling {targetId}...");
                            var cmd = $"Get-AppxPackage -Name '*{targetId}*' | Remove-AppxPackage -ErrorAction SilentlyContinue";
                            if (SystemHelper.IsAdministrator())
                            {
                                cmd += $"; Get-AppxPackage -AllUsers -Name '*{targetId}*' | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue";
                                cmd += $"; Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -like '*{targetId}*' }} | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue";
                            }
                            var (res, err) = await SystemHelper.RunPowerShellCommandAsync(cmd, elevated: false);
                            if (res)
                            {
                                progress.Report($"Successfully uninstalled {app.Title}.");
                                return true;
                            }
                            progress.Report($"Failed to remove {app.Title}: {err}");
                            return false;
                        }
                    });
                }
            }

            return list;
        }

        public async Task<List<DebloatItem>> ScanSystemTweaksAsync()
        {
            var list = new List<DebloatItem>();

            // 1. Disable Bing Search in Start Menu
            object? bingVal = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled");
            object? suggVal = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions")
                           ?? SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions");
            bool bingApplied = (bingVal is int bi && bi == 0) || (suggVal is int si && si == 1);

            list.Add(new DebloatItem
            {
                Id = "tweak_disable_bing_search",
                Category = DebloatCategory.SystemTweak,
                Title = "Disable Bing / Web Search in Start Menu",
                Description = bingApplied
                    ? "Stops SearchHost from loading Edge WebView2 browser engine. (Already applied in system policies)"
                    : "Stops SearchHost from loading Edge WebView2 browser engine (~350 MB saved). Local search is instant and cleaner.",
                Details = "HKCU/HKLM\\...\\Explorer -> DisableSearchBoxSuggestions=1, BingSearchEnabled=0, DisableWebSearch=1",
                EstimatedMemory = "~350 MB",
                RequiresReboot = true,
                Consequences = "Disables Bing internet search suggestions in the Windows Start menu search box. Only local files, apps, and settings on your PC will appear in search results (which makes search noticeably faster and stops SearchHost from loading Edge WebView2 into RAM).",
                HelpUrl = "https://support.microsoft.com/en-us/windows/experience/do-more-with-windows-search",
                IsRecommended = !bingApplied,
                IsSelected = !bingApplied,
                IsCompleted = bingApplied,
                StatusMessage = bingApplied ? "Already Applied" : "Ready",
                UndoHistoryEntry = new UndoRecord
                {
                    Id = "tweak_disable_bing_search",
                    Title = "Disable Bing / Web Search in Start Menu",
                    Description = "Disabled Bing web search integration in Start menu.",
                    Category = nameof(DebloatCategory.SystemTweak),
                    RevertType = "SystemTweak_BingSearch"
                },
                ApplyAction = async (progress) =>
                {
                    progress.Report("Applying registry tweaks to disable web search in Start menu...");
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1);
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0);
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "CortanaConsent", 0);
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", 0);
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", 1);

                    if (SystemHelper.IsAdministrator())
                    {
                        SystemHelper.SetRegistryDWord(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1);
                        SystemHelper.SetRegistryDWord(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", 0);
                        SystemHelper.SetRegistryDWord(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", 1);
                    }

                    // Kill SearchHost so changes take effect immediately
                    progress.Report("Terminating SearchHost to free memory immediately...");
                    try
                    {
                        foreach (var p in Process.GetProcessesByName("SearchHost")) p.Kill();
                    }
                    catch { }

                    progress.Report("Bing search disabled in Start menu.");
                    return true;
                },
                UndoAction = async (progress) =>
                {
                    progress.Report("Re-enabling Bing search in Start menu...");
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 0);
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 1);
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "CortanaConsent", 1);
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", 1);
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", 0);

                    if (SystemHelper.IsAdministrator())
                    {
                        SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions");
                        SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb");
                        SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch");
                    }
                    progress.Report("Bing search re-enabled.");
                    return true;
                }
            });

            // 2. Disable Windows Widgets / News Feed
            object? tbVal = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa");
            object? newsVal = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Dsh", "AllowNewsAndInterests")
                           ?? SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Dsh", "AllowNewsAndInterests");
            bool widgetsApplied = (tbVal is int ti && ti == 0) || (newsVal is int ni && ni == 0);

            list.Add(new DebloatItem
            {
                Id = "tweak_disable_widgets",
                Category = DebloatCategory.SystemTweak,
                Title = "Disable Windows Widgets & Feeds",
                Description = widgetsApplied
                    ? "Hides taskbar widgets and prevents news feed caching in RAM. (Already applied in system policies)"
                    : "Hides taskbar widgets and prevents MicrosoftStartFeedProvider from caching news in RAM.",
                Details = "HKCU\\...\\Explorer\\Advanced -> TaskbarDa=0; HKLM/HKCU Policies -> AllowNewsAndInterests=0",
                EstimatedMemory = "~60 MB",
                RequiresReboot = true,
                Consequences = "Hides the taskbar Widgets icon and prevents Windows from pre-loading and caching MSN news, weather, and traffic feeds in memory.",
                HelpUrl = "https://support.microsoft.com/en-us/windows/experience/personalization/stay-up-to-date-with-widgets-in-windows",
                IsRecommended = !widgetsApplied,
                IsSelected = !widgetsApplied,
                IsCompleted = widgetsApplied,
                StatusMessage = widgetsApplied ? "Already Applied" : "Ready",
                UndoHistoryEntry = new UndoRecord
                {
                    Id = "tweak_disable_widgets",
                    Title = "Disable Windows Widgets & Feeds",
                    Description = "Disabled Windows Widgets and background MSN feed caching.",
                    Category = nameof(DebloatCategory.SystemTweak),
                    RevertType = "SystemTweak_Widgets"
                },
                ApplyAction = async (progress) =>
                {
                    progress.Report("Disabling Windows Widgets and MSN feeds...");
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 0);
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0);

                    if (SystemHelper.IsAdministrator())
                    {
                        SystemHelper.SetRegistryDWord(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0);
                    }

                    try
                    {
                        foreach (var p in Process.GetProcessesByName("MicrosoftStartFeedProvider")) p.Kill();
                        foreach (var p in Process.GetProcessesByName("WidgetService")) p.Kill();
                    }
                    catch { }

                    progress.Report("Widgets disabled.");
                    return true;
                },
                UndoAction = async (progress) =>
                {
                    progress.Report("Re-enabling Windows Widgets and feeds...");
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 1);
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 1);
                    if (SystemHelper.IsAdministrator())
                    {
                        SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Dsh", "AllowNewsAndInterests");
                    }
                    progress.Report("Widgets re-enabled.");
                    return true;
                }
            });

            // 3. Disable Chrome Background Lingering
            object? chromeVal = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, @"Software\Policies\Google\Chrome", "BackgroundModeEnabled")
                             ?? SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Google\Chrome", "BackgroundModeEnabled");
            bool chromeApplied = chromeVal is int ci && ci == 0;

            list.Add(new DebloatItem
            {
                Id = "tweak_chrome_background",
                Category = DebloatCategory.SystemTweak,
                Title = "Disable Chrome Background Mode",
                Description = chromeApplied
                    ? "Ensures Chrome releases all RAM when closed. (Already applied in system policies)"
                    : "Ensures Chrome releases all RAM and closes background extension processes when the last window is closed.",
                Details = "HKCU\\Software\\Policies\\Google\\Chrome -> BackgroundModeEnabled=0",
                EstimatedMemory = "~150 MB",
                Consequences = "Forces Google Chrome to fully exit and free all RAM when its last window is closed, rather than staying resident in the background for extensions or quick launch.",
                HelpUrl = "https://support.google.com/chrome/answer/1184722",
                IsRecommended = !chromeApplied,
                IsSelected = !chromeApplied,
                IsCompleted = chromeApplied,
                StatusMessage = chromeApplied ? "Already Applied" : "Ready",
                UndoHistoryEntry = new UndoRecord
                {
                    Id = "tweak_chrome_background",
                    Title = "Disable Chrome Background Mode",
                    Description = "Disabled Chrome background apps lingering in memory.",
                    Category = nameof(DebloatCategory.SystemTweak),
                    RevertType = "SystemTweak_Chrome"
                },
                ApplyAction = async (progress) =>
                {
                    progress.Report("Disabling Chrome background apps lingering...");
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Policies\Google\Chrome", "BackgroundModeEnabled", 0);
                    if (SystemHelper.IsAdministrator())
                    {
                        SystemHelper.SetRegistryDWord(RegistryHive.LocalMachine, @"Software\Policies\Google\Chrome", "BackgroundModeEnabled", 0);
                    }
                    progress.Report("Chrome background mode policy set.");
                    return true;
                },
                UndoAction = async (progress) =>
                {
                    progress.Report("Restoring Chrome background mode...");
                    SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, @"Software\Policies\Google\Chrome", "BackgroundModeEnabled");
                    if (SystemHelper.IsAdministrator())
                    {
                        SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Google\Chrome", "BackgroundModeEnabled");
                    }
                    progress.Report("Chrome background mode policy removed.");
                    return true;
                }
            });

            // 4. Disable Edge Background Mode & Startup Boost
            object? edgeBg = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Edge", "BackgroundModeEnabled")
                          ?? SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Edge", "BackgroundModeEnabled");
            object? edgeBoost = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Edge", "StartupBoostEnabled")
                             ?? SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Edge", "StartupBoostEnabled");
            bool edgeApplied = (edgeBg is int ebi && ebi == 0) && (edgeBoost is int eoi && eoi == 0);

            list.Add(new DebloatItem
            {
                Id = "tweak_edge_background",
                Category = DebloatCategory.SystemTweak,
                Title = "Disable Edge Startup Boost & Background Mode",
                Description = edgeApplied
                    ? "Prevents Microsoft Edge from running stealth background instances. (Already applied in system policies)"
                    : "Prevents Microsoft Edge from running stealth background instances and pre-launching on boot.",
                Details = "HKCU\\Software\\Policies\\Microsoft\\Edge -> StartupBoostEnabled=0, BackgroundModeEnabled=0",
                EstimatedMemory = "~120 MB",
                RequiresReboot = true,
                Consequences = "Prevents Microsoft Edge from running stealth background processes or pre-loading during Windows boot (Startup Boost), freeing RAM for your active applications.",
                HelpUrl = "https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies#backgroundmodeenabled",
                IsRecommended = !edgeApplied,
                IsSelected = !edgeApplied,
                IsCompleted = edgeApplied,
                StatusMessage = edgeApplied ? "Already Applied" : "Ready",
                UndoHistoryEntry = new UndoRecord
                {
                    Id = "tweak_edge_background",
                    Title = "Disable Edge Startup Boost & Background Mode",
                    Description = "Disabled Edge startup boost and background mode lingering.",
                    Category = nameof(DebloatCategory.SystemTweak),
                    RevertType = "SystemTweak_Edge"
                },
                ApplyAction = async (progress) =>
                {
                    progress.Report("Disabling Edge background mode and startup boost...");
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0);
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Edge", "StartupBoostEnabled", 0);
                    if (SystemHelper.IsAdministrator())
                    {
                        SystemHelper.SetRegistryDWord(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0);
                        SystemHelper.SetRegistryDWord(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Edge", "StartupBoostEnabled", 0);
                    }
                    progress.Report("Edge background policies configured.");
                    return true;
                },
                UndoAction = async (progress) =>
                {
                    progress.Report("Restoring Edge background mode and startup boost...");
                    SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Edge", "BackgroundModeEnabled");
                    SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Edge", "StartupBoostEnabled");
                    if (SystemHelper.IsAdministrator())
                    {
                        SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Edge", "BackgroundModeEnabled");
                        SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Edge", "StartupBoostEnabled");
                    }
                    progress.Report("Edge background policies removed.");
                    return true;
                }
            });

            // 5. Terminate Command Palette Background Process (if running)
            bool cmdPalRunning = Process.GetProcessesByName("Microsoft.CmdPal.UI").Any();
            if (cmdPalRunning)
            {
                list.Add(new DebloatItem
                {
                    Id = "tweak_kill_cmdpal",
                    Category = DebloatCategory.SystemTweak,
                    Title = "Terminate Microsoft Command Palette Background Host",
                    Description = "Microsoft.CmdPal.UI is currently running in the background and holding ~206 MB of memory.",
                    Details = "Process: Microsoft.CmdPal.UI.exe",
                    EstimatedMemory = "~206 MB",
                    Consequences = "Terminates the PowerToys Microsoft Command Palette background host. You can re-open it at any time using your configured shortcut.",
                    HelpUrl = "https://learn.microsoft.com/en-us/windows/powertoys/",
                    IsRecommended = true,
                    IsSelected = true,
                    ApplyAction = async (progress) =>
                    {
                        progress.Report("Terminating Microsoft.CmdPal.UI...");
                        try
                        {
                            foreach (var p in Process.GetProcessesByName("Microsoft.CmdPal.UI")) p.Kill();
                            foreach (var p in Process.GetProcessesByName("Microsoft.CmdPal.Ext.PowerToys")) p.Kill();
                            progress.Report("Command Palette process terminated.");
                            return true;
                        }
                        catch (Exception ex)
                        {
                            progress.Report($"Error: {ex.Message}");
                            return false;
                        }
                    }
                });
            }

            // 6. Disable Windows Input Experience (TextInputHost)
            const string ifeoKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\TextInputHost.exe";
            object? ifeoDebugger = SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, ifeoKey, "Debugger");
            bool textInputDisabled = ifeoDebugger != null && !string.IsNullOrEmpty(ifeoDebugger.ToString());

            list.Add(new DebloatItem
            {
                Id = "tweak_disable_text_input_host",
                Category = DebloatCategory.SystemTweak,
                Title = "Disable Windows Input Experience (TextInputHost)",
                Description = textInputDisabled
                    ? "Prevents TextInputHost.exe from running in the background. (Already disabled)"
                    : "Prevents TextInputHost.exe from idling in the background and holding ~185 MB of memory. Disables touch keyboard and emoji menu (Win+.).",
                Details = "IFEO: HKLM\\...\\TextInputHost.exe -> Debugger=systray.exe",
                EstimatedMemory = "~185 MB",
                RequiresAdmin = true,
                Consequences = "Disables TextInputHost.exe via Image File Execution Options. Note: Disabling this will prevent the touch keyboard and Windows emoji picker shortcut (Win + .) from appearing.",
                HelpUrl = "https://support.microsoft.com/en-us/windows/windows-keyboard-tips-and-tricks-588e0b72-0fff-6d3f-aeee-6e5116097942",
                IsRecommended = false,
                IsSelected = false,
                IsCompleted = textInputDisabled,
                StatusMessage = textInputDisabled ? "Already Disabled" : "Ready",
                UndoHistoryEntry = new UndoRecord
                {
                    Id = "tweak_disable_text_input_host",
                    Title = "Disable Windows Input Experience (TextInputHost)",
                    Description = "Disabled TextInputHost.exe background execution.",
                    Category = nameof(DebloatCategory.SystemTweak),
                    RevertType = "SystemTweak_TextInputHost"
                },
                ApplyAction = async (progress) =>
                {
                    progress.Report("Disabling Windows Input Experience (TextInputHost)...");
                    bool ok = SystemHelper.SetRegistryString(RegistryHive.LocalMachine, ifeoKey, "Debugger", @"%windir%\System32\systray.exe");
                    try
                    {
                        foreach (var p in Process.GetProcessesByName("TextInputHost")) p.Kill();
                    }
                    catch { }
                    progress.Report("TextInputHost.exe disabled and terminated.");
                    return ok;
                },
                UndoAction = async (progress) =>
                {
                    progress.Report("Restoring Windows Input Experience (TextInputHost)...");
                    bool ok = SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, ifeoKey, "Debugger");
                    progress.Report("TextInputHost.exe restored.");
                    return ok;
                }
            });

            // 7. Ghost / Orphaned Browser Cleaner
            int ghostChromeCount = 0;
            int ghostEdgeCount = 0;
            try
            {
                var chromeProcs = Process.GetProcessesByName("chrome");
                bool hasVisibleChrome = chromeProcs.Any(p => p.MainWindowHandle != IntPtr.Zero);
                if (!hasVisibleChrome) ghostChromeCount = chromeProcs.Length;

                var edgeProcs = Process.GetProcessesByName("msedge");
                bool hasVisibleEdge = edgeProcs.Any(p => p.MainWindowHandle != IntPtr.Zero);
                if (!hasVisibleEdge) ghostEdgeCount = edgeProcs.Length;
            }
            catch { }

            if (ghostChromeCount > 0 || ghostEdgeCount > 0)
            {
                list.Add(new DebloatItem
                {
                    Id = "tweak_kill_ghost_browsers",
                    Category = DebloatCategory.SystemTweak,
                    Title = "Clean Ghost / Orphaned Browser Processes",
                    Description = $"Detected background headless browser processes ({ghostChromeCount} Chrome, {ghostEdgeCount} Edge) lingering in RAM with no open browser windows.",
                    Details = "Terminates headless background chrome.exe and msedge.exe processes that persist after windows are closed.",
                    EstimatedMemory = "~200-600 MB",
                    IsRecommended = true,
                    IsSelected = true,
                    Consequences = "Terminates lingering background chrome.exe and msedge.exe processes when no browser windows are open, freeing occupied RAM. If a browser window is currently open, it is skipped to protect active tabs.",
                    HelpUrl = "https://support.google.com/chrome/answer/1184722",
                    ApplyAction = async (progress) =>
                    {
                        progress.Report("Scanning for orphaned background browser processes...");
                        int killed = 0;
                        long bytesFreed = 0;
                        foreach (var name in new[] { "chrome", "msedge" })
                        {
                            var procs = Process.GetProcessesByName(name);
                            bool hasVisible = procs.Any(p => p.MainWindowHandle != IntPtr.Zero);
                            if (hasVisible)
                            {
                                progress.Report($"{name}.exe has an active window open; skipping to protect active tabs.");
                                continue;
                            }

                            foreach (var p in procs)
                            {
                                try
                                {
                                    bytesFreed += p.WorkingSet64;
                                    p.Kill();
                                    killed++;
                                }
                                catch { }
                                finally { try { p.Dispose(); } catch { } }
                            }
                        }
                        double mb = Math.Round(bytesFreed / (1024.0 * 1024.0), 1);
                        progress.Report($"Terminated {killed} headless browser processes, freeing ~{mb} MB RAM.");
                        return true;
                    }
                });
            }

            // 8. Telemetry Scheduled Tasks
            bool telemetryTasksDisabled = false;
            try
            {
                var (success, stdout) = await SystemHelper.RunProcessAsync("schtasks.exe", @"/query /tn ""\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"" /fo csv /nh");
                if (success && stdout.Contains("Disabled", StringComparison.OrdinalIgnoreCase))
                {
                    telemetryTasksDisabled = true;
                }
            }
            catch { }

            list.Add(new DebloatItem
            {
                Id = "tweak_disable_telemetry_tasks",
                Category = DebloatCategory.SystemTweak,
                Title = "Disable Telemetry & Diagnostic Scheduled Tasks",
                Description = telemetryTasksDisabled
                    ? "Prevents background compatibility scans and diagnostic telemetry tasks. (Already disabled)"
                    : "Disables scheduled background tasks (Compatibility Appraiser, ProgramDataUpdater, Consolidator, UsbCeip) that wake the CPU and scan disks.",
                Details = @"Tasks: \Microsoft\Windows\Application Experience & CEIP tasks disabled via schtasks",
                EstimatedMemory = "~45 MB (CPU/Disk Spikes)",
                RequiresAdmin = true,
                IsRecommended = !telemetryTasksDisabled,
                IsSelected = !telemetryTasksDisabled,
                IsCompleted = telemetryTasksDisabled,
                StatusMessage = telemetryTasksDisabled ? "Already Disabled" : "Ready",
                Consequences = "Stops Windows from periodically scanning your hardware, device drivers, and installed software to report diagnostic compatibility telemetry to Microsoft. Does not interfere with Windows Updates.",
                HelpUrl = "https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services",
                UndoHistoryEntry = new UndoRecord
                {
                    Id = "tweak_disable_telemetry_tasks",
                    Title = "Disable Telemetry & Diagnostic Scheduled Tasks",
                    Description = "Disabled scheduled telemetry tasks in Application Experience and CEIP.",
                    Category = nameof(DebloatCategory.SystemTweak),
                    RevertType = "SystemTweak_TelemetryTasks"
                },
                ApplyAction = async (progress) =>
                {
                    progress.Report("Disabling telemetry scheduled tasks...");
                    string[] tasks = new[]
                    {
                        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser Exp",
                        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
                        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
                        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip"
                    };
                    foreach (var t in tasks)
                    {
                        progress.Report($"Disabling task: {t}...");
                        await SystemHelper.RunProcessAsync("schtasks.exe", $@"/change /tn ""{t}"" /disable", elevated: true);
                    }
                    progress.Report("Telemetry scheduled tasks disabled.");
                    return true;
                },
                UndoAction = async (progress) =>
                {
                    progress.Report("Re-enabling telemetry scheduled tasks...");
                    string[] tasks = new[]
                    {
                        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser Exp",
                        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
                        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
                        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip"
                    };
                    foreach (var t in tasks)
                    {
                        progress.Report($"Enabling task: {t}...");
                        await SystemHelper.RunProcessAsync("schtasks.exe", $@"/change /tn ""{t}"" /enable", elevated: true);
                    }
                    progress.Report("Telemetry scheduled tasks re-enabled.");
                    return true;
                }
            });

            // 9. Cross-Device Experience (Phone Link background sync host)
            object? cdVal = SystemHelper.GetRegistryValue(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\CrossDevice", "DisableCrossDeviceExperience")
                         ?? SystemHelper.GetRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Windows\CrossDevice", "DisableCrossDeviceExperience");
            bool cdDisabled = cdVal is int cdi && cdi == 1;

            list.Add(new DebloatItem
            {
                Id = "tweak_disable_cross_device",
                Category = DebloatCategory.SystemTweak,
                Title = "Disable Cross-Device Experience (Phone Link Sync)",
                Description = cdDisabled
                    ? "Prevents CrossDeviceResume.exe and phone link background syncing. (Already disabled)"
                    : "Stops CrossDeviceResume.exe from lingering in RAM to sync phone notifications, clipboard, and browser handoff.",
                Details = @"HKCU/HKLM\Software\Policies\Microsoft\Windows\CrossDevice -> DisableCrossDeviceExperience=1",
                EstimatedMemory = "~65 MB",
                IsRecommended = !cdDisabled,
                IsSelected = !cdDisabled,
                IsCompleted = cdDisabled,
                StatusMessage = cdDisabled ? "Already Disabled" : "Ready",
                Consequences = "Disables background phone syncing and cross-device resume (e.g., continuing browsing tabs or receiving calls from Android/iOS on PC).",
                HelpUrl = "https://support.microsoft.com/en-us/windows/apps/phonelink/phone-link-requirements-and-setup",
                UndoHistoryEntry = new UndoRecord
                {
                    Id = "tweak_disable_cross_device",
                    Title = "Disable Cross-Device Experience",
                    Description = "Disabled CrossDeviceResume.exe background execution.",
                    Category = nameof(DebloatCategory.SystemTweak),
                    RevertType = "SystemTweak_CrossDevice"
                },
                ApplyAction = async (progress) =>
                {
                    progress.Report("Disabling Cross-Device Experience...");
                    SystemHelper.SetRegistryDWord(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\CrossDevice", "DisableCrossDeviceExperience", 1);
                    if (SystemHelper.IsAdministrator())
                    {
                        SystemHelper.SetRegistryDWord(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Windows\CrossDevice", "DisableCrossDeviceExperience", 1);
                    }
                    try
                    {
                        foreach (var p in Process.GetProcessesByName("CrossDeviceResume")) p.Kill();
                    }
                    catch { }
                    progress.Report("Cross-Device Experience disabled.");
                    return true;
                },
                UndoAction = async (progress) =>
                {
                    progress.Report("Re-enabling Cross-Device Experience...");
                    SystemHelper.DeleteRegistryValue(RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\CrossDevice", "DisableCrossDeviceExperience");
                    if (SystemHelper.IsAdministrator())
                    {
                        SystemHelper.DeleteRegistryValue(RegistryHive.LocalMachine, @"Software\Policies\Microsoft\Windows\CrossDevice", "DisableCrossDeviceExperience");
                    }
                    progress.Report("Cross-Device Experience re-enabled.");
                    return true;
                }
            });

            return list;
        }

        public List<DebloatItem> ScanWindowsServices()
        {
            var list = new List<DebloatItem>();

            var serviceConfigs = new[]
            {
                // Telemetry & Diagnostic tracking
                new { 
                    Name = "DiagTrack", 
                    Title = "Connected User Experiences & Telemetry", 
                    EstRam = "~58 MB", 
                    Desc = "Gathers system telemetry and user diagnostics.", 
                    Recommended = true,
                    Consequences = "Disables diagnostic and telemetry transmission to Microsoft. Essential Windows updates and operating system features are unaffected.",
                    HelpUrl = "https://learn.microsoft.com/en-us/windows/privacy/configure-windows-diagnostic-data-in-your-organization"
                },
                new { 
                    Name = "dmwappushservice", 
                    Title = "WAP Push Telemetry Routing Service", 
                    EstRam = "~20 MB", 
                    Desc = "Routes telemetry collection messages in background.", 
                    Recommended = true,
                    Consequences = "Stops background routing of device management and telemetry push notifications. Standard internet and network connectivity remain completely functional.",
                    HelpUrl = "https://learn.microsoft.com/en-us/windows/privacy/diagnostic-data-viewer-overview"
                },
                new { 
                    Name = "InventorySvc", 
                    Title = "Windows Compatibility & Inventory Appraisal", 
                    EstRam = "~28 MB", 
                    Desc = "Scans installed applications and hardware compatibility, transmitting telemetry to Microsoft.", 
                    Recommended = true,
                    Consequences = "Prevents Windows from scanning all installed applications and driver inventory for upload to Microsoft diagnostic servers.",
                    HelpUrl = "https://learn.microsoft.com/en-us/troubleshoot/windows-client/setup-upgrade-and-drivers/use-windows-setup-compatibility-scan-logs-to-identify-blocking-issues"
                },
                new { 
                    Name = "DPS", 
                    Title = "Diagnostic Policy Service", 
                    EstRam = "~22 MB", 
                    Desc = "Enables problem detection, troubleshooting, and diagnostic reporting to Microsoft.", 
                    Recommended = true,
                    Consequences = "Disables automated built-in diagnostic troubleshooting wizards and background problem reporting to Microsoft.",
                    HelpUrl = "https://learn.microsoft.com/en-us/windows/privacy/configure-windows-diagnostic-data-in-your-organization"
                },
                new { 
                    Name = "SysMain", 
                    Title = "SysMain (Superfetch)", 
                    EstRam = "~50 MB", 
                    Desc = "Caches frequently used applications into RAM (high disk/RAM load on older drives).", 
                    Recommended = false,
                    Consequences = "Disables prefetching applications into memory cache. On systems with fast NVMe/SSD storage, disabling SysMain reduces unnecessary memory caching and disk writes with zero noticeable slowdown.",
                    HelpUrl = "https://learn.microsoft.com/en-us/windows-server/administration/performance-tuning/subsystem/cache-memory-management"
                },
                
                // Cloud & Network Background Syncing
                new { 
                    Name = "DoSvc", 
                    Title = "Delivery Optimization Service", 
                    EstRam = "~35 MB", 
                    Desc = "Peer-to-peer background bandwidth service that uploads and shares Windows updates across the internet.", 
                    Recommended = true,
                    Consequences = "Disables peer-to-peer sharing of Windows Update files over local network and the internet. Windows Updates will download directly from Microsoft servers.",
                    HelpUrl = "https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization"
                },
                new { 
                    Name = "WSAIFabricSvc", 
                    Title = "Windows AI Components Host Service", 
                    EstRam = "~30 MB", 
                    Desc = "Background worker process for Windows Copilot and local AI integration components.", 
                    Recommended = true,
                    Consequences = "Stops background host service for Windows Copilot and local AI modules.",
                    HelpUrl = "https://learn.microsoft.com/en-us/windows/ai/"
                },
                new { 
                    Name = "CDPSvc", 
                    Title = "Connected Devices Platform Service", 
                    EstRam = "~20 MB", 
                    Desc = "Coordinates discovery and connectivity with companion mobile devices (Phone Link).", 
                    Recommended = false,
                    Consequences = "Disables device discovery and background syncing for Microsoft Phone Link with iOS and Android devices.",
                    HelpUrl = "https://support.microsoft.com/en-us/windows/apps/phonelink/phone-link-requirements-and-setup"
                },
                new { 
                    Name = "lfsvc", 
                    Title = "Geolocation Service", 
                    EstRam = "~15 MB", 
                    Desc = "Monitors system physical location and feeds location coordinates to apps.", 
                    Recommended = false,
                    Consequences = "Prevents Windows applications from accessing your device's physical GPS/network location directly via Windows Location APIs.",
                    HelpUrl = "https://support.microsoft.com/en-us/windows/privacy/windows-location-service-and-privacy"
                },

                // Consumer & Background services
                new { 
                    Name = "MapsBroker", 
                    Title = "Downloaded Maps Manager", 
                    EstRam = "~25 MB", 
                    Desc = "Background map manager for Windows Maps.", 
                    Recommended = true,
                    Consequences = "Disables background offline map tile downloads and sync for the Windows Maps app.",
                    HelpUrl = "https://support.microsoft.com/en-us/windows/apps/find-your-way-with-maps"
                },
                new { 
                    Name = "RetailDemo", 
                    Title = "Retail Demo Service", 
                    EstRam = "~15 MB", 
                    Desc = "Controls device behavior in store demonstration mode.", 
                    Recommended = true,
                    Consequences = "Disables retail kiosk store demonstration mode. Has no impact on personal, professional, or gaming use.",
                    HelpUrl = "https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/retail-demo-experience"
                },
                new { 
                    Name = "WMPNetworkSvc", 
                    Title = "Windows Media Player Network Sharing", 
                    EstRam = "~25 MB", 
                    Desc = "Shares media libraries over local network.", 
                    Recommended = true,
                    Consequences = "Disables local DLNA/UPnP network media sharing from Windows Media Player to other smart devices on your local network.",
                    HelpUrl = "https://learn.microsoft.com/en-us/windows/win32/wmp/windows-media-player-sdk"
                },
                
                // Xbox Live Services
                new { 
                    Name = "XblAuthManager", 
                    Title = "Xbox Live Auth Manager", 
                    EstRam = "~20 MB", 
                    Desc = "Authentication services for Xbox Live applications.", 
                    Recommended = false,
                    Consequences = "Disables sign-in and authentication for Xbox Live services. If you play games via Xbox Game Pass or the Xbox App, keep this enabled.",
                    HelpUrl = "https://support.xbox.com/en-US/help/games-apps/troubleshooting/troubleshoot-xbox-app-for-windows"
                },
                new { 
                    Name = "XblGameSave", 
                    Title = "Xbox Live Game Save Service", 
                    EstRam = "~20 MB", 
                    Desc = "Syncs game saves to Xbox cloud.", 
                    Recommended = false,
                    Consequences = "Prevents automatic cloud syncing of Xbox Game Pass game save files. Local saves and non-Xbox platforms (Steam, Epic) are unaffected.",
                    HelpUrl = "https://support.xbox.com/en-US/help/games-apps/cloud-gaming/cloud-game-saves-faq"
                },
                new { 
                    Name = "XboxNetApiSvc", 
                    Title = "Xbox Live Networking Service", 
                    EstRam = "~20 MB", 
                    Desc = "Peer-to-peer networking service for Xbox Live.", 
                    Recommended = false,
                    Consequences = "Disables peer-to-peer networking and party voice chat within the Xbox Windows app.",
                    HelpUrl = "https://support.xbox.com/en-US/help/hardware-network/connect-network/troubleshoot-party-chat"
                },
                
                // System Connectivity & Printing
                new { 
                    Name = "Spooler", 
                    Title = "Print Spooler Service", 
                    EstRam = "~37 MB", 
                    Desc = "Manages printer queues. Safe to disable if you do not use a printer.", 
                    Recommended = false,
                    Consequences = "Disables printing queues. You will not be able to print to physical printers or export via Print to PDF until this service is re-enabled.",
                    HelpUrl = "https://learn.microsoft.com/en-us/windows-hardware/drivers/print/print-spooler-architecture"
                },
                new { 
                    Name = "TermService", 
                    Title = "Remote Desktop Service", 
                    EstRam = "~105 MB", 
                    Desc = "Allows inbound Remote Desktop connections to this computer.", 
                    Recommended = false,
                    Consequences = "Blocks incoming Remote Desktop (RDP) connections from other PCs. Outbound remote desktop sessions to other machines remain fully functional.",
                    HelpUrl = "https://learn.microsoft.com/en-us/windows-server/remote/remote-desktop-services/remote-desktop-services-overview"
                }
            };

            foreach (var svc in serviceConfigs)
            {
                try
                {
                    using var sc = new ServiceController(svc.Name);
                    string statusStr = sc.Status.ToString();
                    bool isRunning = sc.Status == ServiceControllerStatus.Running;
                    bool isDisabled = sc.StartType == ServiceStartMode.Disabled;

                    // Only show services that are currently RUNNING and consuming RAM
                    if (!isRunning || isDisabled)
                    {
                        continue;
                    }

                    list.Add(new DebloatItem
                    {
                        Id = $"svc_{svc.Name}",
                        Category = DebloatCategory.Service,
                        Title = $"{svc.Title} ({svc.Name})",
                        Description = $"{svc.Desc} (Currently running in RAM; applying stops and disables service).",
                        Details = $"Service: {svc.Name} | Status: Running | StartType: {sc.StartType}",
                        EstimatedMemory = svc.EstRam,
                        Consequences = svc.Consequences,
                        HelpUrl = svc.HelpUrl,
                        IsBloatware = true,
                        IsRecommended = svc.Recommended,
                        IsSelected = svc.Recommended,
                        IsCompleted = false,
                        StatusMessage = "Running",
                        RequiresAdmin = true,
                        UndoHistoryEntry = new UndoRecord
                        {
                            Id = $"svc_{svc.Name}",
                            Title = $"{svc.Title} ({svc.Name})",
                            Description = $"Disabled and stopped background service: {svc.Title}.",
                            Category = nameof(DebloatCategory.Service),
                            RevertType = "ServiceDisable",
                            ServiceName = svc.Name,
                            OriginalServiceStartup = "Automatic"
                        },
                        ApplyAction = async (progress) =>
                        {
                            progress.Report($"Disabling and stopping service {svc.Name}...");

                            // 1. Direct registry write to Start = 4 (Disabled)
                            bool regSet = SystemHelper.SetRegistryDWord(RegistryHive.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\{svc.Name}", "Start", 4);

                            // 2. Configure Service Control Manager via sc.exe config
                            await SystemHelper.RunProcessAsync("sc.exe", $"config \"{svc.Name}\" start= disabled", elevated: true);

                            // 3. Stop service if running via sc.exe stop
                            await SystemHelper.RunProcessAsync("sc.exe", $"stop \"{svc.Name}\"", elevated: true);

                            // 4. PowerShell Set-Service & Stop-Service
                            var cmd = $"Stop-Service -Name '{svc.Name}' -Force -ErrorAction SilentlyContinue; Set-Service -Name '{svc.Name}' -StartupType Disabled -ErrorAction SilentlyContinue";
                            var (res, outMsg) = await SystemHelper.RunPowerShellCommandAsync(cmd, elevated: true);

                            // Verify service status
                            bool disabledOk = false;
                            try
                            {
                                using var checkSc = new ServiceController(svc.Name);
                                disabledOk = checkSc.StartType == ServiceStartMode.Disabled;
                            }
                            catch { }

                            if (disabledOk || regSet || res)
                            {
                                progress.Report($"Service {svc.Name} stopped and disabled.");
                                return true;
                            }

                            progress.Report($"Failed to configure service {svc.Name}: {outMsg}");
                            return false;
                        },
                        UndoAction = async (progress) =>
                        {
                            progress.Report($"Restoring and re-enabling service {svc.Name}...");
                            SystemHelper.SetRegistryDWord(RegistryHive.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\{svc.Name}", "Start", 2);
                            await SystemHelper.RunProcessAsync("sc.exe", $"config \"{svc.Name}\" start= auto", elevated: true);
                            await SystemHelper.RunProcessAsync("sc.exe", $"start \"{svc.Name}\"", elevated: true);
                            var cmd = $"Set-Service -Name '{svc.Name}' -StartupType Automatic -ErrorAction SilentlyContinue; Start-Service -Name '{svc.Name}' -ErrorAction SilentlyContinue";
                            await SystemHelper.RunPowerShellCommandAsync(cmd, elevated: true);
                            progress.Report($"Service {svc.Name} restored to Automatic and started.");
                            return true;
                        }
                    });
                }
                catch
                {
                    // Service doesn't exist or not accessible
                }
            }

            return list;
        }

        public List<DebloatItem> ScanUserInstalledApps()
        {
            var list = new List<DebloatItem>();
            var installed = SystemHelper.GetInstalledDesktopApps();

            foreach (var app in installed)
            {
                string appName = app.Name;
                string uninstCmd = app.UninstallString;
                bool isHklm = app.IsHklm;

                list.Add(new DebloatItem
                {
                    Id = $"userapp_{Math.Abs(appName.GetHashCode()):X}",
                    Category = DebloatCategory.UserInstalledApp,
                    Title = appName,
                    Description = $"Version: {(string.IsNullOrEmpty(app.Version) ? "Unknown" : app.Version)} | Publisher: {(string.IsNullOrEmpty(app.Publisher) ? "Unknown" : app.Publisher)}",
                    Details = $"Install Location: {app.InstallLocation}\nUninstall Command: {uninstCmd}",
                    EstimatedMemory = "~50 - 250 MB",
                    Consequences = "Runs the official desktop uninstaller for this program. Application files, configuration, and registry entries may be removed.",
                    HelpUrl = "https://support.microsoft.com/en-us/windows/apps/uninstall-or-remove-apps-and-programs-in-windows",
                    IsRecommended = false,
                    IsSelected = false,
                    RequiresAdmin = isHklm,
                    ApplyAction = async (progress) =>
                    {
                        progress.Report($"Launching uninstaller for {appName}...");
                        try
                        {
                            if (uninstCmd.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = uninstCmd.Split(new[] { ' ' }, 2);
                                var args = parts.Length > 1 ? parts[1] : "";
                                var (ok, outMsg) = await SystemHelper.RunProcessAsync("msiexec.exe", args, elevated: isHklm);
                                progress.Report(ok ? $"Finished uninstall of {appName}." : $"Uninstaller completed: {outMsg}");
                                return ok;
                            }
                            else
                            {
                                var (ok, outMsg) = await SystemHelper.RunProcessAsync("cmd.exe", $"/c \"{uninstCmd}\"", elevated: isHklm);
                                progress.Report(ok ? $"Finished uninstall of {appName}." : $"Uninstaller completed: {outMsg}");
                                return ok;
                            }
                        }
                        catch (Exception ex)
                        {
                            progress.Report($"Error running uninstaller: {ex.Message}");
                            return false;
                        }
                    }
                });
            }

            return list;
        }
    }
}
