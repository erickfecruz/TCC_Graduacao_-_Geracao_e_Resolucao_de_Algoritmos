using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using MazeDOTS;

namespace MazeDOTS.EditorTools
{
    /// <summary>
    /// Headless project configuration: creates the URP render pipeline asset (required by Entities
    /// Graphics), builds the runnable Maze scene from scratch, and registers it in the build settings.
    /// Designed to be invoked from the command line:
    ///   Unity -batchmode -quit -projectPath . -executeMethod MazeDOTS.EditorTools.MazeProjectSetup.ConfigureProject
    /// URP creation is done via reflection so this assembly compiles even if the URP API shifts.
    /// </summary>
    public static class MazeProjectSetup
    {
        const string SettingsDir = "Assets/Settings";
        const string ScenesDir = "Assets/Scenes";
        const string ScenePath = "Assets/Scenes/Maze.unity";

        [MenuItem("Maze/Configure Project")]
        public static void ConfigureProject()
        {
            try
            {
                ConfigureUrp();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MazeSetup] URP configuration skipped: " + e.Message);
            }

            BuildScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MazeSetup] Configuration complete.");
        }

        static void ConfigureUrp()
        {
            if (!Directory.Exists(SettingsDir)) Directory.CreateDirectory(SettingsDir);

            var asm = "Unity.RenderPipelines.Universal.Runtime";
            var urpAssetType = Type.GetType($"UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, {asm}");
            var rendererDataType = Type.GetType($"UnityEngine.Rendering.Universal.UniversalRendererData, {asm}");
            if (urpAssetType == null || rendererDataType == null)
            {
                Debug.LogWarning("[MazeSetup] URP types not found; is the URP package installed?");
                return;
            }

            string rendererPath = SettingsDir + "/MazeUniversalRenderer.asset";
            var rendererData = AssetDatabase.LoadAssetAtPath(rendererPath, rendererDataType) as ScriptableObject;
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance(rendererDataType);
                AssetDatabase.CreateAsset(rendererData, rendererPath);
            }

            string urpPath = SettingsDir + "/MazeURP.asset";
            var urpAsset = AssetDatabase.LoadAssetAtPath(urpPath, urpAssetType) as RenderPipelineAsset;
            if (urpAsset == null)
            {
                var create = urpAssetType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
                if (create == null)
                {
                    Debug.LogWarning("[MazeSetup] UniversalRenderPipelineAsset.Create not found.");
                    return;
                }
                var pars = create.GetParameters();
                object result = pars.Length == 1
                    ? create.Invoke(null, new object[] { rendererData })
                    : create.Invoke(null, null);
                urpAsset = result as RenderPipelineAsset;
                if (urpAsset == null) return;
                AssetDatabase.CreateAsset(urpAsset, urpPath);
            }

            GraphicsSettings.defaultRenderPipeline = urpAsset;
            QualitySettings.renderPipeline = urpAsset;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urpAsset;
            }
            Debug.Log("[MazeSetup] URP asset assigned.");
        }

        static void BuildScene()
        {
            if (!Directory.Exists(ScenesDir)) Directory.CreateDirectory(ScenesDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera: top-down orthographic, matching the original project's view.
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 16f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.04f);
            camGo.transform.position = new Vector3(0f, 50f, 0f);
            camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // Directional light.
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            // EventSystem so the runtime uGUI is interactive.
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            // Bootstrap.
            var bootGo = new GameObject("MazeBootstrap");
            bootGo.AddComponent<MazeBootstrap>();

            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[MazeSetup] Scene built at " + ScenePath);
        }
    }
}
