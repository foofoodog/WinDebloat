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
            items.AddRange(ScanSystemTweaks());

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
                        list.Add(new DebloatItem
                        {
                            Id = $"startup_hkcu_{currentValName}",
                            Category = DebloatCategory.Startup,
                            Title = appName,
                            Description = "Starts automatically in the background at user login.",
                            Details = $"Registry: HKCU\\...\\Run -> \"{currentValName}\" = {command}",
                            EstimatedMemory = estRam,
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

        public List<DebloatItem> ScanSystemTweaks()
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

            return list;
        }

        public List<DebloatItem> ScanWindowsServices()
        {
            var list = new List<DebloatItem>();

            var serviceConfigs = new[]
            {
                // Telemetry & Diagnostic tracking
                new { Name = "DiagTrack", Title = "Connected User Experiences & Telemetry", EstRam = "~58 MB", Desc = "Gathers system telemetry and user diagnostics.", Recommended = true },
                new { Name = "dmwappushservice", Title = "WAP Push Telemetry Routing Service", EstRam = "~20 MB", Desc = "Routes telemetry collection messages in background.", Recommended = true },
                new { Name = "InventorySvc", Title = "Windows Compatibility & Inventory Appraisal", EstRam = "~28 MB", Desc = "Scans installed applications and hardware compatibility, transmitting telemetry to Microsoft.", Recommended = true },
                new { Name = "DPS", Title = "Diagnostic Policy Service", EstRam = "~22 MB", Desc = "Enables problem detection, troubleshooting, and diagnostic reporting to Microsoft.", Recommended = true },
                new { Name = "SysMain", Title = "SysMain (Superfetch)", EstRam = "~50 MB", Desc = "Caches frequently used applications into RAM (high disk/RAM load on older drives).", Recommended = false },
                
                // Cloud & Network Background Syncing
                new { Name = "DoSvc", Title = "Delivery Optimization Service", EstRam = "~35 MB", Desc = "Peer-to-peer background bandwidth service that uploads and shares Windows updates across the internet.", Recommended = true },
                new { Name = "WSAIFabricSvc", Title = "Windows AI Components Host Service", EstRam = "~30 MB", Desc = "Background worker process for Windows Copilot and local AI integration components.", Recommended = true },
                new { Name = "CDPSvc", Title = "Connected Devices Platform Service", EstRam = "~20 MB", Desc = "Coordinates discovery and connectivity with companion mobile devices (Phone Link).", Recommended = false },
                new { Name = "lfsvc", Title = "Geolocation Service", EstRam = "~15 MB", Desc = "Monitors system physical location and feeds location coordinates to apps.", Recommended = false },

                // Consumer & Background services
                new { Name = "MapsBroker", Title = "Downloaded Maps Manager", EstRam = "~25 MB", Desc = "Background map manager for Windows Maps.", Recommended = true },
                new { Name = "RetailDemo", Title = "Retail Demo Service", EstRam = "~15 MB", Desc = "Controls device behavior in store demonstration mode.", Recommended = true },
                new { Name = "WMPNetworkSvc", Title = "Windows Media Player Network Sharing", EstRam = "~25 MB", Desc = "Shares media libraries over local network.", Recommended = true },
                
                // Xbox Live Services
                new { Name = "XblAuthManager", Title = "Xbox Live Auth Manager", EstRam = "~20 MB", Desc = "Authentication services for Xbox Live applications.", Recommended = false },
                new { Name = "XblGameSave", Title = "Xbox Live Game Save Service", EstRam = "~20 MB", Desc = "Syncs game saves to Xbox cloud.", Recommended = false },
                new { Name = "XboxNetApiSvc", Title = "Xbox Live Networking Service", EstRam = "~20 MB", Desc = "Peer-to-peer networking service for Xbox Live.", Recommended = false },
                
                // System Connectivity & Printing
                new { Name = "Spooler", Title = "Print Spooler Service", EstRam = "~37 MB", Desc = "Manages printer queues. Safe to disable if you do not use a printer.", Recommended = false },
                new { Name = "TermService", Title = "Remote Desktop Service", EstRam = "~105 MB", Desc = "Allows inbound Remote Desktop connections to this computer.", Recommended = false }
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
