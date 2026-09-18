using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace TaxiVR.Tests.EditMode
{
    /// <summary>El APK de Meta Quest sale del Build Profile, no de los ajustes globales: el perfil es el unico
    /// sitio donde viven los scripting defines de Android y el nivel de calidad de Quest. Se rompe facil desde
    /// la ventana de Build Profiles y sin el el APK arranca sin el optimizador de runtime de Meta.</summary>
    public sealed class QuestBuildProfileTests
    {
        static BuildProfile Profile() => AssetDatabase.LoadAssetAtPath<BuildProfile>(
            TaxiVR.Bootstrap.Editor.QuestApkBuilder.ProfilePath);

        [Test]
        public void TheMetaQuestProfileExists()
        {
            Assert.That(Profile(), Is.Not.Null, "Falta el Build Profile de Meta Quest.");
        }

        [Test]
        public void TheProfileOverridesTheGlobalSceneListWithTheProductionScene()
        {
            var profile = Profile();
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.overrideGlobalScenes, Is.True, "El perfil debe mandar sobre la lista global de escenas.");

            var scenes = profile.GetScenesForBuild().Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            Assert.That(scenes, Is.EqualTo(new[] { TaxiVR.Playable.Editor.PlayableBuilder.Scene }),
                "El APK debe compilar la escena de produccion y solo esa.");
        }

        /// <summary>Los defines de Android no estan ni en ProjectSettings.asset (que solo declara el de
        /// Standalone) ni en BuildProfile.scriptingDefines (vacio, m_HasScriptingDefines: 0): viven en la
        /// sobreescritura de PlayerSettings del perfil. Ahi es donde se pierden sin que nadie se entere.</summary>
        [Test]
        public void TheProfileOverridesTheAndroidScriptingDefines()
        {
            var profile = Profile();
            Assert.That(profile, Is.Not.Null);
            var settings = new SerializedObject(profile).FindProperty("m_PlayerSettingsYaml.m_Settings");
            Assert.That(settings, Is.Not.Null, "El perfil deberia llevar su propia sobreescritura de PlayerSettings.");

            var overrides = string.Join("\n", Enumerable.Range(0, settings.arraySize)
                .Select(index => settings.GetArrayElementAtIndex(index).FindPropertyRelative("line").stringValue));
            Assert.That(overrides, Does.Contain("Android: ENABLE_RUNTIME_OPTIMIZER"),
                "Sin ENABLE_RUNTIME_OPTIMIZER el APK pierde el optimizador de runtime de Meta.");
            Assert.That(overrides, Does.Contain("OVR_DISABLE_HAND_PINCH_BUTTON_MAPPING"));
        }
    }
}
