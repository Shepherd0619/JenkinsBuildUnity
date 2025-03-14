// JenkinsBuild
// Shepherd Zhu
// Jenkins Build Helper

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class JenkinsBuild
{
	// 重要提醒：建议先在工作电脑上配好Groups和Labels，本脚本虽说遇到新文件可以添加到Addressable，但是不太可靠。

	/// <summary>
	///     开始执行HybridCLR热更打包，默认打当前平台
	/// </summary>
	[MenuItem("Shepherd0619/Build Hot Update")]
	public static void BuildHotUpdate()
	{
		if (EditorUtility.DisplayDialog("Warning",
			    $"You are attempting to build hot update for {EditorUserBuildSettings.activeBuildTarget}.",
			    "Proceed", "Quit"))
			BuildHotUpdate(EditorUserBuildSettings.selectedBuildTargetGroup, EditorUserBuildSettings.activeBuildTarget);
	}

	[MenuItem("Shepherd0619/Build Playground Win Server")]
	public static void BuildPlaygroundWinServer()
	{
		var currentTarget = EditorUserBuildSettings.activeBuildTarget;

		if (!EditorUtility.DisplayDialog("Warning",
			    "You are attempting to build a Windows Server.",
			    "Proceed", "Quit"))
			return;

		var options = new BuildPlayerOptions();
		options.target = BuildTarget.StandaloneWindows64;
		options.subtarget = (int)StandaloneBuildSubtarget.Server;
		options.scenes = new[] { "Assets/_Project/Scenes/Playground.unity" };
		options.locationPathName =
			Path.Combine(Application.dataPath,
				$"../Build/WindowsServer/{new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds()}/PSXGame.exe");

		// 关闭热更新
		HybridCLRSettings.Instance.enable = false;
		HybridCLRSettings.Save();

		BuildPipeline.BuildPlayer(options);

		EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, currentTarget);
		HybridCLRSettings.Instance.enable = true;
		HybridCLRSettings.Save();
	}

	/// <summary>
	///     开始执行HybridCLR热更打包
	/// </summary>
	/// <param name="target">目标平台</param>
	public static void BuildHotUpdate(BuildTargetGroup group, BuildTarget target)
	{
		Console.WriteLine(
			$"[JenkinsBuild] Start building hot update for {Enum.GetName(typeof(BuildTarget), target)}"
		);

		EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);

		// 打开热更新
		HybridCLRSettings.Instance.enable = true;
		HybridCLRSettings.Save();

		try
		{
			CompileDllCommand.CompileDll(target);
			Il2CppDefGeneratorCommand.GenerateIl2CppDef();

			// 这几个生成依赖HotUpdateDlls
			LinkGeneratorCommand.GenerateLinkXml(target);

			// 生成裁剪后的aot dll
			StripAOTDllCommand.GenerateStripedAOTDlls(target);

			// 桥接函数生成依赖于AOT dll，必须保证已经build过，生成AOT dll
			MethodBridgeGeneratorCommand.GenerateMethodBridgeAndReversePInvokeWrapper(target);
			AOTReferenceGeneratorCommand.GenerateAOTGenericReference(target);
		}
		catch (Exception e)
		{
			Console.WriteLine(
				$"[JenkinsBuild] ERROR while building hot update! Message:\n{e}"
			);
			return;
		}

		// 复制打出来的DLL并进行替换
		var sourcePath = Path.Combine(
			Application.dataPath,
			$"../{SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target)}"
		);
		var destinationPath = Path.Combine(Application.dataPath, "HotUpdateDLLs");

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
				"[JenkinsBuild] Destination directory does not exist!"
			);
			Directory.CreateDirectory(destinationPath);
		}

		// string[] dllFiles = Directory.GetFiles(sourcePath, "*.dll");

		// foreach (string dllFile in dllFiles)
		// {
		//     string fileName = Path.GetFileName(dllFile);
		//     string destinationFile = Path.Combine(destinationPath, fileName + ".bytes");
		//     Console.WriteLine($"[JenkinsBuild] Copy: {dllFile}");
		//     File.Copy(dllFile, destinationFile, true);
		// }

		var hotUpdateAssemblyNames = SettingsUtil.HotUpdateAssemblyNamesExcludePreserved;
		for (var i = 0; i < hotUpdateAssemblyNames.Count; i++)
		{
			Console.WriteLine($"[JenkinsBuild] Copy: {hotUpdateAssemblyNames[i] + ".dll"}");
			File.Copy(sourcePath + "/" + hotUpdateAssemblyNames[i] + ".dll",
				Path.Combine(destinationPath, hotUpdateAssemblyNames[i] + ".dll.bytes"), true);
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
				"[JenkinsBuild] Destination directory does not exist!"
			);
			Directory.CreateDirectory(destinationPath);
		}

		// 获取AOTGenericReferences.cs文件的路径
		var aotReferencesFilePath = Path.Combine(
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
		var aotReferencesFileContent = File.ReadAllLines(aotReferencesFilePath);

		// 查找PatchedAOTAssemblyList列表
		var patchedAOTAssemblyList = new List<string>();

		for (var i = 0; i < aotReferencesFileContent.Length; i++)
			if (aotReferencesFileContent[i].Contains("PatchedAOTAssemblyList"))
			{
				while (!aotReferencesFileContent[i].Contains("};"))
				{
					if (aotReferencesFileContent[i].Contains("\""))
					{
						var startIndex = aotReferencesFileContent[i].IndexOf("\"") + 1;
						var endIndex = aotReferencesFileContent[i].LastIndexOf("\"");
						var dllName = aotReferencesFileContent[i].Substring(
							startIndex,
							endIndex - startIndex
						);
						patchedAOTAssemblyList.Add(dllName);
					}

					i++;
				}

				break;
			}

		// 复制DLL文件到目标文件夹，并添加后缀名".bytes"
		foreach (var dllName in patchedAOTAssemblyList)
		{
			var sourceFile = Path.Combine(sourcePath, dllName);
			var destinationFile = Path.Combine(
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

		// 刷新后开始给DLL加标签
		//SetHotUpdateDllLabel("Assets/HotUpdateDLLs/Assembly-CSharp.dll.bytes");
		for (var i = 0; i < hotUpdateAssemblyNames.Count; i++)
			SetHotUpdateDllLabel("Assets/HotUpdateDLLs/" + hotUpdateAssemblyNames[i] + ".dll.bytes");

		foreach (var dllName in patchedAOTAssemblyList)
			SetAOTMetadataDllLabel("Assets/HotUpdateDLLs/AOTMetadata/" + Path.GetFileName(dllName) + ".bytes");

		Console.WriteLine("[JenkinsBuild] Start building Addressables!");
		BuildAddressableContent();
	}

	public static void BuildHotUpdateForWindows64()
	{
		BuildHotUpdate(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
	}

	public static void BuildHotUpdateForiOS()
	{
		BuildHotUpdate(BuildTargetGroup.iOS, BuildTarget.iOS);
	}

	public static void BuildHotUpdateForLinux64()
	{
		BuildHotUpdate(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);
	}

	public static void BuildHotUpdateForAndroid()
	{
		BuildHotUpdate(BuildTargetGroup.Android, BuildTarget.Android);
	}

	public static void BuildWindowsServer()
	{
		// 获取命令行参数
		var args = Environment.GetCommandLineArgs();

		// 获取scenes参数
		var scenes = GetArgument(args, "scenes");
		if (scenes == null || scenes.Length <= 0) return;

		// 获取targetPath参数
		var targetPath = GetArgument(args, "targetPath");
		if (targetPath == null || targetPath.Length <= 0) return;

		var options = new BuildPlayerOptions();
		options.target = BuildTarget.StandaloneWindows64;
		options.subtarget = (int)StandaloneBuildSubtarget.Server;
		options.scenes = scenes;
		options.locationPathName = targetPath[0];

		// 关闭热更新
		HybridCLRSettings.Instance.enable = false;
		HybridCLRSettings.Save();

		var sb = new StringBuilder();
		for (var i = 0; i < scenes.Length; i++) sb.Append(scenes[i] + "; ");
		Console.WriteLine($"[JenkinsBuild] Start building WindowsServer! Scenes: {sb}, TargetPath: {targetPath[0]}");
		BuildPipeline.BuildPlayer(options);
	}

	public static void BuildLinuxServer()
	{
		// 获取命令行参数
		var args = Environment.GetCommandLineArgs();

		// 获取scenes参数
		var scenes = GetArgument(args, "scenes");
		if (scenes == null || scenes.Length <= 0) return;

		// 获取targetPath参数
		var targetPath = GetArgument(args, "targetPath");
		if (targetPath == null || targetPath.Length <= 0) return;

		var options = new BuildPlayerOptions();
		options.target = BuildTarget.StandaloneLinux64;
		options.subtarget = (int)StandaloneBuildSubtarget.Server;
		options.scenes = scenes;
		options.locationPathName = targetPath[0];

		// 关闭热更新
		HybridCLRSettings.Instance.enable = false;
		HybridCLRSettings.Save();

		var sb = new StringBuilder();
		for (var i = 0; i < scenes.Length; i++) sb.Append(scenes[i] + "\n");
		Console.WriteLine($"[JenkinsBuild] Start building LinuxServer! Scenes: {sb}, TargetPath: {targetPath[0]}");
		BuildPipeline.BuildPlayer(options);
	}

	/// <summary>
	///     获取某个参数
	/// </summary>
	/// <param name="args">全部的命令行参数</param>
	/// <param name="name">要获取的参数名称</param>
	/// <returns>所求参数</returns>
	private static string[] GetArgument(string[] args, string name)
	{
		var start = Array.FindIndex(args, arg => arg == $"-{name}");
		if (start < 0)
		{
			Console.WriteLine($"[JenkinsBuild.GetArgument] Can not find argument: {name}");
			return null;
		}

		start++;
		var end = Array.FindIndex(args, start, arg => arg[0] == '-');
		if (end < 0) end = args.Length;
		var count = end - start;
		if (count <= 0)
		{
			Console.WriteLine(
				$"[JenkinsBuild.GetArgument] Can not find argument value: {name}, Count: {count}, Start: {start}, End: {end}");
			return null;
		}

		var result = args.Skip(start).Take(count).ToArray();
		return result;
	}

	/// <summary>
	///     将热更DLL加入到Addressable
	/// </summary>
	/// <param name="dllPath">DLL完整路径</param>
	private static void SetHotUpdateDllLabel(string dllPath)
	{
		var settings = AddressableAssetSettingsDefaultObject.Settings;
		var group = settings.FindGroup("DLLs");
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
	///     将AOT元数据DLL加入到Addressable
	/// </summary>
	/// <param name="dllPath">DLL完整路径</param>
	private static void SetAOTMetadataDllLabel(string dllPath)
	{
		var settings = AddressableAssetSettingsDefaultObject.Settings;
		var group = settings.FindGroup("DLLs");
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

	/// <summary>
	///     打当前平台的打包Addressable
	/// </summary>
	/// <returns></returns>
	private static bool BuildAddressableContent()
	{
		var path = Path.Combine(Application.dataPath,
			"../ServerData/" + Enum.GetName(typeof(BuildTarget), EditorUserBuildSettings.activeBuildTarget));
		if (Directory.Exists(path)) Directory.Delete(path, true);

		AddressableAssetSettings.BuildPlayerContent(out var result);
		var success = string.IsNullOrEmpty(result.Error);

		if (!success)
			Console.WriteLine("[JenkinsBuild.BuildAddressableContent] Addressable build error encountered: " +
			                  result.Error);
		return success;
	}
}
