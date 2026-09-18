using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace TaxiVR.Bootstrap.Editor
{
    /// <summary>Ajustes de OpenXR que el juego necesita para que las manos existan. El tooling solo miraba
    /// Standalone, asi que el APK de Quest se compilaba con el subsistema de manos apagado: OpenXR no creaba
    /// el XRHandSubsystem, PlayerHands se quedaba sin subsistema y todo el camino de manos era codigo muerto
    /// que solo funcionaba con los mandos Touch.</summary>
    public static class XrFeatureSetup
    {
        /// <summary>Enciende las features de entrada que el juego usa en un build target group y apaga las que
        /// estorban, y guarda los assets para que el ajuste sobreviva a la sesion.</summary>
        public static void Enable(BuildTargetGroup group)
        {
            var openxr = Settings(group);
            foreach (var feature in openxr.GetFeatures<OpenXRFeature>())
            {
                if (feature is HandTracking || feature is OculusTouchControllerProfile || feature.GetType().Name == "MetaQuestTouchPlusControllerProfile")
                    Set(feature, true);
                if (feature.GetType().Name == "OculusTouchControllerProximityProfile") Set(feature, false);
            }
            EditorUtility.SetDirty(openxr);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Lo mismo que <see cref="Enable"/> pero como lista de fallos, para poder fallar en voz alta
        /// antes de compilar en vez de repartir un APK sin manos.</summary>
        public static string[] Validate(BuildTargetGroup group)
        {
            var errors = new List<string>();
            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openxr == null) { errors.Add("No hay ajustes de OpenXR para " + group + "."); return errors.ToArray(); }
            if (openxr.GetFeature<HandTracking>()?.enabled != true)
                errors.Add(group + ": falta la feature Hand Tracking Subsystem (XR_EXT_hand_tracking).");
            if (openxr.GetFeature<OculusTouchControllerProfile>()?.enabled != true)
                errors.Add(group + ": falta el perfil de mandos Touch.");
            if (!openxr.GetFeatures<OpenXRFeature>().Any(feature => feature.GetType().Name == "MetaQuestTouchPlusControllerProfile" && feature.enabled))
                errors.Add(group + ": falta el perfil de mandos Touch Plus.");
            return errors.ToArray();
        }

        static OpenXRSettings Settings(BuildTargetGroup group) =>
            OpenXRSettings.GetSettingsForBuildTargetGroup(group)
            ?? throw new InvalidOperationException("No hay ajustes de OpenXR para " + group + ".");

        static void Set(OpenXRFeature feature, bool enabled)
        {
            if (feature.enabled == enabled) return;
            feature.enabled = enabled;
            EditorUtility.SetDirty(feature);
        }
    }
}
