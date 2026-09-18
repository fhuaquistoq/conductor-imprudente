using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaxiVR.Bootstrap.Editor
{
    /// <summary>Compila el APK independiente de Meta Quest a partir del Build Profile "Meta Quest".
    /// En batch mode el perfil tiene que entrar por linea de comandos (<c>-activeBuildProfile</c>): Unity no
    /// puede cambiar de plataforma mientras ejecuta <c>-executeMethod</c>, y sin ese argumento compilaria el
    /// perfil de Windows a un archivo con extension .apk. El perfil aporta ademas los scripting defines de
    /// Android (ENABLE_RUNTIME_OPTIMIZER, OVR_DISABLE_HAND_PINCH_BUTTON_MAPPING, USE_INPUT_SYSTEM_POSE_CONTROL,
    /// USE_STICK_CONTROL_THUMBSTICKS), que no estan en los ajustes globales.</summary>
    public static class QuestApkBuilder
    {
        public const string ProfilePath = "Assets/Settings/Build Profiles/Meta Quest.asset";
        public const string ApkPath = "Builds/Quest/TaxiVR.apk";
        public const string ResultPath = "Logs/TaxiQuestBuildResult.txt";
        const string AndroidPackage = "com.unsa.eltaxistaimprudente";
        const string ScenePath = TaxiVR.Playable.Editor.PlayableBuilder.Scene;

        [MenuItem("TaxiVR/Build Quest APK")]
        public static void BuildFromMenu()
        {
            Build();
        }

        /// <summary>Punto de entrada de <c>-executeMethod</c>. Lanza una excepcion si la compilacion falla,
        /// para que Unity salga con codigo 1.</summary>
        public static void Build()
        {
            var profile = RequireProfile();
            RequireContract(profile);
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));

            var report = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
            {
                buildProfile = profile,
                locationPathName = ApkPath,
                options = BuildOptions.None,
            });

            var summary = report.summary;
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ResultPath, summary.result + "\nEscena: " + ScenePath + "\nPerfil: " + ProfilePath +
                "\nBytes: " + summary.totalSize + "\nErrors: " + summary.totalErrors +
                "\nWarnings: " + summary.totalWarnings + "\nApk: " + ApkPath);

            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("La compilacion del APK fallo (" + summary.result + "). Detalle en " + ResultPath + ".");
            if (!File.Exists(ApkPath))
                throw new InvalidOperationException("Unity informo exito pero no hay APK en " + ApkPath + ".");
        }

        static BuildProfile RequireProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath);
            if (profile == null) throw new InvalidOperationException("Falta el Build Profile '" + ProfilePath + "'.");
            if (BuildProfile.GetActiveBuildProfile() == profile) return profile;
            if (Application.isBatchMode)
                throw new InvalidOperationException("El perfil activo no es '" + ProfilePath +
                    "'. En batch mode anade -activeBuildProfile \"" + ProfilePath + "\".");
            // En el Editor el cambio de plataforma se difiere al siguiente update, asi que no se puede
            // compilar en la misma llamada: se activa y se pide repetir el comando.
            BuildProfile.SetActiveBuildProfile(profile);
            throw new InvalidOperationException("Se ha activado el perfil '" + profile.name +
                "'. Vuelve a ejecutar el comando para compilar el APK.");
        }

        static void RequireContract(BuildProfile profile)
        {
            var errors = new List<string>();
            var scenes = profile.GetScenesForBuild().Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length != 1 || scenes[0] != ScenePath)
                errors.Add("El perfil debe compilar una sola escena: " + ScenePath + ".");
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
                errors.Add("Android exige IL2CPP.");
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                errors.Add("Meta Quest solo admite ARM64.");
            if (PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) != AndroidPackage)
                errors.Add("El identificador Android debe ser " + AndroidPackage + ".");
            if (PlayerSettings.Android.minSdkVersion != AndroidSdkVersions.AndroidApiLevel32)
                errors.Add("minSdk debe ser 32.");
            if (PlayerSettings.Android.targetSdkVersion != AndroidSdkVersions.AndroidApiLevel34)
                errors.Add("targetSdk debe ser 34.");
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (apis.Length != 1 || apis[0] != GraphicsDeviceType.Vulkan)
                errors.Add("Meta Quest exige Vulkan como unica API grafica.");
            if (errors.Count > 0)
                throw new InvalidOperationException("Contrato de compilacion Android roto:\n - " + string.Join("\n - ", errors));
        }
    }
}
