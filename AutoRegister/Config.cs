using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TShockAPI;

#nullable enable

namespace AutoRegister;

public class Config
{
    public int PasswordLength { get; set; } = 10;
    
    public bool EnableAutoLogin { get; set; } = true;

    // NEW: Prevents hackers from spoofing UUIDs by requiring the IP to match a previous session
    public bool RequireStrictIPForAutoLogin { get; set; } = true;

    public bool ShowTemporaryPassword { get; set; } = true;

    private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "AutoRegister.json");

    public void Write()
    {
        try
        {
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
            return JsonSerializer.Deserialize(json, ConfigSourceContext.Default.Config) ?? new Config();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[AutoRegister] Failed to read config: {ex.Message}");
            return new Config();
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Config))]
internal partial class ConfigSourceContext : JsonSerializerContext 
{ 
}