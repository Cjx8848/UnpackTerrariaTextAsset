using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Text;
using Image = SixLabors.ImageSharp.Image;

namespace UnpackTerrariaTextAsset;

public class UnpackBundle
{
    public BundleWorkspace Workspace { get; }
    public AssetsManager am { get => Workspace.am; }
    public BundleFileInstance BundleInst { get => Workspace.BundleInst!; }

    public AssetWorkspace AssetWorkspace { get; }

    public Dictionary<string, AssetContainer> LoadAssets { get; }

    public List<Tuple<AssetsFileInstance, byte[]>> ChangedAssetsDatas { get; set; }

    public string ImportDir => GetFullPath(ConfigurationManager.Settings.ImportDir);

    public string ExportDir => GetFullPath(ConfigurationManager.Settings.ExportDir);

    public string WorkDir => GetFullPath(ConfigurationManager.Settings.WorkDir);

    private string GetFullPath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
    }

    public UnpackBundle()
    {
        Workspace = new BundleWorkspace();
        AssetWorkspace = new AssetWorkspace(am, true);
        LoadAssets = [];
        ChangedAssetsDatas = new();
        if (!Directory.Exists(ImportDir))
        {
            Directory.CreateDirectory(ImportDir);
        }
        if (!Directory.Exists(ExportDir))
        {
            Directory.CreateDirectory(ExportDir);
        }
        if (!Directory.Exists(WorkDir))
        {
            Directory.CreateDirectory(WorkDir);
        }
    }
    public void OpenFiles(string file)
    {
        string classDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "classdata.tpk");
        am.LoadClassPackage(classDataPath);
        DetectedFileType fileType = FileTypeDetector.DetectFileType(file);
        if (fileType == DetectedFileType.BundleFile)
        {
            BundleFileInstance bundleInst = am.LoadBundleFile(file, false);

            if (bundleInst.file.BlockAndDirInfo.BlockInfos.Any(inf => inf.GetCompressionType() != 0))
            {
                DecompressToMemory(bundleInst);
                LoadBundle(bundleInst);
            }
            else
            {
                LoadBundle(bundleInst);
            }

        }
        else
        {
            throw new FieldAccessException("This doesn't seem to be an assets file or bundle.");
        }
    }

    private void DecompressToMemory(BundleFileInstance bundleInst)
    {
        AssetBundleFile bundle = bundleInst.file;

        MemoryStream bundleStream = new MemoryStream();
        bundle.Unpack(new AssetsFileWriter(bundleStream));

        bundleStream.Position = 0;

        AssetBundleFile newBundle = new AssetBundleFile();
        newBundle.Read(new AssetsFileReader(bundleStream));

        bundle.Close();
        bundleInst.file = newBundle;
    }

    private void LoadBundle(BundleFileInstance bundleInst)
    {
        Workspace.Reset(bundleInst);
        foreach (var file in Workspace.Files)
        {
            string name = file.Name;

            AssetBundleFile bundleFile = BundleInst.file;

            Stream assetStream = file.Stream;

            DetectedFileType fileType = FileTypeDetector.DetectFileType(new AssetsFileReader(assetStream), 0);
            assetStream.Position = 0;

            if (fileType == DetectedFileType.AssetsFile)
            {
                string assetMemPath = Path.Combine(BundleInst.path, name);
                AssetsFileInstance fileInst = am.LoadAssetsFile(assetStream, assetMemPath, true);
                string uVer = fileInst.file.Metadata.UnityVersion;
                am.LoadClassDatabaseFromPackage(uVer);
                if (BundleInst != null && fileInst.parentBundle == null)
                    fileInst.parentBundle = BundleInst;
                AssetWorkspace.LoadAssetsFile(fileInst, true);

            }
        }
        SetupContainers(AssetWorkspace);
        AssetWorkspace.GenerateAssetsFileLookup();
        foreach (var asset in AssetWorkspace.LoadedAssets)
        {

            AssetContainer cont = asset.Value;
            AssetNameUtils.GetDisplayNameFast(AssetWorkspace, cont, true, out string assetName, out string typeName);
            assetName = PathUtils.ReplaceInvalidPathChars(assetName);
            var assetPath = $"{assetName}-{Path.GetFileName(cont.FileInstance.path)}-{cont.PathId}";
            LoadAssets.Add(assetPath, cont);
        }

    }

    public void BatchImport()
    {
        // 预处理：修改 import 目录中的 JSON 文件的语言字段
        PreprocessJsonFiles();
        
        var dir = ImportDir;

        var files = Directory.GetFiles(dir);
        foreach (var file in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            string extension = Path.GetExtension(file).ToLower();
            
            if (LoadAssets.TryGetValue(fileName, out AssetContainer? cont) && cont != null)
            {
                AssetTypeValueField baseField = AssetWorkspace.GetBaseField(cont)!;
                
                // Check if this is a Texture2D asset (ClassId 28)
                if (cont.ClassId == 28 && extension == ".png" && ConfigurationManager.Settings.EnableTexture2D)
                {
                    ImportTexture2D(baseField, file, cont);
                }
                else
                {
                    // Regular text asset import
                    byte[] byteData = File.ReadAllBytes(file);
                    baseField["m_Script"].AsByteArray = byteData;

                    byte[] savedAsset = baseField.WriteToByteArray();

                    var replacer = new AssetsReplacerFromMemory(
                        cont.PathId, cont.ClassId, cont.MonoId, savedAsset);
                    AssetWorkspace.AddReplacer(cont.FileInstance, replacer, new MemoryStream(savedAsset));
                }
            }
        }
    }

    /// <summary>
    /// 预处理 ImportDir 中的 JSON 文件，修改语言字段
    /// </summary>
    private void PreprocessJsonFiles()
    {
        var replacements = ConfigurationManager.Settings.LanguageFieldReplacements;
        var filters = ConfigurationManager.Settings.LanguageFieldReplacementFilters;
        
        if (replacements == null || replacements.Count == 0)
            return;

        var dir = ImportDir;
        if (!Directory.Exists(dir))
            return;

        var jsonFiles = Directory.GetFiles(dir, "*.json");
        foreach (var file in jsonFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            
            // 检查是否在过滤器中
            bool skipFile = false;
            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    if (fileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"跳过语言字段修改（匹配过滤器: {filter}）: {fileName}");
                        skipFile = true;
                        break;
                    }
                }
            }
            
            if (skipFile)
                continue;

            try
            {
                string content = File.ReadAllText(file);
                bool modified = false;

                foreach (var replacement in replacements)
                {
                    string fieldName = replacement.Key;
                    string newValue = replacement.Value;
                    
                    // 查找 "Language": { ... "FieldName": "OldValue" ... } 模式
                    string pattern = $"\"{fieldName}\"\\s*:\\s*\"[^\"]*\"";
                    var regex = new System.Text.RegularExpressions.Regex(pattern);
                    var match = regex.Match(content);
                    
                    if (match.Success)
                    {
                        string oldValue = match.Value.Split(':')[1].Trim().Trim('"');
                        if (oldValue != newValue)
                        {
                            string replacementStr = $"\"{fieldName}\": \"{newValue}\"";
                            content = regex.Replace(content, replacementStr, 1);
                            Console.WriteLine($"语言字段修改: {fileName} - {fieldName} = \"{oldValue}\" -> \"{newValue}\"");
                            modified = true;
                        }
                    }
                }

                if (modified)
                {
                    File.WriteAllText(file, content);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理文件失败 {fileName}: {ex.Message}");
            }
        }
    }

    private void ImportTexture2D(AssetTypeValueField baseField, string filePath, AssetContainer cont)
    {
        try
        {
            // 使用 UABEA 的导入方式
            TextureFormat fmt = (TextureFormat)baseField["m_TextureFormat"].AsInt;
            
            byte[] platformBlob = TextureHelper.GetPlatformBlob(baseField);
            uint platform = cont.FileInstance.file.Metadata.TargetPlatform;

            int mips = baseField["m_MipCount"].AsInt;
            if (mips < 1) mips = 1;

            byte[] encImageBytes = TextureImportExport.Import(filePath, fmt, out int width, out int height, ref mips, platform, platformBlob);

            if (encImageBytes == null)
            {
                Console.WriteLine($"导入纹理失败 {Path.GetFileName(filePath)}: 无法编码纹理格式 {fmt}");
                return;
            }

            // 检查是否需要格式转换（ETC_RGB4 -> DXT1）
            TextureFormat finalFormat = fmt;
            if (fmt == TextureFormat.ETC_RGB4)
            {
                finalFormat = TextureFormat.DXT1;
                Console.WriteLine($"  格式转换: {fmt} -> {finalFormat}");
            }

            AssetTypeValueField m_StreamData = baseField["m_StreamData"];
            m_StreamData["offset"].AsInt = 0;
            m_StreamData["size"].AsInt = 0;
            m_StreamData["path"].AsString = "";

            if (!baseField["m_MipCount"].IsDummy)
                baseField["m_MipCount"].AsInt = mips;

            baseField["m_TextureFormat"].AsInt = (int)finalFormat;
            baseField["m_CompleteImageSize"].AsInt = encImageBytes.Length;
            baseField["m_Width"].AsInt = width;
            baseField["m_Height"].AsInt = height;

            AssetTypeValueField image_data = baseField["image data"];
            image_data.Value.ValueType = AssetValueType.ByteArray;
            image_data.TemplateField.ValueType = AssetValueType.ByteArray;
            image_data.AsByteArray = encImageBytes;

            byte[] savedAsset = baseField.WriteToByteArray();
            var replacer = new AssetsReplacerFromMemory(
                cont.PathId, cont.ClassId, cont.MonoId, savedAsset);
            AssetWorkspace.AddReplacer(cont.FileInstance, replacer, new MemoryStream(savedAsset));

            Console.WriteLine($"导入纹理: {Path.GetFileName(filePath)} ({width}x{height}, 格式: {finalFormat})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"导入纹理失败 {Path.GetFileName(filePath)}: {ex.Message}");
        }
    }

    public void BatchExport()
    {
        var dir = ExportDir;
        int textureCount = 0;
        int textAssetCount = 0;
        int skippedCount = 0;
        
        // 打印所有不同的 ClassId 以便调试
        var uniqueClassIds = LoadAssets.Values.Select(c => c.ClassId).Distinct().OrderBy(id => id).ToList();
        Console.WriteLine($"加载的资源类型 (ClassId): {string.Join(", ", uniqueClassIds)}");
        
        // 打印白名单配置
        var whitelist = ConfigurationManager.Settings.ExportWhitelist;
        if (whitelist.Count > 0)
        {
            Console.WriteLine($"应用导出白名单: {string.Join(", ", whitelist)}");
        }

        foreach (var (_, cont) in LoadAssets)
        {
            AssetTypeValueField baseField = AssetWorkspace.GetBaseField(cont)!;
            var name = baseField?["m_Name"]?.AsString;
            if (name == null) { continue; }

            name = PathUtils.ReplaceInvalidPathChars(name);
            string fileName = $"{name}-{Path.GetFileName(cont.FileInstance.path)}-{cont.PathId}";
            
            // 检查白名单
            if (whitelist.Count > 0)
            {
                bool isWhitelisted = false;
                foreach (var keyword in whitelist)
                {
                    if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fileName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isWhitelisted = true;
                        break;
                    }
                }
                
                if (!isWhitelisted)
                {
                    skippedCount++;
                    continue;
                }
            }

            // Check if this is a Texture2D asset (ClassId 28 is Texture2D)
            if (cont.ClassId == 28 && ConfigurationManager.Settings.EnableTexture2D)
            {
                ExportTexture2D(baseField, name, dir, fileName, cont);
                textureCount++;
            }
            else
            {
                // Regular text asset export
                var byteData = baseField?["m_Script"]?.AsByteArray;
                if (byteData == null) { continue; }

                string extension = ".json";
                string ucontExt = TextAssetHelper.GetUContainerExtension(cont);
                if (ucontExt != string.Empty)
                {
                    extension = ucontExt;
                }

                string file = Path.Combine(dir, $"{fileName}{extension}");
                File.WriteAllBytes(file, byteData);
                textAssetCount++;
            }
        }
        
        Console.WriteLine($"导出统计: {textAssetCount} 个文本资源, {textureCount} 个纹理资源, 跳过 {skippedCount} 个资源");
    }

    private void ExportTexture2D(AssetTypeValueField baseField, string name, string dir, string fileName, AssetContainer cont)
    {
        try
        {
            // 使用 UABEA 的导出方式
            TextureFile texFile = TextureFile.ReadTextureFile(baseField);

            // 0x0 texture, usually called like Font Texture or smth
            if (texFile.m_Width == 0 && texFile.m_Height == 0)
            {
                Console.WriteLine($"警告: 纹理尺寸为 0x0: {name}");
                return;
            }

            if (!TextureHelper.GetResSTexture(texFile, cont.FileInstance))
            {
                string resSName = Path.GetFileName(texFile.m_StreamData.path);
                Console.WriteLine($"警告: resS 文件未找到: {resSName}");
                return;
            }

            byte[] data = TextureHelper.GetRawTextureBytes(texFile, cont.FileInstance);

            if (data == null)
            {
                string resSName = Path.GetFileName(texFile.m_StreamData.path);
                Console.WriteLine($"警告: resS 文件在磁盘上未找到: {resSName}");
                return;
            }

            byte[] platformBlob = TextureHelper.GetPlatformBlob(baseField);
            uint platform = cont.FileInstance.file.Metadata.TargetPlatform;

            string file = Path.Combine(dir, $"{fileName}.png");
            bool success = TextureImportExport.Export(data, file, texFile.m_Width, texFile.m_Height, (TextureFormat)texFile.m_TextureFormat, platform, platformBlob);
            
            if (success)
            {
                Console.WriteLine($"导出纹理: {name} -> {fileName}.png ({texFile.m_Width}x{texFile.m_Height})");
            }
            else
            {
                string texFormat = ((TextureFormat)texFile.m_TextureFormat).ToString();
                Console.WriteLine($"导出纹理失败 {name}: 无法解码纹理格式 {texFormat}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"导出纹理失败 {name}: {ex.Message}");
        }
    }

    public void CompressBundle(string path, AssetBundleCompressionType type)
    {
        using FileStream fs = File.Open(path, FileMode.Create);
        using AssetsFileWriter w = new AssetsFileWriter(fs);
        BundleInst.file.Pack(BundleInst.file.Reader, w, type, false);
    }

    private void BatchExportDump()
    {
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ExportDir);

        foreach (var (name, cont) in LoadAssets)
        {
            string file = Path.Combine(dir, $"{name}.json");

            using (FileStream fs = File.Open(file, FileMode.Create))
            using (StreamWriter sw = new StreamWriter(fs))
            {
                AssetTypeValueField? baseField = AssetWorkspace.GetBaseField(cont);

                if (baseField == null)
                {
                    sw.WriteLine("Asset failed to deserialize.");
                    continue;
                }

                AssetImportExport dumper = new();
                dumper.DumpJsonAsset(sw, baseField);
            }
        }
    }

    private void BatchImportDump()
    {
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ImportDir);
        var files = Directory.GetFiles(dir);
        foreach (var file in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            if (LoadAssets.TryGetValue(fileName, out AssetContainer? cont) && cont != null)
            {
                using FileStream fs = File.OpenRead(file);
                using StreamReader sr = new StreamReader(fs);
                var importer = new AssetImportExport();

                byte[]? bytes;
                string? exceptionMessage;

                AssetTypeTemplateField tempField = AssetWorkspace.GetTemplateField(cont);
                bytes = importer.ImportJsonAsset(tempField, sr, out exceptionMessage);

                if (bytes == null)
                {
                    throw new Exception("Something went wrong when reading the dump file:\n" + exceptionMessage);
                }

                AssetsReplacer replacer = AssetImportExport.CreateAssetReplacer(cont, bytes);
                AssetWorkspace.AddReplacer(cont.FileInstance, replacer, new MemoryStream(bytes));
            }
        }
    }

    public void SaveToMemory()
    {
        var fileToReplacer = new Dictionary<AssetsFileInstance, List<AssetsReplacer>>();
        var changedFiles = AssetWorkspace.GetChangedFiles();
        foreach (var newAsset in AssetWorkspace.NewAssets)
        {
            AssetID assetId = newAsset.Key;
            AssetsReplacer replacer = newAsset.Value;
            string fileName = assetId.fileName;

            if (AssetWorkspace.LoadedFileLookup.TryGetValue(fileName.ToLower(), out AssetsFileInstance? file))
            {
                if (!fileToReplacer.ContainsKey(file))
                    fileToReplacer[file] = new List<AssetsReplacer>();

                fileToReplacer[file].Add(replacer);
            }
        }
        if (AssetWorkspace.fromBundle)
        {
            ChangedAssetsDatas.Clear();
            foreach (var file in changedFiles)
            {
                List<AssetsReplacer> replacers;
                if (fileToReplacer.ContainsKey(file))
                    replacers = fileToReplacer[file];
                else
                    replacers = new List<AssetsReplacer>(0);
                using (MemoryStream ms = new MemoryStream())
                using (AssetsFileWriter w = new AssetsFileWriter(ms))
                {
                    file.file.Write(w, 0, replacers);
                    ChangedAssetsDatas.Add(new Tuple<AssetsFileInstance, byte[]>(file, ms.ToArray()));
                }
            }
        }

        List<Tuple<AssetsFileInstance, byte[]>> assetDatas = ChangedAssetsDatas;
        foreach (var tup in assetDatas)
        {
            AssetsFileInstance fileInstance = tup.Item1;
            byte[] assetData = tup.Item2;

            string assetName = Path.GetFileName(fileInstance.path);
            Workspace.AddOrReplaceFile(new MemoryStream(assetData), assetName, true);
            am.UnloadAssetsFile(fileInstance.path);

        }
    }

    public void SaveBundle(string path)
    {
        List<BundleReplacer> replacers = Workspace.GetReplacers();
        using FileStream fs = File.Open(path, FileMode.Create);
        using AssetsFileWriter w = new AssetsFileWriter(fs);
        BundleInst.file.Write(w, replacers.ToList());
    }


    private void SetupContainers(AssetWorkspace Workspace)
    {
        if (Workspace.LoadedFiles.Count == 0)
        {
            return;
        }

        UnityContainer ucont = new UnityContainer();
        foreach (AssetsFileInstance file in Workspace.LoadedFiles)
        {
            AssetsFileInstance? actualFile;
            AssetTypeValueField? ucontBaseField;
            if (UnityContainer.TryGetBundleContainerBaseField(Workspace, file, out actualFile, out ucontBaseField))
            {
                ucont.FromAssetBundle(am, actualFile, ucontBaseField);
            }
            else if (UnityContainer.TryGetRsrcManContainerBaseField(Workspace, file, out actualFile, out ucontBaseField))
            {
                ucont.FromResourceManager(am, actualFile, ucontBaseField);
            }
        }

        foreach (var asset in Workspace.LoadedAssets)
        {
            AssetPPtr pptr = new AssetPPtr(asset.Key.fileName, 0, asset.Key.pathID);
            string? path = ucont.GetContainerPath(pptr);
            if (path != null)
            {
                asset.Value.Container = path;
            }
        }
    }
}
