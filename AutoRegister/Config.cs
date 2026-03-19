using System;
using System.IO;
using Newtonsoft.Json;
using TShockAPI;

#nullable enable

namespace AutoRegister;

/// <summary>
/// Configuration class for the AutoRegister plugin.
/// </summary>
public class Config
{
    // Length of the generated password
    public int PasswordLength { get; set; } = 10;

    /// <summary>
    /// Writes the current configuration to a JSON file.
    /// </summary>
    public void Write()
    {
        string path = Path.Combine(TShock.SavePath, "AutoRegister.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    /// <summary>
    /// Reads the configuration from the JSON file or creates a default one.
    /// </summary>
    /// <returns>A Config object.</returns>
    public static Config Read()
    {
        string path = Path.Combine(TShock.SavePath, "AutoRegister.json");
        
        if (!File.Exists(path))
        {
            var config = new Config();
            config.Write();
            return config;
        }

        try
        {
            return JsonConvert.DeserializeObject<Config>(File.ReadAllText(path)) ?? new Config();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[AutoRegister] Failed to read config: {ex.Message}");
            return new Config();
        }
    }
}