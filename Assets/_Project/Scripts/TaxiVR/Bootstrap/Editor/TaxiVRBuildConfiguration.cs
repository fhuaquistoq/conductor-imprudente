using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace TaxiVR.Bootstrap.Editor
{
    public static class TaxiVRBuildConfiguration
    {
        public const string ScenePath = "Assets/Main.unity";
        public const string InputPath = "Assets/_Project/Settings/Input/TaxiVR.inputactions";
        public const string OutputPath = "Builds/Playable/TaxiVR.exe";
        public const BuildTarget Target = BuildTarget.StandaloneWindows64;

        public static string[] Validate()
        {
            var errors = new System.Collections.Generic.List<string>();
            if (!File.Exists("Assets/_Project/Settings/Build/TaxiVR.Windows.build.json")) errors.Add("Windows build contract is missing.");
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            if (scenes.Length != 1 || scenes[0].path != ScenePath) errors.Add("Main must be the sole enabled scene.");
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            var map = actions == null ? null : actions.FindActionMap("TaxiVR", false);
            if (map == null) errors.Add("TaxiVR action map is missing.");
            else if (map.bindings.Any(binding => binding.path.Contains("Joystick", StringComparison.OrdinalIgnoreCase)
                || binding.path.Contains("Gamepad", StringComparison.OrdinalIgnoreCase)))
                errors.Add("TaxiVR action map contains a joystick/gamepad binding.");
            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (xr?.InitManagerOnStart != false)
                errors.Add("Standalone XR must not auto-initialize: PlayableRoot owns startup so -taxivr-desktop works.");
            if (xr?.Manager?.activeLoaders?.Any(loader => loader is OpenXRLoader) != true)
                errors.Add("Standalone OpenXR loader is not configured.");
            return errors.ToArray();
        }

        [MenuItem("TaxiVR/Configure Windows Bootstrap")]
        public static void ConfigureProject()
        {
            TaxiVR.Playable.Editor.PlayableBuilder.Configure();
        }

        [MenuItem("TaxiVR/Build Windows Development")]
        public static void BuildDevelopment()
        {
            TaxiVR.Playable.Editor.PlayableBuilder.Build();
        }
    }
}
