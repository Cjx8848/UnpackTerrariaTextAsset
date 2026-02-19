using LibCpp2IL.Elf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;

namespace UnpackTerrariaTextAsset;

class Program
{
    static void Main(string[] args)
    {
        var Arguements = ParseArguements(Environment.GetCommandLineArgs());
        
        // 如果提供了 -config 参数，显示当前配置
        if (Arguements.TryGetValue("-config", out _))
        {
            ShowConfig();
            return;
        }

        // 优先使用命令行参数，否则使用配置文件
        string? originPath = null;
        string? outputPath = null;

        if (Arguements.TryGetValue("-export", out var target))
        {
            originPath = target;
        }
        else if (Arguements.TryGetValue("-import", out var importArg) || 
                 Arguements.TryGetValue("-patch", out importArg) ||
                 Arguements.TryGetValue("-autowork", out importArg))
        {
            var sp = importArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (sp.Length >= 1)
                originPath = sp[0];
            if (sp.Length >= 2)
                outputPath = sp[1];
        }

        // 如果命令行没有提供，使用配置文件
        if (string.IsNullOrEmpty(originPath) && !string.IsNullOrEmpty(ConfigurationManager.Settings.OriginPath))
        {
            originPath = ConfigurationManager.Settings.OriginPath;
        }
        if (string.IsNullOrEmpty(outputPath) && !string.IsNullOrEmpty(ConfigurationManager.Settings.OutputPath))
        {
            outputPath = ConfigurationManager.Settings.OutputPath;
        }

        if (Arguements.TryGetValue("-export", out _))
        {
            if (string.IsNullOrEmpty(originPath))
            {
                Console.WriteLine("错误: 未指定源文件路径！请使用 -export <路径> 或在配置文件中设置 OriginPath");
                return;
            }
            if (!File.Exists(originPath))
            {
                Console.WriteLine($"目标文件不存在: {originPath}");
                return;
            }

            var unpack = new UnpackBundle();
            unpack.OpenFiles(originPath);
            unpack.BatchExport();
            Console.WriteLine($"导出完成！文件保存在: {unpack.ExportDir}");
        }

        if (Arguements.TryGetValue("-import", out _))
        {
            if (string.IsNullOrEmpty(originPath))
            {
                Console.WriteLine("错误: 未指定源文件路径！请使用 -import <源路径> <输出路径> 或在配置文件中设置 OriginPath");
                return;
            }
            if (string.IsNullOrEmpty(outputPath))
            {
                Console.WriteLine("错误: 未指定输出文件路径！请使用 -import <源路径> <输出路径> 或在配置文件中设置 OutputPath");
                return;
            }

            var ins = new UnpackBundle();
            if (!File.Exists(originPath))
            {
                Console.WriteLine($"未找到文件: {originPath}");
                return;
            }
            var tempPath = Path.Combine(ins.WorkDir, "temp");
            ins.OpenFiles(originPath);
            ins.BatchImport();
            ins.SaveToMemory();
            ins.SaveBundle(tempPath);
            var cop = new UnpackBundle();
            cop.OpenFiles(tempPath);
            cop.CompressBundle(outputPath, AssetsTools.NET.AssetBundleCompressionType.LZ4);
            Console.WriteLine($"导入完成！输出文件: {outputPath}");
        }

        if (Arguements.TryGetValue("-patch", out _))
        {
            if (string.IsNullOrEmpty(originPath))
            {
                Console.WriteLine("错误: 未指定源文件路径！请使用 -patch <源路径> <输出路径> 或在配置文件中设置 OriginPath");
                return;
            }
            if (string.IsNullOrEmpty(outputPath))
            {
                Console.WriteLine("错误: 未指定输出文件路径！请使用 -patch <源路径> <输出路径> 或在配置文件中设置 OutputPath");
                return;
            }

            var ins = new UnpackBundle();
            if (!File.Exists(originPath))
            {
                Console.WriteLine($"未找到文件: {originPath}");
                return;
            }
            var tempPath = Path.Combine(ins.WorkDir, "temp");
            ins.OpenFiles(originPath);
            ins.BatchExport();
            Sinicization(ins);
            ins.BatchImport();
            ins.SaveToMemory();
            ins.SaveBundle(tempPath);
            var cop = new UnpackBundle();
            cop.OpenFiles(tempPath);
            cop.CompressBundle(outputPath, AssetsTools.NET.AssetBundleCompressionType.LZ4);
            Console.WriteLine($"补丁完成！输出文件: {outputPath}");
        }

        if (Arguements.TryGetValue("-autowork", out _))
        {
            if (string.IsNullOrEmpty(originPath))
            {
                Console.WriteLine("错误: 未指定源文件路径！请使用 -autowork <源路径> <输出路径> 或在配置文件中设置 OriginPath");
                return;
            }
            if (string.IsNullOrEmpty(outputPath))
            {
                Console.WriteLine("错误: 未指定输出文件路径！请使用 -autowork <源路径> <输出路径> 或在配置文件中设置 OutputPath");
                return;
            }

            var bundle = new UnpackBundle();
            if (!File.Exists(originPath))
            {
                Console.WriteLine($"未找到文件: {originPath}");
                return;
            }

            // 1. 执行 export
            Console.WriteLine("步骤 1: 导出资源文件...");
            bundle.OpenFiles(originPath);
            bundle.BatchExport();
            Console.WriteLine($"导出完成！文件保存在: {bundle.ExportDir}");

            // 2. 匹配 workdir 和 export 中的文件
            Console.WriteLine("步骤 2: 匹配 workdir 和 export 中的文件...");
            int matchedCount = AutoWorkMatchFiles(bundle);
            Console.WriteLine($"匹配完成！共创建 {matchedCount} 个导入文件");

            // 3. 执行 import 后续流程
            Console.WriteLine("步骤 3: 导入资源文件...");
            var tempPath = Path.Combine(bundle.WorkDir, "temp");
            bundle.BatchImport();
            bundle.SaveToMemory();
            bundle.SaveBundle(tempPath);
            var cop = new UnpackBundle();
            cop.OpenFiles(tempPath);
            cop.CompressBundle(outputPath, AssetsTools.NET.AssetBundleCompressionType.LZ4);
            Console.WriteLine($"自动工作流完成！输出文件: {outputPath}");
        }

        // 如果没有提供任何参数，显示帮助信息
        if (Arguements.Count == 0 || (Arguements.Count == 1 && Arguements.ContainsKey("")))
        {
            ShowHelp();
        }
    }

