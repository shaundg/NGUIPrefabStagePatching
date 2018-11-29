// Uncomment this if TextMeshProp support as part of your NGUI prefab is required
#define TEXTMESHPRO_SUPPORT

using System;
using System.Reflection;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Experimental.SceneManagement;

using Harmony;

[InitializeOnLoad]
static class NGUIPrefabStageHelper
{
	private static bool log = false;
	private static bool initialized = false;

	static NGUIPrefabStageHelper()
	{
		PrefabStage.prefabStageOpened += OnPrefabStageOpened;
		PrefabStage.prefabStageClosing += OnPrefabStageClosed;
		EditorApplication.update += Update;

		// This is necessary to trigger the patching of Unity executable
		// Check this article for details: https://github.com/pardeike/Harmony/wiki/Bootstrapping
		var harmony = HarmonyInstance.Create("REPLACE_THIS");
		harmony.PatchAll(Assembly.GetExecutingAssembly());
	}

	// This is a patch for PrefabStageUtility.cs functions: https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/SceneManagement/StageManager/PrefabStage/PrefabStageUtility.cs
	// which are called in the PrefabStage.cs file for initializing a stage https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/SceneManagement/StageManager/PrefabStage/PrefabStage.cs

	[HarmonyPatch(typeof(PrefabStageUtility))]
	[HarmonyPatch("LoadPrefabIntoPreviewScene")]
	public class PatchSceneRoot
	{
		static void Postfix(GameObject __result, string prefabAssetPath, Scene previewScene)
		{
			// Handling inconvenient var names
			var instanceRoot = __result;
			var scene = previewScene;

			if (instanceRoot == null || scene == null)
			{
				return;
			}

			var stageHandleMain = StageUtility.GetMainStageHandle();
			var rootsInPrefab = instanceRoot.GetComponentsInChildren<UIRoot>(true);
			var panelsInPrefab = instanceRoot.GetComponentsInChildren<UIPanel>(true);

			bool missingRoot = rootsInPrefab.Length == 0;
			bool missingPanel = panelsInPrefab.Length == 0;

			// If nothing is missing, there is no reason to continue
			if (!missingRoot && !missingPanel)
			{
				return;
			}

			GameObject container = EditorUtility.CreateGameObjectWithHideFlags("UIRoot (Environment)", HideFlags.DontSave);
			container.layer = LayerMask.NameToLayer("UI");

			if (missingRoot)
			{
				// To maintain consistent world space scale of UI elements, it might be worth looking for existing root in main stage
				// If you don't need non-default root settings, it's perfectly fine to just leave a single AddComponent<UIRoot> call here

				var rootsInMainStage = new List<UIRoot>();
				for (int s = 0; s < SceneManager.sceneCount; s++)
				{
					var sceneFromList = SceneManager.GetSceneAt(s);
					if (!sceneFromList.isLoaded || sceneFromList == scene)
					{
						continue;
					}

					var sceneStageHandle = StageUtility.GetStageHandle(sceneFromList);
					if (sceneStageHandle != stageHandleMain)
					{
						continue;
					}

					var sceneRootObjects = sceneFromList.GetRootGameObjects();
					for (int j = 0; j < sceneRootObjects.Length; j++)
					{
						var go = sceneRootObjects[j];
						var rootsInChildren = go.GetComponentsInChildren<UIRoot>(true);
						if (rootsInChildren.Length > 0)
						{
							rootsInMainStage.AddRange(rootsInChildren);
						}
					}
				}

				var rootInContainer = container.AddComponent<UIRoot>();
				if (rootsInMainStage.Count > 0)
				{
					var rootInMainStage = rootsInMainStage[0];
					rootInContainer.scalingStyle = rootInMainStage.scalingStyle;
					rootInContainer.manualWidth = rootInMainStage.manualWidth;
					rootInContainer.manualHeight = rootInMainStage.manualHeight;
					rootInContainer.minimumHeight = rootInMainStage.minimumHeight;
					rootInContainer.maximumHeight = rootInMainStage.maximumHeight;
					rootInContainer.adjustByDPI = rootInMainStage.adjustByDPI;
					rootInContainer.fitWidth = rootInMainStage.fitWidth;
					rootInContainer.fitHeight = rootInMainStage.fitHeight;
					rootInContainer.shrinkPortraitUI = rootInMainStage.shrinkPortraitUI;
				}
				else
				{
					rootInContainer.scalingStyle = UIRoot.Scaling.Flexible;
					rootInContainer.manualWidth = 1920;
					rootInContainer.manualHeight = 1080;
					rootInContainer.minimumHeight = 1920;
					rootInContainer.maximumHeight = 1080;
					rootInContainer.adjustByDPI = false;
					rootInContainer.fitWidth = false;
					rootInContainer.fitHeight = true;
					rootInContainer.shrinkPortraitUI = false;
				}
			}

			if (missingPanel)
			{
				// Default values of a panel are perfectly fine, so we are not going to go through the trouble of finding an existing one
				var panelInContainer = container.AddComponent<UIPanel>();
			}

			SceneManager.MoveGameObjectToScene(container, scene);
			instanceRoot.transform.SetParent(container.transform, false);

#if TEXTMESHPRO_SUPPORT
			Canvas canvas = container.AddComponent<Canvas> ();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
#endif
		}
	}

