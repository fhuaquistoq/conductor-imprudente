using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TaxiVR.Tests.EditMode
{
    public sealed class BootstrapXrViewCompositionTests
    {
        const string MainScenePath = "Assets/_Project/Scenes/Main.unity";

        [Test]
        public void MainContainsTrackedCameraRigAndVisibleBootstrapGeometry()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            Assert.That(scene.name, Is.EqualTo("Main"));

            var seatOrigin = GameObject.Find("SeatOrigin");
            Assert.That(seatOrigin, Is.Not.Null, "Main must contain a seated XR origin.");
            var rig = GameObject.Find("OVRCameraRig");
            Assert.That(rig, Is.Not.Null, "Main must contain the Meta XR camera rig.");
            Assert.That(rig.transform.parent, Is.EqualTo(seatOrigin.transform));
            Assert.That(rig.transform.Find("TrackingSpace/CenterEyeAnchor"), Is.Not.Null,
                "The rig must expose a tracked center-eye anchor.");

            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude)
                .Where(camera => camera.enabled).ToArray();
            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude)
                .Where(listener => listener.enabled).ToArray();
            Assert.That(cameras, Has.Length.EqualTo(1), "The bootstrap must have one active camera.");
            Assert.That(listeners, Has.Length.EqualTo(1), "The bootstrap must have one active AudioListener.");

            var geometry = GameObject.Find("Bootstrap Geometry");
            Assert.That(geometry, Is.Not.Null);
            Assert.That(geometry.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.enabled), Is.True,
                "Bootstrap geometry must render visible evidence in the headset.");
            Assert.That(GameObject.Find("Bootstrap Light"), Is.Not.Null);
        }
    }
}
