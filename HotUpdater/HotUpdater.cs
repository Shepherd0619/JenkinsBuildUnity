// HotUpdater
// Shepherd Zhu
// Fetch the update and load dlls before enter the game.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Reflection;
using HybridCLR;

public class HotUpdater : MonoBehaviour
{
	public string versionNumber;
	private string progressText;

	private void OnGUI()
	{
		GUIStyle style = new GUIStyle(GUI.skin.label);
		style.alignment = TextAnchor.LowerRight;
		style.normal.textColor = Color.white;

		GUI.Label(new Rect(Screen.width - 100, Screen.height - 30, 100, 30), "Version: " + versionNumber, style);
		GUI.Label(new Rect(Screen.width - 100, Screen.height - 60, 100, 30), "ResVer: " + PlayerPrefs.GetString("ResVersion"), style);

		GUIStyle progressStyle = new GUIStyle(GUI.skin.label);
		style.alignment = TextAnchor.MiddleCenter;
		style.normal.textColor = Color.white;
		GUI.Label(new Rect(Screen.width * 0.5f, Screen.height * 0.5f,300,30), progressText, progressStyle);
	}

	private void Start()
	{
		progressText = "Hot Updater Initializing...";
		StartCoroutine(StartCheckForResDownload());
	}

	IEnumerator StartCheckForResDownload()
	{
		Debug.Log("[HotUpdater] Start looking for local catalog cache.");
		string _catalogPath = Application.persistentDataPath + "/com.unity.addressables";
		if (Directory.Exists(_catalogPath))
		{
			try
			{
				Directory.Delete(_catalogPath, true);
				Debug.Log("[HotUpdater] Successfully delete catalog cache!");
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[HotUpdater] Unable to delete catalog cache. \n{e.ToString()}");
			}
		}
		Debug.Log("[HotUpdater] Looking for local catalog cache is complete!");
		var init = Addressables.InitializeAsync(false);
		yield return init;
		if (init.Status == AsyncOperationStatus.Succeeded)
		{
			var checkUpdateCatlogHandle = Addressables.CheckForCatalogUpdates(false);
			yield return checkUpdateCatlogHandle;
			if (checkUpdateCatlogHandle.Status == AsyncOperationStatus.Failed)
			{
				progressText = "Failed on downloading updates!";
				Debug.LogError("[HotUpdater] Failed on Addressables.CheckForCatalogUpdates!");
				yield break;
			}
			else if (checkUpdateCatlogHandle.Result.Count > 0)
			{
				var updateCatlogHandle = Addressables.UpdateCatalogs(checkUpdateCatlogHandle.Result, false);
				yield return updateCatlogHandle;
				if(updateCatlogHandle.Status == AsyncOperationStatus.Failed)
				{
					progressText = "Failed on downloading updates!";
					Debug.LogError("[HotUpdater] Failed on Addressables.UpdateCatalogs!");
					yield break;
				}
				else if(updateCatlogHandle.Status == AsyncOperationStatus.Succeeded)
				{
					Debug.Log("[HotUpdater] GetDownloadSizeAsync Started!");
					var downloadSize = Addressables.GetDownloadSizeAsync("default");
					yield return downloadSize;
					float size = (float)downloadSize.Result / 1024 / 1024;
					Debug.Log($"[HotUpdater] DownloadSize is {size}");
					if (updateCatlogHandle.Status == AsyncOperationStatus.Succeeded && downloadSize.Result > 0)
					{
						Debug.Log("[HotUpdater] Start downloading addressables.");
						yield return StartCoroutine(DownloadAddressables());
						yield break;
					}
					else if(updateCatlogHandle.Status == AsyncOperationStatus.Failed)
					{
						progressText = "Failed on downloading updates!";
						Debug.LogError("[HotUpdater] Failed on Addressables.GetDownloadSizeAsync!");
						yield break;
					}
				}
				Addressables.Release(updateCatlogHandle);
			}
			Addressables.Release(checkUpdateCatlogHandle);
		}
		else if(init.Status == AsyncOperationStatus.Failed)
		{
			progressText = "Failed on Addressables.InitializeAsync!";
			Debug.LogError("[HotUpdater] Failed on Addressables.InitializeAsync!");
			yield break;
		}
		Addressables.Release(init);

		StartCoroutine(EnterGame());
	}

