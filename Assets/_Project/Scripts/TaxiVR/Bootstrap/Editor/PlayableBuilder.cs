using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.Hands.OpenXR;

namespace TaxiVR.Playable.Editor
{
    public static class PlayableBuilder
    {
        public const string Scene = "Assets/_Project/Scenes/TaxiVR_Playable.unity";
        public const string BuildPath = "Builds/Playable/TaxiVR.exe";
        const string DataFolder = "Assets/_Project/PlayableData";
        [MenuItem("TaxiVR/Playable/1 - Create or update playable scene")]
        public static void Configure()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building the scene.");
            Directory.CreateDirectory(DataFolder);
            var assets = AssetDatabase.LoadAssetAtPath<CityAssets>(DataFolder + "/CityAssets.asset");
            if (assets == null) { assets = ScriptableObject.CreateInstance<CityAssets>(); AssetDatabase.CreateAsset(assets, DataFolder + "/CityAssets.asset"); }
            assets.Buildings = new[] { "Building_Small_1", "Building_Medium_2_001", "Building_Large_2" }.Select(n => Load("Assets/ThirdParty/DowntownCity/Models/" + n + ".fbx")).ToArray();
            assets.Cars = new[] { "NormalCar1", "NormalCar2", "SUV" }.Select(n => Load("Assets/ThirdParty/Vehicles/Models/" + n + ".fbx")).ToArray();
            assets.Taxi = Load("Assets/ThirdParty/Vehicles/Models/Taxi_Full.fbx");
            foreach (var path in new[] { "Assets/ThirdParty/Vehicles/Models/Taxi_Full.fbx", "Assets/ThirdParty/Vehicles/Models/NormalCar1.fbx", "Assets/ThirdParty/Vehicles/Models/NormalCar2.fbx", "Assets/ThirdParty/Vehicles/Models/SUV.fbx", "Assets/ThirdParty/DowntownCity/Models/Building_Small_1.fbx", "Assets/ThirdParty/DowntownCity/Models/Building_Medium_2_001.fbx", "Assets/ThirdParty/DowntownCity/Models/Building_Large_2.fbx", "Assets/ThirdParty/UniversalCharacters/Models/Superhero_Male_FullBody.fbx" }) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            assets.Asphalt = Material("Asfalto", new Color(.15f, .18f, .20f));
            assets.Pavement = Material("Vereda", new Color(.56f, .56f, .51f));
            assets.Dark = Material("Grafito", new Color(.045f, .06f, .07f));
            assets.Yellow = Material("Amarillo", new Color(1, .69f, .12f));
            assets.White = Material("Blanco", new Color(.89f, .91f, .85f));
            assets.Glass = Material("Cristal", new Color(.14f, .32f, .4f));
            assets.Grass = Material("Cesped", new Color(.24f, .35f, .24f));
            assets.Foliage = Material("Hojas", new Color(.19f, .37f, .25f));
            assets.Skin = Material("Manos", new Color(.65f, .39f, .25f));
            assets.Red = Material("Rojo", new Color(.65f, .18f, .13f));
            assets.Blue = Material("Azul", new Color(.045f, .39f, .61f));
            assets.Facades = new[] { Material("Salvia", new Color(.42f,.58f,.5f)), Material("Arena", new Color(.79f,.55f,.3f)), Material("Terracota", new Color(.65f,.29f,.22f)), Material("Marino", new Color(.16f,.29f,.43f)) };
            assets.DistantBuilding = Material("Edificios lejanos", Color.white);
            var facade = AssetDatabase.LoadAssetAtPath<Texture2D>(DataFolder + "/DistantFacade.asset");
            if (facade == null)
            {
                facade = new Texture2D(64,128,TextureFormat.RGBA32,true) { name = "DistantFacade", filterMode = FilterMode.Bilinear };
                for (int y = 0; y < 128; y++) for (int x = 0; x < 64; x++)
                {
                    bool window = x % 16 >= 4 && x % 16 < 12 && y % 16 >= 4 && y % 16 < 12;
                    facade.SetPixel(x,y,window ? new Color(.27f,.36f,.4f) : new Color(.52f,.44f,.36f));
                }
                facade.Apply(true); AssetDatabase.CreateAsset(facade, DataFolder + "/DistantFacade.asset");
            }
            assets.DistantBuilding.mainTexture = facade; EditorUtility.SetDirty(assets.DistantBuilding);
            assets.UnlitShader = Shader.Find("Standard");
            assets.Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EditorUtility.SetDirty(assets);
            // Keep the earlier bootstrap scene intact. This scene owns the first playable version.
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Taxi VR - playable composition").AddComponent<PlayableRoot>(); root.Assets = assets;
            var sun = new GameObject("Luz de tarde").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.4f;
            sun.color = new Color(1, .92f, .78f); sun.transform.rotation = Quaternion.Euler(42, -35, 0); sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.57f,.66f,.73f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = new Color(.62f,.73f,.78f);
            RenderSettings.fogStartDistance = 105; RenderSettings.fogEndDistance = 175;
            QualitySettings.shadowDistance = 65; QualitySettings.shadowResolution = ShadowResolution.Medium;
            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (xr == null || xr.Manager == null) throw new InvalidOperationException("Existing Windows XR settings not found.");
            // Explicit startup lets -taxivr-desktop run without starting the Link/Simulator runtime.
            xr.InitManagerOnStart = false; EditorUtility.SetDirty(xr);
            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            foreach (var feature in openxr.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRFeature>())
            {
                if (feature is HandTracking || feature is OculusTouchControllerProfile || feature.GetType().Name == "MetaQuestTouchPlusControllerProfile")
                { feature.enabled = true; EditorUtility.SetDirty(feature); }
                if (feature.GetType().Name == "OculusTouchControllerProximityProfile") { feature.enabled = false; EditorUtility.SetDirty(feature); }
            }
            EditorUtility.SetDirty(openxr);
            PlayerSettings.companyName = "TaxiVR"; PlayerSettings.productName = "Taxi VR";
            PlayerSettings.defaultIsNativeResolution = false; PlayerSettings.defaultScreenWidth = 1440; PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.runInBackground = true;
            EditorSceneManager.SaveScene(scene, Scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            AssetDatabase.SaveAssets();
            Verify();
        }
        static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new InvalidOperationException("Missing model " + path);
        static Material Material(string name, Color color)
        {
            string path = DataFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
            else if (material.shader != Shader.Find("Standard")) material.shader = Shader.Find("Standard");
            material.color = color; material.SetFloat("_Smoothness", .22f); material.enableInstancing = true; EditorUtility.SetDirty(material); return material;
        }
        [MenuItem("TaxiVR/Playable/2 - Verify configuration")]
        public static void Verify()
        {
            void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
            Require(File.Exists(Scene), "Playable scene is missing");
            Require(CityMath.Sector(-.1f) == -1 && CityMath.Sector(64) == 1, "Sector boundaries");
            for (float t = 0; t < 56; t += .1f) Require(!(CityMath.SignalGreen(true,t) && CityMath.SignalGreen(false,t)), "Conflicting green lights");
            Require(Mathf.Abs(CityMath.WheelDelta(179,-179)-2) < .01f, "Wheel angle seam");
            var path = CityMath.Route(new Vector2Int(-2,4),new Vector2Int(3,-1)); Require(path.Count == 11 && path.Last() == new Vector2Int(3,-1), "A* route");
            var assets = AssetDatabase.LoadAssetAtPath<CityAssets>(DataFolder + "/CityAssets.asset");
            Require(assets != null && assets.Taxi != null && assets.Buildings.All(x=>x!=null) && assets.Cars.All(x=>x!=null), "Model references");
            Require(assets.UnlitShader != null && assets.Font != null, "UI shader and font");
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Require(xr.GetFeature<HandTracking>()?.enabled == true, "OpenXR Hand Tracking feature");
            Require(xr.GetFeature<OculusTouchControllerProfile>()?.enabled == true, "Touch controller profile");
            File.WriteAllText("Logs/TaxiConfigurationChecks.txt", "PASS sector boundaries\nPASS mutually exclusive traffic signals\nPASS wheel seam\nPASS A* connected shortest path\nPASS imported models\nPASS UI resources\nPASS OpenXR hands and Touch profiles\n");
        }
        [MenuItem("TaxiVR/Playable/3 - Build Windows playable")]
        public static void Build()
        {
            Configure();
            Verify(); Directory.CreateDirectory(Path.GetDirectoryName(BuildPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { Scene }, target = BuildTarget.StandaloneWindows64, locationPathName = BuildPath, options = BuildOptions.Development });
            File.WriteAllText("Logs/TaxiBuildResult.txt", report.summary.result + "\nBytes: " + report.summary.totalSize + "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings);
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Windows build failed: " + report.summary.result);
            File.WriteAllText("Builds/Playable/Jugar - Escritorio.bat", "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"TaxiVR.exe\" -taxivr-desktop -screen-fullscreen 0\r\n");
            File.WriteAllText("Builds/Playable/Jugar - VR.bat", "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"TaxiVR.exe\" -screen-fullscreen 0\r\n");
        }
    }
}