	// Patched method:
	// static public UIDrawCall Create (UIPanel panel, Material mat, Texture tex, Shader shader)

	[HarmonyPatch(typeof(UIDrawCall))]
	[HarmonyPatch("Create")]
	[HarmonyPatch(new Type[] { typeof(UIPanel), typeof(Material), typeof(Texture), typeof(Shader) })]
	public class PatchUIDrawCall
	{
		static void Postfix(UIPanel panel, Material mat, Texture tex, Shader shader, UIDrawCall __result)
		{
			var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
			if (prefabStage != null)
			{
				if (__result.manager != null)
				{
					var stage = StageUtility.GetStageHandle(__result.manager.gameObject);
					if (stage == prefabStage.stageHandle)
					{
						SceneManager.MoveGameObjectToScene(__result.gameObject, prefabStage.scene);
						if (log)
							Debug.Log(string.Format("Intercepted draw call creation for a panel in prefab stage ({0}), moving it from main stage",
							__result.manager.gameObject.name), __result.manager.gameObject);
					}
				}
			}
		}
	}





	private static void OnPrefabStageOpened(PrefabStage prefabStage)
	{
		if (log)
		{
			Debug.LogWarning(string.Format("Prefab stage opened, checking NGUI objects | Cameras: {0} | Panels: {1} | Drawcalls: {2}/{3}",
				UICamera.list.size, UIPanel.list.Count, UIDrawCall.activeList.size, UIDrawCall.inactiveList.size));
		}
		CheckNGUIObjects();
	}

	private static void OnPrefabStageClosed(PrefabStage prefabStage)
	{
		if (log)
		{
			Debug.LogWarning(string.Format("Prefab stage closed, checking NGUI objects | Cameras: {0} | Panels: {1} | Drawcalls: {2}/{3}",
				UICamera.list.size, UIPanel.list.Count, UIDrawCall.activeList.size, UIDrawCall.inactiveList.size));
		}
		CheckNGUIObjects();

		// Since no events happened from standpoint of main stage objects, we have force them to update
		var stageHandleMain = StageUtility.GetMainStageHandle();
		for (int s = 0; s < SceneManager.sceneCount; s++)
		{
			var sceneFromList = SceneManager.GetSceneAt(s);
			if (!sceneFromList.isLoaded)
			{
				continue;
			}

			var stageHandleFromList = StageUtility.GetStageHandle(sceneFromList);
			if (stageHandleFromList != stageHandleMain)
			{
				continue;
			}

			var sceneRootObjects = sceneFromList.GetRootGameObjects();
			for (int i = 0; i < sceneRootObjects.Length; i++)
			{
				FindAndRefreshPanels(sceneRootObjects[i].transform);
			}
		}
	}

	private static void FindAndRefreshPanels(Transform t)
	{
		var panel = t.GetComponent<UIPanel>();
		if (panel != null && panel.gameObject.activeInHierarchy)
		{
			NGUITools.ImmediatelyCreateDrawCalls(panel.gameObject);
		}
		else
		{
			// This is wrapped in an else block because there is no need to go deeper if a panel was found
			for (int i = 0, count = t.childCount; i < count; ++i)
			{
				FindAndRefreshPanels(t.GetChild(i));
			}
		}
	}