    static void ShowConfig()
    {
        Console.WriteLine("当前配置:");
        Console.WriteLine($"  ImportDir:  {ConfigurationManager.Settings.ImportDir}");
        Console.WriteLine($"  ExportDir:  {ConfigurationManager.Settings.ExportDir}");
        Console.WriteLine($"  WorkDir:    {ConfigurationManager.Settings.WorkDir}");
        Console.WriteLine($"  OriginPath: {ConfigurationManager.Settings.OriginPath}");
        Console.WriteLine($"  OutputPath: {ConfigurationManager.Settings.OutputPath}");
        Console.WriteLine();
        Console.WriteLine($"配置文件位置: {Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigurationManager.CONFIG_FILENAME)}");
    }

    static void ShowHelp()
    {
        Console.WriteLine("用法:");
        Console.WriteLine("  UnpackTerrariaTextAsset.exe -export [data.unity3d路径]");
        Console.WriteLine("  UnpackTerrariaTextAsset.exe -import [源路径] [输出路径]");
        Console.WriteLine("  UnpackTerrariaTextAsset.exe -patch [源路径] [输出路径]");
        Console.WriteLine("  UnpackTerrariaTextAsset.exe -autowork [源路径] [输出路径]");
        Console.WriteLine("  UnpackTerrariaTextAsset.exe -config");
        Console.WriteLine();
        Console.WriteLine("参数:");
        Console.WriteLine("  -export <路径>    导出资源文件到 ExportDir");
        Console.WriteLine("  -import <源> <输出>  导入资源文件并生成新的 bundle");
        Console.WriteLine("  -patch <源> <输出>   自动汉化并生成补丁");
        Console.WriteLine("  -autowork <源> <输出>  自动匹配 workdir 和 export 文件并导入");
        Console.WriteLine("  -config           显示当前配置");
        Console.WriteLine();
        Console.WriteLine("注意: 可以在 config.json 中配置默认路径，命令行参数优先级高于配置文件。");
    }

