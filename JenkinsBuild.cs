// JenkinsBuild
// Shepherd Zhu
// Jenkins Build Helper
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public class JenkinsBuild : MonoBehaviour
{
    // 重要提醒：建议先在工作电脑上配好Groups和Labels，本脚本虽说遇到新文件可以添加到Addressables，但是不太可靠。

    /// <summary>
    /// 开始执行HybridCLR热更打包，默认打当前平台
    /// </summary>
    public static void BuildHotUpdate()
    {
        BuildHotUpdate(EditorUserBuildSettings.activeBuildTarget);
    }

    /// <summary>
    /// 开始执行HybridCLR热更打包
    /// </summary>
    /// <param name="target">目标平台</param>
    public static void BuildHotUpdate(BuildTarget target)
    {
        Console.WriteLine(
            $"[JenkinsBuild] Start building hot update for {Enum.GetName(typeof(BuildTarget), target)}"
        );
        try
        {
            CompileDllCommand.CompileDll(target);
            Il2CppDefGeneratorCommand.GenerateIl2CppDef();

            // 这几个生成依赖HotUpdateDlls
            LinkGeneratorCommand.GenerateLinkXml(target);

            // 生成裁剪后的aot dll
            StripAOTDllCommand.GenerateStripedAOTDlls(target);

            // 桥接函数生成依赖于AOT dll，必须保证已经build过，生成AOT dll
            MethodBridgeGeneratorCommand.GenerateMethodBridge(target);
            ReversePInvokeWrapperGeneratorCommand.GenerateReversePInvokeWrapper(target);
            AOTReferenceGeneratorCommand.GenerateAOTGenericReference(target);
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"[JenkinsBuild] ERROR while building hot update! Message:\n{e.ToString()}"
            );
            return;
        }

        // 复制打出来的DLL并进行替换
        string sourcePath = Path.Combine(
            Application.dataPath,
            $"../{SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target)}"
        );
        string destinationPath = Path.Combine(Application.dataPath, "HotUpdateDLLs");

        if (!Directory.Exists(sourcePath))
        {
            Console.WriteLine(
                "[JenkinsBuild] Source directory does not exist! Possibly HybridCLR build failed!"
            );
            return;
        }

        if (!Directory.Exists(destinationPath))
        {
            Console.WriteLine(
                "[JenkinsBuild] Destination directory does not exist! Abort the build!"
            );
            return;
        }

        string[] dllFiles = Directory.GetFiles(sourcePath, "*.dll");

        foreach (string dllFile in dllFiles)
        {
            string fileName = Path.GetFileName(dllFile);
            string destinationFile = Path.Combine(destinationPath, fileName + ".bytes");
            Console.WriteLine($"[JenkinsBuild] Copy: {dllFile}");
            File.Copy(dllFile, destinationFile, true);
        }

        Console.WriteLine("[JenkinsBuild] Hot Update DLLs copied successfully!");

        // 复制打出来的AOT元数据DLL并进行替换
        Console.WriteLine("[JenkinsBuild] Start copying AOT Metadata DLLs!");
        sourcePath = Path.Combine(
            Application.dataPath,
            $"../{SettingsUtil.GetAssembliesPostIl2CppStripDir(target)}"
        );
        destinationPath = Path.Combine(Application.dataPath, "HotUpdateDLLs/AOTMetadata");

        if (!Directory.Exists(sourcePath))
        {
            Console.WriteLine(
                "[JenkinsBuild] Source directory does not exist! Possibly HybridCLR build failed!"
            );
            return;
        }

        if (!Directory.Exists(destinationPath))
        {
            Console.WriteLine(
                "[JenkinsBuild] Destination directory does not exist! Abort the build!"
            );
            return;
        }

        // 获取AOTGenericReferences.cs文件的路径
        string aotReferencesFilePath = Path.Combine(
            Application.dataPath,
            SettingsUtil.HybridCLRSettings.outputAOTGenericReferenceFile
        );

        if (!File.Exists(aotReferencesFilePath))
        {
            Console.WriteLine(
                "[JenkinsBuild] AOTGenericReferences.cs file does not exist! Abort the build!"
            );
            return;
        }

        // 读取AOTGenericReferences.cs文件内容
        string[] aotReferencesFileContent = File.ReadAllLines(aotReferencesFilePath);

        // 查找PatchedAOTAssemblyList列表
        List<string> patchedAOTAssemblyList = new List<string>();

        for (int i = 0; i < aotReferencesFileContent.Length; i++)
        {
            if (aotReferencesFileContent[i].Contains("PatchedAOTAssemblyList"))
            {
                while (!aotReferencesFileContent[i].Contains("};"))
                {
                    if (aotReferencesFileContent[i].Contains("\""))
                    {
                        int startIndex = aotReferencesFileContent[i].IndexOf("\"") + 1;
                        int endIndex = aotReferencesFileContent[i].LastIndexOf("\"");
                        string dllName = aotReferencesFileContent[i].Substring(
                            startIndex,
                            endIndex - startIndex
                        );
                        patchedAOTAssemblyList.Add(dllName);
                    }
                    i++;
                }
                break;
            }
        }

        // 复制DLL文件到目标文件夹，并添加后缀名".bytes"
        foreach (string dllName in patchedAOTAssemblyList)
        {
            string sourceFile = Path.Combine(sourcePath, dllName);
            string destinationFile = Path.Combine(
                destinationPath,
                Path.GetFileName(dllName) + ".bytes"
            );

            if (File.Exists(sourceFile))
            {
                Console.WriteLine($"[JenkinsBuild] Copy: {sourceFile}");
                File.Copy(sourceFile, destinationFile, true);
                //SetAOTMetadataDllLabel("Assets/HotUpdateDLLs/" + Path.GetFileName(dllName) + ".bytes");
            }
            else
            {
                Console.WriteLine("[JenkinsBuild] AOTMetadata DLL file not found: " + dllName);
            }
        }

        AssetDatabase.SaveAssets();

        Console.WriteLine("[JenkinsBuild] BuildHotUpdate complete!");

        AssetDatabase.Refresh();

		// 刷新后开始拷贝DLL
		SetHotUpdateDllLabel("Assets/HotUpdateDLLs/Assembly-CSharp.dll.bytes");
		foreach(string dllName in patchedAOTAssemblyList)
		{
			SetAOTMetadataDllLabel("Assets/HotUpdateDLLs/AOTMetadata/" + Path.GetFileName(dllName) + ".bytes");
		}

		Console.WriteLine("[JenkinsBuild] Start building Addressables!");
		buildAddressableContent();
    }

    public static void BuildHotUpdateForWindows64()
    {
        BuildHotUpdate(BuildTarget.StandaloneWindows64);
    }

    public static void BuildHotUpdateForiOS()
    {
        BuildHotUpdate(BuildTarget.iOS);
    }

    public static void BuildHotUpdateForLinux64()
    {
        BuildHotUpdate(BuildTarget.StandaloneLinux64);
    }

    public static void BuildHotUpdateForAndroid()
    {
        BuildHotUpdate(BuildTarget.Android);
    }

    /// <summary>
    /// 将热更DLL加入到Addressables
    /// </summary>
    /// <param name="dllPath">DLL完整路径</param>
    private static void SetHotUpdateDllLabel(string dllPath)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetGroup group = settings.FindGroup("DLLs");
        var guid = AssetDatabase.AssetPathToGUID(dllPath);
        if (settings.FindAssetEntry(guid) != null)
        {
            Console.WriteLine(
                $"[JenkinsBuild.SetHotUpdateDLLLabel] {dllPath} already exist in Addressables. Abort!"
            );
            return;
        }
        var entry = settings.CreateOrMoveEntry(guid, group);
		entry.labels.Add("default");
        entry.labels.Add("HotUpdateDLL");
        entry.address = Path.GetFileName(dllPath);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
    }

    /// <summary>
    /// 将AOT元数据DLL加入到Addressables
    /// </summary>
    /// <param name="dllPath">DLL完整路径</param>
    private static void SetAOTMetadataDllLabel(string dllPath)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetGroup group = settings.FindGroup("DLLs");
        var guid = AssetDatabase.AssetPathToGUID(dllPath);
        if (settings.FindAssetEntry(guid) != null)
        {
            Console.WriteLine(
                $"[JenkinsBuild.SetAOTMetadataDLLLabel] {dllPath} already exist in Addressables. Abort!"
            );
            return;
        }
        var entry = settings.CreateOrMoveEntry(guid, group);
		entry.labels.Add("default");
        entry.labels.Add("AOTMetadataDLL");
        entry.address = Path.GetFileName(dllPath);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
    }

    private static bool buildAddressableContent()
    {
        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
        bool success = string.IsNullOrEmpty(result.Error);

        if (!success)
        {
            Console.WriteLine("[JenkinsBuild.buildAddressableContent] Addressables build error encountered: " + result.Error);
        }
        return success;
    }
}
