using System;
using System.IO;
using TShockAPI;

#nullable enable

namespace AutoRegister;

public class Config
{
    public int PasswordLength { get; set; } = 10;

    // Standard TShock path for plugin-specific configs
    private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "AutoRegister.json");

    public void Write()
    {
        try
        {
            // Ensure the directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

            string json = JsonSerializer.Serialize(this, ConfigSourceContext.Default.Config);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[AutoRegister] Failed to write config: {ex.Message}");
        }
    }

    public static Config Read()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                var config = new Config();
                config.Write();
                return config;
            }

            string json = File.ReadAllText(ConfigPath);
            // Source-generated deserialization is near-instant and reflection-free
            return JsonSerializer.Deserialize(json, ConfigSourceContext.Default.Config) ?? new Config();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[AutoRegister] Failed to read config: {ex.Message}");
            return new Config();
        }
    }
}

/// <summary>
/// Source generation context for high-performance JSON serialization in .NET 9.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Config))]
internal partial class ConfigSourceContext : JsonSerializerContext 
{ 
}
