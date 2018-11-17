using Harmony;
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
class EditorPrefabStageManagerBootStrapper
{
	static EditorPrefabStageManagerBootStrapper()
	{
		var harmony = HarmonyInstance.Create("INIT_THIS_THING_FIRST_SEE_URL"); //https://github.com/pardeike/Harmony/wiki/Bootstrapping
		harmony.PatchAll(Assembly.GetExecutingAssembly());
		Debug.Log("Patching " + Assembly.GetExecutingAssembly().FullName);

		PrefabStage.prefabStageOpened += new Action<PrefabStage>(PrefabStageOpened);
		PrefabStage.prefabSaving += new Action<GameObject>(PrefabSaving);
		PrefabStage.prefabSaved += new Action<GameObject>(PrefabSaved);
		PrefabStage.prefabStageClosing += new Action<PrefabStage>(PrefabStageClosed);
	}

	static void PrefabStageOpened(PrefabStage prefabStage)
	{
		Debug.Log("Opening Prefab Stage Scene" + prefabStage.scene.name);

		var root = prefabStage.prefabContentsRoot;
		var scene = prefabStage.scene;
		var mainStage = StageUtility.GetMainStageHandle();

		//Not sure if this is needed anymore
		for (int i = UICamera.list.size - 1; i >= 0; --i)
		{
			UICamera cam = UICamera.list[i];
			if (cam == null)
			{
				UICamera.list.RemoveAt(i);
			}

			var camStage = StageUtility.GetStageHandle(cam.gameObject);
			if (camStage == mainStage)
			{
				UICamera.list.RemoveAt(i);
			}
		}
	}

	static void PrefabStageClosed(PrefabStage prefabStage)
	{
		Debug.Log("Closing Prefab Stage Scene" + prefabStage.scene.name);
	}

	static void PrefabSaving(GameObject go)
	{
		Debug.Log("Saving Prefab: " + go.name);
	}

	static void PrefabSaved(GameObject go)
	{
		Debug.Log("Saved Prefab: " + go.name);
	}
}


/// <summary>
/// This overrides PrefabStageUtility.cs functions
/// https://github.com/Unity-Technologies/UnityCsReference/blob/31c7ca85bcc43ef4258c2a216b195ad1d6e59948/Editor/Mono/SceneManagement/StageManager/PrefabStage/PrefabStageUtility.cs
/// which are called in the PrefabStage.cs file for initializing a stage
/// https://github.com/Unity-Technologies/UnityCsReference/blob/2d9e6431c06e628577d972e063ba0998cfa338cb/Editor/Mono/SceneManagement/StageManager/PrefabStage/PrefabStage.cs
/// </summary>
[HarmonyPatch(typeof(PrefabStageUtility))]
[HarmonyPatch("HandleUIReparenting")]
public class PatchSceneRoot
{
	static void Postfix(GameObject instanceRoot, Scene scene)
	{
		var originalRoot = GameObject.FindObjectOfType<UIRoot>();
		Debug.Assert(originalRoot);

		if(instanceRoot != null)
		{
			if(instanceRoot.GetComponent<UIRect>())
			{
				GameObject root = EditorUtility.CreateGameObjectWithHideFlags("UIRoot (Environment)", HideFlags.DontSave);
				var uiRoot = root.AddComponent<UIRoot>();

				//copy existing UIRoot settings over to the scene component
				uiRoot.scalingStyle = originalRoot.scalingStyle;
				uiRoot.manualWidth = originalRoot.manualWidth;
				uiRoot.manualHeight = originalRoot.manualHeight;
				uiRoot.minimumHeight = originalRoot.minimumHeight;
				uiRoot.maximumHeight = originalRoot.maximumHeight;
				uiRoot.adjustByDPI = originalRoot.adjustByDPI;
				uiRoot.fitWidth = originalRoot.fitWidth;
				uiRoot.fitHeight = originalRoot.fitHeight;
				uiRoot.shrinkPortraitUI = originalRoot.shrinkPortraitUI;

				//this is for TextMeshPro rendering (seems to work)
				root.layer = LayerMask.NameToLayer("UI");
				Canvas canvas = root.AddComponent<Canvas>();
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				SceneManager.MoveGameObjectToScene(root, scene);

				instanceRoot.transform.SetParent(root.transform, false);
			}
		}
	}
}

[HarmonyPatch(typeof(UIDrawCall))]
[HarmonyPatch("Create")]
[HarmonyPatch(new Type[] { typeof(string) })]
public class PatchUIDrawCall
{
	static void Postfix(string name, UIDrawCall __result)
	{
		var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
		if (prefabStage != null)
		{
			var prefabScene = prefabStage.scene;
			SceneManager.MoveGameObjectToScene(__result.gameObject, prefabScene);
		}
	}
}