	// Since edit mode doesn't have a reliable early start event, we're using an Update here
	// This event mostly exists to handle assembly reloads landing within the prefab stage - 
	// after all, assembly reload in the main stage works correctly, as always

	private static void Update()
	{
		if (!initialized)
		{
			// No point using this event twice, all subsequent jumps through stages would 
			// be caught by the other two events we have subscribed to
			EditorApplication.update -= Update;
			initialized = true;

			// If prefab stage is null, we don't have to do anything here - assembly reloads
			// happening on the main stage are the old default case already working correctly
			var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
			if (prefabStage == null)
			{
				return;
			}

			if (log)
			{
				Debug.LogWarning(string.Format("Assembly reloaded in prefab stage, checking NGUI objects | Cameras: {0} | Panels: {1} | Drawcalls: {2}/{3}",
					UICamera.list.size, UIPanel.list.Count, UIDrawCall.activeList.size, UIDrawCall.inactiveList.size));
			}
			CheckNGUIObjects();
		}
	}

	private static void CheckNGUIObjects()
	{
		// var root = prefabStage.prefabContentsRoot;
		// var scene = prefabStage.scene;

		var stageHandleMain = StageUtility.GetMainStageHandle();
		var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

		for (int i = UICamera.list.size - 1; i >= 0; --i)
		{
			UICamera cam = UICamera.list[i];
			if (cam == null || !cam.gameObject.BelongsToCurrentStage(prefabStage, stageHandleMain))
			{
				UICamera.list.RemoveAt(i);
				if (log)
				{
					Debug.Log(string.Format("Removing {0} entry {1} from the camera list", cam == null ? "a null" : "an out of stage", i));
				}
			}
		}

		for (int i = UIPanel.list.Count - 1; i >= 0; --i)
		{
			UIPanel panel = UIPanel.list[i];
			if (panel == null || !panel.gameObject.BelongsToCurrentStage(prefabStage, stageHandleMain))
			{
				UIPanel.list.RemoveAt(i);
				if (log)
				{
					Debug.Log(string.Format("Removing {0} entry {1} from the panel list", panel == null ? "a null" : "an out of stage", i));
				}
			}
		}

		TrimDrawCalls(prefabStage, stageHandleMain, UIDrawCall.activeList, "active");
		TrimDrawCalls(prefabStage, stageHandleMain, UIDrawCall.inactiveList, "inactive");

		if (log)
		{
			Debug.LogWarning(string.Format("Recheck finished | Cameras: {0} | Panels: {1} | Drawcalls: {2}/{3}",
				UICamera.list.size, UIPanel.list.Count, UIDrawCall.activeList.size, UIDrawCall.inactiveList.size));
		}

		// Draw calls are not automatically restored on exit from prefab stage otherwise
		for (int i = 0; i < UIPanel.list.Count; ++i)
		{
			UIPanel.list[i].RebuildAllDrawCalls();
		}
	}

	private static void TrimDrawCalls(PrefabStage prefabStage, StageHandle stageHandleMain, BetterList<UIDrawCall> list, string identifier)
	{
		for (int i = list.size - 1; i >= 0; --i)
		{
			UIDrawCall dc = list[i];
			if (dc == null || !dc.gameObject.BelongsToCurrentStage(prefabStage, stageHandleMain))
			{
				list.RemoveAt(i);
				if (dc != null)
				{
					UIDrawCall.Destroy(dc);
				}

				if (log)
				{
					Debug.Log(string.Format("Removing {0} entry {1} from the {2} draw call list", dc == null ? "a null" : "an out of stage", i, identifier));
				}
			}
		}
	}

	private static bool BelongsToCurrentStage(this GameObject go, PrefabStage prefabStage, StageHandle stageHandleMain)
	{
		var stageHandleFromObject = StageUtility.GetStageHandle(go);
		bool result = prefabStage != null ? (stageHandleFromObject == prefabStage.stageHandle) : (stageHandleFromObject == stageHandleMain);
		return result;
	}
}
