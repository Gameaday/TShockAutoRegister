using System.Text.Json;
using System.Text.Json.Serialization;
using TShockAPI;

#nullable enable

namespace AutoRegister;

public class Config
{
    public int PasswordLength { get; set; } = 10;

    // Use a private static field for the path to avoid redundant Path.Combine calls
    private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "AutoRegister.json");

    public void Write()
    {
        try
        {
            // Using the Source Generation context for zero-reflection serialization
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
        if (!File.Exists(ConfigPath))
        {
            var config = new Config();
            config.Write();
            return config;
        }

        try
        {
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

/// <summary>
/// This tells the .NET 9 compiler to generate serialization code at compile-time.
/// It's significantly faster and more memory-efficient than Newtonsoft.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Config))]
internal partial class ConfigSourceContext : JsonSerializerContext { }