	IEnumerator DownloadAddressables()
	{
		var downloadDependenciesHandle = Addressables.DownloadDependenciesAsync("default", false);
		while (downloadDependenciesHandle.Status == AsyncOperationStatus.None)
		{
			var progress = downloadDependenciesHandle.GetDownloadStatus().Percent;
			progressText = $"Downloading...{progress}";
			yield return null;
		}
		yield return downloadDependenciesHandle;
		if (downloadDependenciesHandle.Status == AsyncOperationStatus.Succeeded)
		{
			string time = string.Format("{0:yyyyMMddHHMMss}", DateTime.Now);
			PlayerPrefs.SetString("ResVersion", time);
			progressText = "LoadMetadataForAOTDLLs";
			StartCoroutine(EnterGame());
		}
		else
		{
			progressText = "Failed on downloading updates!";
			Debug.LogError("[HotUpdater] Failed on Addressables.DownloadDependenciesAsync!");
			yield break;
		}
	}

	/// <summary>
	/// Load hot update dlls.
	/// </summary>
	IEnumerator LoadHotfixDLLs()
	{
		// Can't load dlls in Editor because it will duplicate the reference to scripts and cause issues.
#if !UNITY_EDITOR
        var hotFixDllLabelHandle = Addressables.LoadAssetsAsync<TextAsset>("HotUpdateDLL", null);
        yield return hotFixDllLabelHandle;
        var hotFixDlls = hotFixDllLabelHandle.Result;
        Addressables.Release(hotFixDllLabelHandle);
        foreach (var hotFixDll in hotFixDlls) {
            Assembly.Load(hotFixDll.bytes);
        }
        Debug.Log("[HotUpdater] LoadHotfixDLLs complete!");
#endif
		yield return null;
	}
	/// <summary>
	/// Load AOT metadata dlls.
	/// </summary>
	/// <returns></returns>
	IEnumerator LoadMetadataForAOTDLLs()
	{
		// Can't load dlls in Editor because it will duplicate the reference to scripts and cause issues.
#if !UNITY_EDITOR
		HomologousImageMode mode = HomologousImageMode.SuperSet;
        var aotMetadateDllHandle = Addressables.LoadAssetsAsync<TextAsset>("AOTMetadataDLL", null);
        yield return aotMetadateDllHandle;
        var AOTMetadataDlls = aotMetadateDllHandle.Result;
        foreach (var AOTMetadataDll in AOTMetadataDlls)
        {
            LoadImageErrorCode err = RuntimeApi.LoadMetadataForAOTAssembly(AOTMetadataDll.bytes, mode);
            Debug.Log($"[HotUpdater] LoadMetadataForAOTAssembly:{AOTMetadataDll.name}. mode:{mode} ret:{err}");
        }
        Addressables.Release(aotMetadateDllHandle);
		Debug.Log("[HotUpdater] LoadMetadataForAOTDLLs complete!");
#endif
		yield return null;
	}

	/// <summary>
	/// Load hot update dlls and AOT metadata dlls here and enter the main menu scene or somewhere
	/// </summary>
	/// <returns></returns>
	IEnumerator EnterGame()
	{
		yield return LoadHotfixDLLs();
		yield return LoadMetadataForAOTDLLs();
	
		Addressables.LoadSceneAsync("MainMenu").Completed += (obj) =>
		{
			if (obj.Status == AsyncOperationStatus.Succeeded)
			{
				Debug.Log("[HotUpdater] Successfully load into MainMenu.");
			}
			else
			{
				Debug.LogError("[HotUpdater] Failed to load MainMenu scene!");
			}
		};
	}
}
