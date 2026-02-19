# 文件名映射功能说明

## 功能简介

配置文件中的 `WorkDirFileNameMapping` 选项允许你在不修改 WorkDir 中的文件名的情况下，将 WorkDir 中的文件映射到 ExportDir 中的不同名称的文件。

## 使用场景

典型的使用场景是：你有简体中文（zh-Hans）的翻译文件放在 WorkDir 中，但你想用这些文件替换英文（en-US）的资源。

## 配置方法

在 `config.json` 中添加 `WorkDirFileNameMapping` 字典：

```json
{
  "UseDarkTheme": false,
  "UseCpp2Il": true,
  "ImportDir": "C:/Users/Cjx/Desktop/Code/Hans/Import",
  "ExportDir": "C:/Users/Cjx/Desktop/Code/Hans/Export",
  "WorkDir": "C:/Users/Cjx/Desktop/Code/Hans/WorkDir",
  "OriginPath": "C:/Users/Cjx/Desktop/Code/Hans/data.unity3d",
  "OutputPath": "C:/Users/Cjx/Desktop/Code/Hans/Output/data.unity3d",
  "WorkDirFileNameMapping": {
    "zh-Hans": "en-US"
  }
}
```

## 工作原理

### 示例1：简单的语言代码替换

**WorkDir 文件：**
- `zh-Hans-resources.assets.txt`
- `zh-Hans.Game-resources.assets.txt`

**ExportDir 文件：**
- `en-US-resources.assets-123.json`
- `en-US.Game-resources.assets-456.json`

**配置：**
```json
"WorkDirFileNameMapping": {
  "zh-Hans": "en-US"
}
```

**匹配结果：**
- `zh-Hans-resources.assets.txt` → `en-US-resources.assets-123.json` ✓
- `zh-Hans.Game-resources.assets.txt` → `en-US.Game-resources.assets-456.json` ✓

### 示例2：多种映射规则

你可以添加多个映射规则：

```json
"WorkDirFileNameMapping": {
  "zh-Hans": "en-US",
  "zh-Hant": "fr-FR",
  "Japanese": "de-DE"
}
```

**注意：** 只会应用第一个匹配的映射规则。

## 使用 -autowork 指令

配置好映射后，使用 `-autowork` 指令：

```bash
UnpackTerrariaTextAsset.exe -autowork
```

或者直接使用配置文件中的路径：

```bash
UnpackTerrariaTextAsset.exe -autowork C:/path/to/data.unity3d C:/path/to/output/data.unity3d
```

## 完整工作流程

1. **准备 WorkDir**: 将你的翻译文件（如简体中文）放入 WorkDir
2. **配置映射**: 在 config.json 中设置 `WorkDirFileNameMapping`
3. **运行 -autowork**: 程序会自动导出原始资源，应用映射匹配，然后导入

## 注意事项

1. 映射是部分匹配，例如 `zh-Hans.Game` 会被映射为 `en-US.Game`
2. 映射不区分大小写
3. 只会应用第一个匹配的映射规则
4. WorkDir 中的文件名不会被修改，只在匹配时使用映射后的名称

## 查看当前配置

使用 `-config` 指令查看当前配置：

```bash
UnpackTerrariaTextAsset.exe -config
```
