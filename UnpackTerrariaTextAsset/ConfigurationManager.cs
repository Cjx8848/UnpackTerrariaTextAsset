using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace UnpackTerrariaTextAsset;

public static class ConfigurationManager
{
    public const string CONFIG_FILENAME = "config.json";
    public static ConfigurationSettings Settings { get; }
    static ConfigurationManager()
    {
        string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILENAME);
        if (!File.Exists(configPath))
        {
            Settings = new ConfigurationSettings()
            {
                UseDarkTheme = false,
                UseCpp2Il = true,
                ImportDir = "import",
                ExportDir = "export",
                WorkDir = "work",
                OriginPath = "",
                OutputPath = ""
            };
            SaveConfig();
        }
        else
        {
            string configText = File.ReadAllText(configPath);
            Settings = JsonConvert.DeserializeObject<ConfigurationSettings>(configText) ?? new ConfigurationSettings();
        }
        EnsureDirectoriesExist();
    }

    public static void SaveConfig()
    {
        string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILENAME);
        if (Settings != null)
        {
            string configText = JsonConvert.SerializeObject(Settings, Formatting.Indented);
            File.WriteAllText(configPath, configText);
        }
    }

    private static void EnsureDirectoriesExist()
    {
        if (!string.IsNullOrEmpty(Settings.ImportDir) && !Directory.Exists(Settings.ImportDir))
        {
            Directory.CreateDirectory(Settings.ImportDir);
        }
        if (!string.IsNullOrEmpty(Settings.ExportDir) && !Directory.Exists(Settings.ExportDir))
        {
            Directory.CreateDirectory(Settings.ExportDir);
        }
        if (!string.IsNullOrEmpty(Settings.WorkDir) && !Directory.Exists(Settings.WorkDir))
        {
            Directory.CreateDirectory(Settings.WorkDir);
        }
    }
}

public class ConfigurationSettings
{
    private bool _useDarkTheme;
    public bool UseDarkTheme
    {
        get => _useDarkTheme;
        set
        {
            _useDarkTheme = value;
            ConfigurationManager.SaveConfig();
        }
    }

    private bool _useCpp2Il;
    public bool UseCpp2Il
    {
        get => _useCpp2Il;
        set
        {
            _useCpp2Il = value;
            ConfigurationManager.SaveConfig();
        }
    }

    public string ImportDir { get; set; } = "import";
    public string ExportDir { get; set; } = "export";
    public string WorkDir { get; set; } = "work";
    public string OriginPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
}