    /// <summary>
    /// 自动匹配 workdir 和 export 中的文件，并在 import 目录中创建对应文件
    /// </summary>
    /// <param name="bundle">UnpackBundle 实例</param>
    /// <returns>成功匹配并创建的文件数量</returns>
    static int AutoWorkMatchFiles(UnpackBundle bundle)
    {
        int matchedCount = 0;

        // 检查 workdir 是否存在
        if (!Directory.Exists(bundle.WorkDir))
        {
            Console.WriteLine($"警告: WorkDir 不存在: {bundle.WorkDir}");
            return 0;
        }

        // 检查 exportdir 是否存在
        if (!Directory.Exists(bundle.ExportDir))
        {
            Console.WriteLine($"警告: ExportDir 不存在: {bundle.ExportDir}");
            return 0;
        }

        // 获取 workdir 中的所有文件
        var workFiles = Directory.GetFiles(bundle.WorkDir);
        if (workFiles.Length == 0)
        {
            Console.WriteLine($"警告: WorkDir 中没有文件: {bundle.WorkDir}");
            return 0;
        }

        // 构建 workdir 文件的字典：简短名称 -> 完整文件路径
        // 简短名称 = 去掉扩展名后，取 -resources.assets- 之前的部分
        // 例如: zh-Hans-resources.assets.txt -> zh-Hans
        var workFileDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var workFile in workFiles)
        {
            string fileName = Path.GetFileName(workFile);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(workFile);
            
            // 提取简短名称：找到 "-resources.assets" 并取前面的部分
            string shortName = fileNameWithoutExt;
            int resourcesIndex = fileNameWithoutExt.IndexOf("-resources.assets", StringComparison.OrdinalIgnoreCase);
            if (resourcesIndex > 0)
            {
                shortName = fileNameWithoutExt.Substring(0, resourcesIndex);
            }
            
            if (!workFileDict.ContainsKey(shortName))
            {
                workFileDict[shortName] = workFile;
            }
            else
            {
                Console.WriteLine($"警告: WorkDir 中存在重复名称 '{shortName}'，跳过: {fileName}");
            }
        }

        Console.WriteLine($"WorkDir 中找到 {workFileDict.Count} 个唯一名称");

        // 获取 exportdir 中的所有文件
        var exportFiles = Directory.GetFiles(bundle.ExportDir);
        if (exportFiles.Length == 0)
        {
            Console.WriteLine($"警告: ExportDir 中没有文件: {bundle.ExportDir}");
            return 0;
        }

