using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using TaxiVR.Bootstrap;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Management;
using Object = UnityEngine.Object;

namespace TaxiVR.Tests.PlayMode
{
    [PrebuildSetup(typeof(HeadlessXrTestSetup))]
    public sealed class BootstrapPlayModeTests
    {
        [UnityTest]
        public IEnumerator MainEnablesTaxiMapAndProducesValidDiagnostics()
        {
            yield return SceneManager.LoadSceneAsync("Main");
            yield return null;
            var root = Object.FindAnyObjectByType<TaxiVRCompositionRoot>();
            Assert.That(root, Is.Not.Null);
            Assert.That(root.Snapshot.Scene, Is.EqualTo("Main"));
            Assert.That(root.Snapshot.TaxiMapEnabled, Is.True);
            Assert.That(root.Snapshot.ReferencesValid, Is.True);
                var rig = GameObject.Find("OVRCameraRig");
                Assert.That(rig, Is.Not.Null);
                Assert.That(rig.transform.Find("TrackingSpace/CenterEyeAnchor"), Is.Not.Null);
                Assert.That(GameObject.Find("Bootstrap Geometry")?.GetComponent<Renderer>()?.enabled, Is.True);
                Assert.That(GameObject.Find("Bootstrap Light")?.GetComponent<Light>()?.enabled, Is.True);
                Assert.That(Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude)
                    .Count(camera => camera.enabled), Is.EqualTo(1));
                Assert.That(Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude)
                    .Count(listener => listener.enabled), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator BatchPlayModeUsesTheNonXrHarnessSeam()
        {
#if UNITY_EDITOR
            if (Application.isBatchMode)
            {
                Assert.That(XRGeneralSettings.Instance, Is.Not.Null);
                Assert.That(XRGeneralSettings.Instance.InitManagerOnStart, Is.False);
            }
#endif
            yield return null;
        }
    }

    public sealed class HeadlessXrTestSetup : IPrebuildSetup, IPostBuildCleanup
    {
#if UNITY_EDITOR
        static bool previousInitManagerOnStart;
        static bool changed;
#endif

        public void Setup()
        {
#if UNITY_EDITOR
            if (!Application.isBatchMode || !Array.Exists(Environment.GetCommandLineArgs(), argument => argument == "-runTests"))
                return;

            var settings = XRGeneralSettings.Instance;
            if (settings == null)
                return;

            previousInitManagerOnStart = settings.InitManagerOnStart;
            settings.InitManagerOnStart = false;
            changed = true;
#endif
        }

        public void Cleanup()
        {
#if UNITY_EDITOR
            if (!changed)
                return;

            if (XRGeneralSettings.Instance != null)
                XRGeneralSettings.Instance.InitManagerOnStart = previousInitManagerOnStart;
            changed = false;
#endif
        }
    }
}
