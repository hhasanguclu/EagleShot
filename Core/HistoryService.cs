using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EagleShot.Core;

public class HistoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? FilePath { get; set; }
    public string? Url { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Kind { get; set; } = "Region";

    [JsonIgnore]
    public string DisplayName => FilePath != null ? Path.GetFileName(FilePath) : Url ?? "Screenshot";
    [JsonIgnore]
    public bool FileExists => FilePath != null && File.Exists(FilePath);
}

/// <summary>
/// Persists the list of captures so they can be browsed from the main window.
/// </summary>
public class HistoryService
{
    private readonly List<HistoryItem> _items = new();
    private readonly int _maxItems;
    private static string FilePath => Path.Combine(AppPaths.ConfigFolder, "history.json");

    public event Action? Changed;

    public HistoryService(int maxItems)
    {
        _maxItems = Math.Max(10, maxItems);
        Load();
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<HistoryItem> Items => _items;

    public void Add(HistoryItem item)
    {
        _items.Insert(0, item);
        if (_items.Count > _maxItems)
            _items.RemoveRange(_maxItems, _items.Count - _maxItems);
        Save();
    }

    public void Update(HistoryItem item)
    {
        if (_items.Contains(item)) Save();
    }

    public void Remove(HistoryItem item, bool deleteFile)
    {
        _items.Remove(item);
        if (deleteFile && item.FileExists)
        {
            try { File.Delete(item.FilePath!); } catch { }
        }
        Save();
    }

    public void Clear()
    {
        _items.Clear();
        Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var items = JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(FilePath));
                if (items != null)
                    _items.AddRange(items.OrderByDescending(i => i.CreatedAt).Take(_maxItems));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"History load failed: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ConfigFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"History save failed: {ex.Message}");
        }
        Changed?.Invoke();
    }
}
