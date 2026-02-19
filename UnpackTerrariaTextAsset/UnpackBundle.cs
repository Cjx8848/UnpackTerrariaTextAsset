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
                if (cont.ClassId == 28 && extension == ".png")
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

    private void ImportTexture2D(AssetTypeValueField baseField, string filePath, AssetContainer cont)
    {
        try
        {
            // Load PNG image
            using (var image = Image.Load(filePath))
            {
                // Get texture info from baseField
                int origWidth = baseField["m_Width"].AsInt;
                int origHeight = baseField["m_Height"].AsInt;
                
                // Resize if dimensions don't match
                if (image.Width != origWidth || image.Height != origHeight)
                {
                    image.Mutate(x => x.Resize(origWidth, origHeight));
                    Console.WriteLine($"调整纹理尺寸: {Path.GetFileName(filePath)} ({image.Width}x{image.Height} -> {origWidth}x{origHeight})");
                }

                // Convert to BGRA32 format
                using (var bgraImage = image.CloneAs<Bgra32>())
                {
                    // Flip vertically (Unity stores textures flipped)
                    bgraImage.Mutate(i => i.Flip(FlipMode.Vertical));
                    
                    // Get raw pixel data
                    byte[] pixelData = new byte[origWidth * origHeight * 4];
                    bgraImage.CopyPixelDataTo(pixelData);
                    
                    // Read current texture info
                    var texture = TextureFile.ReadTextureFile(baseField);
                    
                    // Get the texture format
                    int textureFormat = baseField["m_TextureFormat"].AsInt;
                    
                    // Encode the pixel data to the appropriate format
                    byte[] encodedData = EncodeTextureData(pixelData, origWidth, origHeight, textureFormat);
                    
                    // Check if texture uses stream data
                    var streamData = baseField["m_StreamData"];
                    long streamOffset = streamData["offset"].AsLong;
                    
                    if (streamOffset > 0 || (streamData["size"].AsLong > 0 && !string.IsNullOrEmpty(streamData["path"].AsString)))
                    {
                        // Streamed texture - save to separate file
                        string streamPath = streamData["path"].AsString;
                        if (!string.IsNullOrEmpty(streamPath))
                        {
                            // Update the stream file
                            string fullStreamPath = Path.Combine(Path.GetDirectoryName(this.BundleInst.path) ?? "", streamPath);
                            if (File.Exists(fullStreamPath))
                            {
                                using (var fs = new FileStream(fullStreamPath, FileMode.Open, FileAccess.Write))
                                {
                                    fs.Seek(streamOffset, SeekOrigin.Begin);
                                    fs.Write(encodedData, 0, encodedData.Length);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Embedded texture - update image data directly
                        baseField["image data"].AsByteArray = encodedData;
                    }
                    
                    // Update texture dimensions if changed
                    baseField["m_Width"].AsInt = origWidth;
                    baseField["m_Height"].AsInt = origHeight;
                    
                    // Write the modified asset
                    byte[] savedAsset = baseField.WriteToByteArray();
                    var replacer = new AssetsReplacerFromMemory(
                        cont.PathId, cont.ClassId, cont.MonoId, savedAsset);
                    AssetWorkspace.AddReplacer(cont.FileInstance, replacer, new MemoryStream(savedAsset));
                    
                    Console.WriteLine($"导入纹理: {Path.GetFileName(filePath)} ({origWidth}x{origHeight})");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"导入纹理失败 {Path.GetFileName(filePath)}: {ex.Message}");
        }
    }

    private byte[] EncodeTextureData(byte[] rgbaData, int width, int height, int textureFormat)
    {
        // For now, just return RGBA32 data directly
        // Unity texture format 4 is RGBA32
        if (textureFormat == 4 || textureFormat == 0)
        {
            // Convert BGRA to RGBA if needed
            byte[] rgba = new byte[rgbaData.Length];
            for (int i = 0; i < rgbaData.Length; i += 4)
            {
                rgba[i] = rgbaData[i + 2];     // R
                rgba[i + 1] = rgbaData[i + 1]; // G
                rgba[i + 2] = rgbaData[i];     // B
                rgba[i + 3] = rgbaData[i + 3]; // A
            }
            return rgba;
        }
        
        // For other formats, return data as-is (may need format-specific encoding)
        return rgbaData;
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
            if (cont.ClassId == 28)
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
            // Read Texture2D data
            var texture = TextureFile.ReadTextureFile(baseField);
            
            // Use the FileInstance directly from the container
            AssetsFileInstance fileInst = cont.FileInstance;
            
            // Get the raw texture data
            byte[] textureData = texture.GetTextureData(fileInst);
            
            if (textureData == null || textureData.Length == 0)
            {
                Console.WriteLine($"警告: 无法获取纹理数据: {name}");
                return;
            }

            // Convert to ImageSharp image
            using (var image = Image.LoadPixelData<Bgra32>(textureData, texture.m_Width, texture.m_Height))
            {
                // Flip vertically (Unity stores textures flipped)
                image.Mutate(i => i.Flip(FlipMode.Vertical));
                
                // Save as PNG
                string file = Path.Combine(dir, $"{fileName}.png");
                image.SaveAsPng(file);
                Console.WriteLine($"导出纹理: {name} -> {fileName}.png ({texture.m_Width}x{texture.m_Height})");
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
