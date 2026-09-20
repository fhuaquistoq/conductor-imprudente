using System;
using System.Collections;
using NUnit.Framework;
using TaxiVR.Playable;
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
        public IEnumerator MainBootsTheProductionComposition()
        {
            yield return SceneManager.LoadSceneAsync("Main");
            yield return null;

            var root = Object.FindAnyObjectByType<PlayableRoot>();
            Assert.That(root, Is.Not.Null, "Main debe cargar PlayableRoot, la unica raiz de composicion de produccion.");
            Assert.That(root.Assets, Is.Not.Null, "La raiz de produccion necesita el catalogo de assets de la ciudad.");
            Assert.That(root.City, Is.Not.Null, "PlayableRoot debe construir la ciudad infinita.");
            Assert.That(root.Drive, Is.Not.Null, "PlayableRoot debe construir el taxi.");
            Assert.That(Object.FindAnyObjectByType<EndlessCity>(), Is.Not.Null);
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
