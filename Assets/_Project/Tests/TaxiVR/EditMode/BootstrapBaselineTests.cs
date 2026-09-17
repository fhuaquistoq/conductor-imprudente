using System.Linq;
using NUnit.Framework;
using TaxiVR.Bootstrap;
using TaxiVR.Bootstrap.Editor;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine.InputSystem;
using UnityEngine.XR.Management;

namespace TaxiVR.Tests.EditMode
{
    public sealed class BootstrapBaselineTests
    {
        [Test]
        public void PlayableIsTheOnlyEnabledBuildScene()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            Assert.That(scenes, Has.Length.EqualTo(1));
            Assert.That(scenes[0].path, Is.EqualTo(TaxiVR.Playable.Editor.PlayableBuilder.Scene));
            Assert.That(TaxiVRBuildConfiguration.Target, Is.EqualTo(BuildTarget.StandaloneWindows64));
            Assert.DoesNotThrow(TaxiVR.Playable.Editor.PlayableBuilder.Verify);
        }

        [Test]
        public void DedicatedTaxiMapUsesOnlyApprovedDevices()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(TaxiVRBuildConfiguration.InputPath);
            Assert.That(asset, Is.Not.Null);
            var paths = asset.FindActionMap("TaxiVR", true).bindings.Select(binding => binding.path).ToArray();
            Assert.That(paths, Has.Some.StartsWith("<Keyboard>"));
            Assert.That(paths, Has.Some.StartsWith("<XRController>"));
            Assert.That(paths, Has.None.Contains("Joystick").IgnoreCase.And.None.Contains("Gamepad").IgnoreCase);
            foreach (var key in new[] { "/w", "/s", "/a", "/d", "/q", "/e", "/n" })
                Assert.That(paths, Has.Some.EndsWith(key));
        }

        [TestCase(true, true, "development", "")]
        [TestCase(false, true, "release", "")]
        [TestCase(true, false, "development", RuntimeDiagnostics.RecoveryText)]
        public void DiagnosticsDescribeModeAndRecovery(bool development, bool valid, string mode, string recovery)
        {
            var snapshot = RuntimeDiagnostics.Create("build-1", "6000.6.0f1", "Main",
                "OpenXR Loader", "Meta Quest Link", true, valid, development);
            Assert.That(snapshot.BuildId, Is.EqualTo("build-1"));
            Assert.That(snapshot.Scene, Is.EqualTo("Main"));
            Assert.That(snapshot.Mode, Is.EqualTo(mode));
            Assert.That(snapshot.Recovery, Is.EqualTo(recovery));
        }

        [Test]
        public void PlayableOwnsXrStartupSoDesktopDoesNotRequireAHeadset()
        {
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.InitManagerOnStart, Is.False);
        }

        [Test]
        public void GateZeroLeavesLaterAndDeferredFeaturesAbsent()
        {
            var flags = new TaxiVRFeatureFlags();
            Assert.That(flags.DeferredSystemsAbsent, Is.True);
        }
    }
}
