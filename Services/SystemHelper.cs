using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WinDebloat.Services
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    public record MemoryInfo(double TotalGB, double AvailableGB, double UsedGB, int PercentUsed);

    public static class SystemHelper
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public static MemoryInfo GetMemoryInfo()
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                double total = memStatus.ullTotalPhys / (1024.0 * 1024 * 1024);
                double avail = memStatus.ullAvailPhys / (1024.0 * 1024 * 1024);
                double used = total - avail;
                int percent = (int)Math.Round((used / total) * 100.0);
                return new MemoryInfo(Math.Round(total, 2), Math.Round(avail, 2), Math.Round(used, 2), percent);
            }
            return new MemoryInfo(16, 8, 8, 50);
        }

        public static bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static bool RelaunchAsAdministrator(string arguments = "")
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = Process.GetCurrentProcess().MainModule?.FileName;
                }
                if (string.IsNullOrEmpty(exePath)) return false;

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                var proc = Process.Start(startInfo);
                if (proc != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        System.Windows.Application.Current.Shutdown();
                    });
                    return true;
                }
                return false;
            }
            catch
            {
                // User may have cancelled UAC prompt
                return false;
            }
        }

        public static async Task<(bool success, string output)> RunProcessAsync(string fileName, string arguments, bool elevated = false)
        {
            return await Task.Run(() =>
            {
                try
                {
                    bool needRunAs = elevated && !IsAdministrator();

                    var psi = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = needRunAs,
                        CreateNoWindow = !needRunAs,
                        RedirectStandardOutput = !needRunAs,
                        RedirectStandardError = !needRunAs
                    };

                    if (needRunAs)
                    {
                        psi.Verb = "runas";
                    }
                    else
                    {
                        psi.StandardOutputEncoding = Encoding.UTF8;
                        psi.StandardErrorEncoding = Encoding.UTF8;
                    }

                    using var proc = Process.Start(psi);
                    if (proc == null) return (false, "Failed to launch process.");

                    if (needRunAs)
                    {
                        proc.WaitForExit(30000);
                        return (proc.ExitCode == 0, $"Process finished with code {proc.ExitCode}");
                    }
                    else
                    {
                        string stdout = proc.StandardOutput.ReadToEnd();
                        string stderr = proc.StandardError.ReadToEnd();
                        proc.WaitForExit(30000);

                        bool ok = proc.ExitCode == 0;
                        string msg = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}\nError: {stderr}";
                        return (ok, msg.Trim());
                    }
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });
        }

        public static async Task<(bool success, string output)> RunPowerShellCommandAsync(string command, bool elevated = false)
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            string args = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";
            return await RunProcessAsync("powershell.exe", args, elevated);
        }

        public static bool DeleteRegistryValue(RegistryHive hive, string subKey, string valueName)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(subKey, writable: true);
                if (key != null && key.GetValue(valueName) != null)
                {
                    key.DeleteValue(valueName, throwOnMissingValue: false);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool SetRegistryDWord(RegistryHive hive, string subKey, string valueName, int value)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.CreateSubKey(subKey, writable: true);
                if (key != null)
                {
                    key.SetValue(valueName, value, RegistryValueKind.DWord);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool SetRegistryString(RegistryHive hive, string subKey, string valueName, string value)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.CreateSubKey(subKey, writable: true);
                if (key != null)
                {
                    key.SetValue(valueName, value, RegistryValueKind.String);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static object? GetRegistryValue(RegistryHive hive, string subKey, string valueName)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(subKey);
                return key?.GetValue(valueName);
            }
            catch
            {
                return null;
            }
        }

        public static async Task<(bool success, string output, string? details)> CreateRestorePoint(string description, IProgress<string>? progress = null)
        {
            progress?.Report("Checking Administrator privileges and system policies...");
            if (!IsAdministrator())
            {
                return (false, "Administrator privileges are required to create a System Restore Point.", null);
            }

            progress?.Report("Bypassing 24-hour rate limit and preparing Volume Shadow Copy (VSS)...");

            string script = $@"
$ErrorActionPreference = 'Stop'
try {{
    # 1. Enable computer restore on system drive
    Enable-ComputerRestore -Drive 'C:\' -ErrorAction SilentlyContinue

    # 2. Reset 24-hr creation rate limit
    $reg = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
    if (!(Test-Path $reg)) {{ New-Item $reg -Force | Out-Null }}
    Set-ItemProperty -Path $reg -Name 'SystemRestorePointCreationFrequency' -Value 0 -Type DWord -Force -ErrorAction SilentlyContinue

    # 3. Ensure VSS service is running
    Start-Service -Name 'VSS' -ErrorAction SilentlyContinue

    # 4. Checkpoint computer
    Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS'

    # 5. Verify creation
    $rp = Get-ComputerRestorePoint -ErrorAction SilentlyContinue | Select-Object -Last 1
    if ($rp) {{
        Write-Output ""VERIFIED: Sequence #$($rp.SequenceNumber) created at $($rp.CreationTime) ($($rp.Description))""
    }} else {{
        Write-Output ""CREATED: Restore point successfully registered.""
    }}
}} catch {{
    Write-Error $_.Exception.Message
}}
";
            progress?.Report("Creating volume snapshot via Volume Shadow Copy (typically takes 15-30s)...");
            var (success, output) = await RunPowerShellCommandAsync(script, elevated: false);

            if (success && output.Contains("VERIFIED:"))
            {
                var line = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                 .FirstOrDefault(l => l.Contains("VERIFIED:"))?.Trim() ?? output.Trim();
                return (true, line, line);
            }
            else if (success)
            {
                return (true, output.Trim(), output.Trim());
            }
            else
            {
                return (false, output.Trim(), null);
            }
        }

        public record InstalledDesktopApp(string Name, string Version, string Publisher, string UninstallString, bool IsHklm, string InstallLocation);

        public static List<InstalledDesktopApp> GetInstalledDesktopApps()
        {
            var list = new List<InstalledDesktopApp>();
            var hives = new[]
            {
                (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", true),
                (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", false)
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (hive, subKey, isHklm) in hives)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                    using var root = baseKey.OpenSubKey(subKey);
                    if (root == null) continue;

                    foreach (var subName in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var appKey = root.OpenSubKey(subName);
                            if (appKey == null) continue;

                            string name = appKey.GetValue("DisplayName")?.ToString()?.Trim() ?? "";
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            // Filter out system components, updates, and runtimes
                            if (appKey.GetValue("SystemComponent") is int sc && sc == 1) continue;
                            if (appKey.GetValue("ParentKeyName") != null) continue;
                            if (name.StartsWith("KB", StringComparison.OrdinalIgnoreCase)) continue;
                            if (name.StartsWith("Security Update", StringComparison.OrdinalIgnoreCase)) continue;
                            if (name.StartsWith("Update for", StringComparison.OrdinalIgnoreCase)) continue;
                            if (name.StartsWith("Microsoft Visual C++", StringComparison.OrdinalIgnoreCase)) continue;
                            if (name.StartsWith("Windows Software Development Kit", StringComparison.OrdinalIgnoreCase)) continue;
                            if (name.Equals("Windows Subsystem for Linux", StringComparison.OrdinalIgnoreCase)) continue;

                            if (seen.Contains(name)) continue;
                            seen.Add(name);

                            string ver = appKey.GetValue("DisplayVersion")?.ToString()?.Trim() ?? "";
                            string pub = appKey.GetValue("Publisher")?.ToString()?.Trim() ?? "";
                            string location = appKey.GetValue("InstallLocation")?.ToString()?.Trim() ?? "";
                            string uninst = appKey.GetValue("QuietUninstallString")?.ToString()?.Trim() ?? "";
                            if (string.IsNullOrWhiteSpace(uninst))
                            {
                                uninst = appKey.GetValue("UninstallString")?.ToString()?.Trim() ?? "";
                            }

                            if (!string.IsNullOrWhiteSpace(uninst))
                            {
                                list.Add(new InstalledDesktopApp(name, ver, pub, uninst, isHklm, location));
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return list.OrderBy(a => a.Name).ToList();
        }
    }
}
