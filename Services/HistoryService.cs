using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WinDebloat.Models;

namespace WinDebloat.Services
{
    public class HistoryService
    {
        private static readonly string AppFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinDebloat");
        private static readonly string HistoryFile = Path.Combine(AppFolder, "undo_history.json");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static readonly object FileLock = new();

        public string HistoryFilePath => HistoryFile;

        public HistoryService()
        {
            try
            {
                if (!Directory.Exists(AppFolder))
                {
                    Directory.CreateDirectory(AppFolder);
                }
            }
            catch { }
        }

        public async Task<List<UndoRecord>> LoadHistoryAsync()
        {
            return await Task.Run(() =>
            {
                lock (FileLock)
                {
                    try
                    {
                        if (!File.Exists(HistoryFile))
                        {
                            return new List<UndoRecord>();
                        }

                        string json = File.ReadAllText(HistoryFile);
                        if (string.IsNullOrWhiteSpace(json))
                        {
                            return new List<UndoRecord>();
                        }

                        var records = JsonSerializer.Deserialize<List<UndoRecord>>(json, JsonOptions);
                        return records ?? new List<UndoRecord>();
                    }
                    catch
                    {
                        return new List<UndoRecord>();
                    }
                }
            });
        }

        public async Task SaveRecordAsync(UndoRecord record)
        {
            await Task.Run(() =>
            {
                lock (FileLock)
                {
                    try
                    {
                        var list = new List<UndoRecord>();
                        if (File.Exists(HistoryFile))
                        {
                            string json = File.ReadAllText(HistoryFile);
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                list = JsonSerializer.Deserialize<List<UndoRecord>>(json, JsonOptions) ?? new List<UndoRecord>();
                            }
                        }

                        // Remove existing record with same ID to avoid duplicates
                        list.RemoveAll(r => string.Equals(r.Id, record.Id, StringComparison.OrdinalIgnoreCase));

                        // Insert new record at beginning
                        list.Insert(0, record);

                        string newJson = JsonSerializer.Serialize(list, JsonOptions);
                        File.WriteAllText(HistoryFile, newJson);
                    }
                    catch { }
                }
            });
        }

        public async Task RemoveRecordAsync(string recordId)
        {
            await Task.Run(() =>
            {
                lock (FileLock)
                {
                    try
                    {
                        if (!File.Exists(HistoryFile)) return;

                        string json = File.ReadAllText(HistoryFile);
                        if (string.IsNullOrWhiteSpace(json)) return;

                        var list = JsonSerializer.Deserialize<List<UndoRecord>>(json, JsonOptions);
                        if (list == null) return;

                        int removed = list.RemoveAll(r => string.Equals(r.Id, recordId, StringComparison.OrdinalIgnoreCase));
                        if (removed > 0)
                        {
                            string newJson = JsonSerializer.Serialize(list, JsonOptions);
                            File.WriteAllText(HistoryFile, newJson);
                        }
                    }
                    catch { }
                }
            });
        }

        public async Task ClearHistoryAsync()
        {
            await Task.Run(() =>
            {
                lock (FileLock)
                {
                    try
                    {
                        if (File.Exists(HistoryFile))
                        {
                            File.Delete(HistoryFile);
                        }
                    }
                    catch { }
                }
            });
        }
    }
}
