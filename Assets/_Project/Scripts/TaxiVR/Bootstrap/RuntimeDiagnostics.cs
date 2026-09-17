using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace TaxiVR.Bootstrap
{
    [Serializable]
    public sealed class DiagnosticSnapshot
    {
        public string BuildId;
        public string UnityVersion;
        public string Scene;
        public string XrLoader;
        public string OpenXrRuntime;
        public string Mode;
        public bool TaxiMapEnabled;
        public bool ReferencesValid;
        public string Recovery;
    }

    public sealed class RuntimeDiagnostics
    {
        public const string RecoveryText = "Open Main, assign the TaxiVR input asset, and restore the seated OVRCameraRig, visible bootstrap geometry, and Windows OpenXR loader.";

        public static DiagnosticSnapshot Create(string buildId, string unityVersion, string scene,
            string loader, string runtime, bool mapEnabled, bool referencesValid, bool development)
        {
            return new DiagnosticSnapshot {
                BuildId = buildId, UnityVersion = unityVersion, Scene = scene,
                XrLoader = loader, OpenXrRuntime = runtime,
                Mode = development ? "development" : "release",
                TaxiMapEnabled = mapEnabled, ReferencesValid = referencesValid,
                Recovery = referencesValid ? string.Empty : RecoveryText
            };
        }

        public DiagnosticSnapshot Capture(bool mapEnabled, bool referencesValid)
        {
            var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
            return Create(Application.buildGUID, Application.unityVersion,
                SceneManager.GetActiveScene().name, loader == null ? "none" : loader.name,
                OpenXRRuntime.name ?? "unavailable", mapEnabled, referencesValid, Debug.isDebugBuild);
        }

        public string Write(DiagnosticSnapshot snapshot)
        {
            var folder = Path.Combine(Application.persistentDataPath, "Diagnostics");
            Directory.CreateDirectory(folder);
            var json = JsonUtility.ToJson(snapshot);
            File.AppendAllText(Path.Combine(folder, "taxivr.jsonl"), json + Environment.NewLine);
            var textPath = Path.Combine(folder, "taxivr-latest.txt");
            File.WriteAllText(textPath, json);
            return textPath;
        }
    }
}
