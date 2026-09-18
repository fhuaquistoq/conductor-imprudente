using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TaxiVR.Bootstrap
{
    public sealed class TaxiVRCompositionRoot : MonoBehaviour
    {
        [SerializeField] InputActionAsset taxiActions;
        [SerializeField] TaxiVRFeatureFlags features = new();
        InputActionMap taxiMap;
        RuntimeDiagnostics diagnostics;
        DiagnosticSnapshot snapshot;
        bool monitorVisible;

        public TaxiVRFeatureFlags Features => features;
        public DiagnosticSnapshot Snapshot => snapshot;
        public void AssignActions(InputActionAsset value) => taxiActions = value;

        void Awake()
        {
            diagnostics = new RuntimeDiagnostics();
            taxiMap = taxiActions == null ? null : taxiActions.FindActionMap("TaxiVR", false);
            snapshot = diagnostics.Capture(false, ReferencesAreValid());
            if (!snapshot.ReferencesValid) Debug.LogError(snapshot.Recovery, this);
        }

        void OnEnable() => taxiMap?.Enable();

        void Start()
        {
            snapshot = diagnostics.Capture(taxiMap?.enabled == true, ReferencesAreValid());
            diagnostics.Write(snapshot);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-taxivr-smoke") >= 0)
                Application.Quit(snapshot.ReferencesValid ? 0 : 2);
        }

        void Update()
        {
            // Las metricas de depuracion solo existen en Development: en una compilacion de release la
            // especificacion no admite HUD de ningun tipo, ni siquiera este monitor de diagnostico.
            if (!Debug.isDebugBuild && !Application.isEditor) { monitorVisible = false; return; }
            if (Keyboard.current?.f1Key.wasPressedThisFrame == true) monitorVisible = !monitorVisible;
        }

        void OnGUI()
        {
            if (monitorVisible) GUI.Box(new Rect(16, 16, 520, 120), JsonUtility.ToJson(snapshot, true));
        }

        void OnDisable() => taxiMap?.Disable();

        bool ReferencesAreValid()
        {
            var rig = GameObject.Find("OVRCameraRig");
            var eye = rig?.transform.Find("TrackingSpace/CenterEyeAnchor");
            var camera = eye?.GetComponent<Camera>();
            var geometry = GameObject.Find("Bootstrap Geometry");
            var light = GameObject.Find("Bootstrap Light");
            return gameObject.scene.name == "Main" && taxiMap != null && rig != null &&
                camera != null && camera.enabled && geometry?.GetComponent<Renderer>()?.enabled == true &&
                light?.GetComponent<Light>()?.enabled == true;
        }
    }
}