        // 构建 export 文件的字典：简短名称 -> 完整文件名（带扩展名）
        // 简短名称 = 去掉扩展名后，取 -resources.assets- 之前的部分
        // 例如: zh-Hans-resources.assets-123.json -> zh-Hans
        var exportFileDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var exportFile in exportFiles)
        {
            string fileName = Path.GetFileName(exportFile);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(exportFile);
            
            // 提取简短名称：找到 "-resources.assets-" 并取前面的部分
            string shortName = fileNameWithoutExt;
            int resourcesIndex = fileNameWithoutExt.IndexOf("-resources.assets", StringComparison.OrdinalIgnoreCase);
            if (resourcesIndex > 0)
            {
                shortName = fileNameWithoutExt.Substring(0, resourcesIndex);
            }
            
            if (!exportFileDict.ContainsKey(shortName))
            {
                exportFileDict[shortName] = fileName;
            }
            else
            {
                Console.WriteLine($"警告: ExportDir 中存在重复名称 '{shortName}'，跳过: {fileName}");
            }
        }

        Console.WriteLine($"ExportDir 中找到 {exportFileDict.Count} 个唯一简短名称");

        // 确保 import 目录存在
        if (!Directory.Exists(bundle.ImportDir))
        {
            Directory.CreateDirectory(bundle.ImportDir);
        }

        // 匹配并创建文件
        foreach (var workEntry in workFileDict)
        {
            string shortName = workEntry.Key;
            string workFilePath = workEntry.Value;

            if (exportFileDict.TryGetValue(shortName, out string? exportFileName))
            {
                // 创建目标文件路径（使用 export 的文件名，但放在 import 目录）
                string targetPath = Path.Combine(bundle.ImportDir, exportFileName);
                
                // 复制 workdir 文件内容到 import 目录，使用 export 的文件名
                File.Copy(workFilePath, targetPath, overwrite: true);
                
                Console.WriteLine($"匹配成功: {shortName} -> {exportFileName}");
                matchedCount++;
            }
            else
            {
                Console.WriteLine($"未找到匹配: {shortName} (文件: {Path.GetFileName(workFilePath)})");
            }
        }

        return matchedCount;
    }

    public static void Sinicization(UnpackBundle bundle)
    {
        var files = Directory.GetFiles(bundle.ExportDir);
        var dic = files.Select(f => new { File = f, Name = Path.GetFileNameWithoutExtension(f) })
            .Where(x => x.Name.StartsWith("zh-Hans") || x.Name.StartsWith("fr-FR"))
            .GroupBy(x =>
            {
                var name = x.Name;
                var resourcePart = name.Contains('.') ? name[(name.IndexOf('.') + 1)..] : name[(name.IndexOf('-') + 1)..];
                var lastDashIndex = resourcePart.LastIndexOf('-');
                if (lastDashIndex > 0)
                {
                    resourcePart = resourcePart.Substring(0, lastDashIndex);
                }
                return resourcePart;
            })
            .Where(g => g.Count() == 2)
            .ToDictionary(
                g => Path.GetFileName(g.First(x => x.Name.StartsWith("zh-Hans")).File),
                g => Path.GetFileName(g.First(x => x.Name.StartsWith("fr-FR")).File)
            );
        foreach (var (zh_file, fr_file) in dic)
        {
            using var fs = new FileStream(Path.Combine(bundle.ImportDir, fr_file), FileMode.Create);
            fs.Write(File.ReadAllBytes(Path.Combine(bundle.ExportDir, zh_file)));
            fs.Close();
        }
        var en = files.FirstOrDefault(x => Path.GetFileName(x).StartsWith("en-US-resources.assets"));
        if (en == null)
            return;
        ModifyLanguage(bundle, en);
        var fr = dic.Values.FirstOrDefault(x => Path.GetFileName(x).StartsWith("fr-FR-resources.assets"));
        if (fr == null)
            return;
        ModifyLanguage(bundle, Path.Combine(bundle.ImportDir, fr));
        
    }

    static void ModifyLanguage(UnpackBundle bundle, string file)
    {
        var content = File.ReadAllText(file);
        var json = JsonConvert.DeserializeObject<JObject>(content);
        json!["Language"]!["French"] = "简体中文";
        File.WriteAllText(Path.Combine(bundle.ImportDir, Path.GetFileName(file)), JsonConvert.SerializeObject(json, Formatting.Indented));
    }

    public static Dictionary<string, string> ParseArguements(string[] args)
    {
        string text = null;
        string text2 = "";
        Dictionary<string, string> dictionary = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Length == 0)
            {
                continue;
            }
            if (args[i][0] == '-' || args[i][0] == '+')
            {
                if (text != null)
                {
                    dictionary.Add(text.ToLower(), text2);
                    text2 = "";
                }
                text = args[i];
                text2 = "";
            }
            else
            {
                if (text2 != "")
                {
                    text2 += " ";
                }
                text2 += args[i];
            }
        }
        if (text != null)
        {
            dictionary.Add(text.ToLower(), text2);
            text2 = "";
        }
        return dictionary;
    }


}