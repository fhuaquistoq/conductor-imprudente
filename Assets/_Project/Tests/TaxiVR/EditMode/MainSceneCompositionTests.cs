using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaxiVR.Playable;

namespace TaxiVR.Tests.EditMode
{
    /// <summary>La escena de produccion es unica. Estos invariantes son los que la hacen jugable: la raiz de
    /// composicion con sus assets, una luz direccional y una sola camara activa.</summary>
    public sealed class MainSceneCompositionTests
    {
        const string MainScenePath = Playable.Editor.PlayableBuilder.Scene;

        [Test]
        public void MainIsTheOnlyEnabledBuildScene()
        {
            var enabled = UnityEditor.EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            Assert.That(enabled, Has.Length.EqualTo(1));
            Assert.That(enabled[0].path, Is.EqualTo(MainScenePath));
        }

        [Test]
        public void MainOwnsThePlayableCompositionWithItsAssets()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            Assert.That(scene.name, Is.EqualTo("Main"));
            var roots = scene.GetRootGameObjects();

            var root = roots.SelectMany(r => r.GetComponentsInChildren<PlayableRoot>(true)).ToArray();
            Assert.That(root, Has.Length.EqualTo(1), "Main debe tener exactamente una raiz de composicion.");
            Assert.That(root[0].Assets, Is.Not.Null, "La raiz necesita el catalogo de assets de la ciudad.");

            var city = roots.SelectMany(r => r.GetComponentsInChildren<EndlessCity>(true)).ToArray();
            Assert.That(city, Has.Length.EqualTo(1), "La ciudad infinita debe estar en la escena.");
            Assert.That(city[0].Assets, Is.Not.Null);
        }

        [Test]
        public void MainHasOneDirectionalSunAndASingleActiveCamera()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();

            var suns = roots.SelectMany(root => root.GetComponentsInChildren<Light>(true))
                .Where(light => light.enabled && light.type == LightType.Directional).ToArray();
            Assert.That(suns, Has.Length.EqualTo(1), "La especificacion pide una sola luz direccional con sombras.");
            Assert.AreEqual(LightShadows.Soft, suns[0].shadows);

            // El modelo del taxi trae sus propias camaras para los retrovisores y la vista previa editorial las
            // arrastra a la escena. Se excluye ese subarbol, que no viaja en la compilacion, para medir de
            // verdad cuantas camaras tiene el juego.
            var cameras = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .Where(camera => camera.enabled && !IsEditorOnly(camera.transform)).ToArray();
            Assert.That(cameras, Has.Length.EqualTo(1), "Debe haber una unica camara activa: la del jugador sentado.");

            var listeners = roots.SelectMany(root => root.GetComponentsInChildren<AudioListener>(true))
                .Where(listener => listener.enabled && !IsEditorOnly(listener.transform)).ToArray();
            Assert.That(listeners, Has.Length.EqualTo(1));
        }

        static bool IsEditorOnly(Transform transform)
        {
            for (var node = transform; node != null; node = node.parent)
                if (node.CompareTag("EditorOnly")) return true;
            return false;
        }
    }
}
