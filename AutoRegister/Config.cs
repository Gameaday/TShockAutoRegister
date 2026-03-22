using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using TShockAPI;

#nullable enable

namespace AutoRegister
{
    public class Config
    {
        public int PasswordLength { get; set; } = 10;
        
        // This color code (Tomato) is used for History Labs branding in the chat prefix
        public string ChatPrefixColor { get; set; } = "ff6347";

        public void Write()
        {
            string path = Path.Combine(TShock.SavePath, "AutoRegister.json");
            Directory.CreateDirectory(TShock.SavePath);
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public static Config Read()
        {
            string filepath = Path.Combine(TShock.SavePath, "AutoRegister.json");
            try
            {
                Directory.CreateDirectory(TShock.SavePath);
                if (!File.Exists(filepath))
                {
                    Config def = new Config();
                    def.Write();
                    return def;
                }
                var content = File.ReadAllText(filepath);
                return JsonConvert.DeserializeObject<Config>(content) ?? new Config();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[AutoRegister] Config error: {ex.Message}");
                return new Config();
            }
        }
    }
    
}