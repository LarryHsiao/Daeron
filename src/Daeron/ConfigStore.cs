using System;
using System.IO;
using System.Text.Json;

namespace Daeron;

public static class ConfigStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Daeron",
        "config.json");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static Config Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Config();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Config>(json) ?? new Config();
        }
        catch
        {
            return new Config();
        }
    }

    public static void Save(Config config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config, WriteOptions));
        }
        catch
        {
        }
    }
}
