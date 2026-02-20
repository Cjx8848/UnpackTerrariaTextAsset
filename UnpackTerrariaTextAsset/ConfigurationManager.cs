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
                OutputPath = "",
                EnableTexture2D = true
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
    
    /// <summary>
    /// WorkDir文件名映射字典。键为原始名称（或部分内容），值为替换后的名称。
    /// 例如：{"zh-Hans": "en-US"} 会将workdir中的zh-Hans文件匹配到export中的en-US文件
    /// </summary>
    public Dictionary<string, string> WorkDirFileNameMapping { get; set; } = new Dictionary<string, string>();
    
    /// <summary>
    /// 导出白名单列表。只有文件名中包含列表中任一关键词的资源才会被导出。
    /// 如果列表为空，则导出所有资源。
    /// 例如：["zh-Hans", "en-US", "Mouse_Text"] 只导出包含这些关键词的文件
    /// </summary>
    public List<string> ExportWhitelist { get; set; } = new List<string>();

    private bool _enableTexture2D = true;
    /// <summary>
    /// 是否启用Texture2D纹理的导入和导出功能
    /// </summary>
    public bool EnableTexture2D
    {
        get => _enableTexture2D;
        set
        {
            _enableTexture2D = value;
            ConfigurationManager.SaveConfig();
        }
    }
}