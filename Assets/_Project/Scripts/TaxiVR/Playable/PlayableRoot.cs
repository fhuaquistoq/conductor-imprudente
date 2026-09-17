using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Management;

namespace TaxiVR.Playable
{
    public sealed class PlayableRoot : MonoBehaviour
    {
        public static PlayableRoot Instance { get; private set; }
        public CityAssets Assets;
        public bool DesktopInEditor = true;
        public TaxiDrive Drive { get; private set; }
        public EndlessCity City { get; private set; }
        public PlayerHands Player { get; private set; }
        public TaxiGPS GPS { get; private set; }
        public FootReceiver Feet { get; private set; }
        public SkidController Skid { get; private set; }
        public string Status => Player.TrackingStatus;
        AudioSource radio, engine, effects;
        AudioClip click;
        AudioClip[] stations;
        TextMesh speedText, radioText;
        float volume = .3f;
        int station;
        bool radioOn = true, debug = true;
        Transform cabin;
        bool initializedXR;
        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 90; Application.runInBackground = true;
            QualitySettings.vSyncCount = 0; Time.fixedDeltaTime = 1f / 90;
            Physics.IgnoreLayerCollision(8, 9, true);
            BuildWorld();
        }
        IEnumerator Start()
        {
            bool desktop = Array.IndexOf(Environment.GetCommandLineArgs(), "-taxivr-desktop") >= 0 || Application.isEditor && DesktopInEditor;
            Player.Desktop = desktop;
            if (!desktop)
            {
                var manager = XRGeneralSettings.Instance?.Manager;
                if (manager != null)
                {
                    yield return manager.InitializeLoader();
                    if (manager.activeLoader != null) { manager.StartSubsystems(); initializedXR = true; }
                    else { Player.Desktop = true; Debug.LogWarning("No OpenXR headset available; desktop controls enabled."); }
                }
                else Player.Desktop = true;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-taxivr-verify") >= 0) gameObject.AddComponent<PlayableVerification>();
        }
        void BuildWorld()
        {
            var car = new GameObject("Taxi"); car.transform.position = new Vector3(3, 0, 22);
            var body = car.AddComponent<Rigidbody>(); body.mass = 1100; body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            var collision = car.AddComponent<BoxCollider>(); collision.center = new Vector3(0, .52f, 0); collision.size = new Vector3(1.65f, .8f, 3.95f);
            car.layer = 9;
            Drive = car.AddComponent<TaxiDrive>();
            cabin = Instantiate(Assets.Taxi, car.transform).transform;
            cabin.name = "Cabina Taxi Full";
            foreach (var t in cabin.GetComponentsInChildren<Transform>())
            {
                t.gameObject.layer = 9;
                if (t.name.StartsWith("PREVIEW", StringComparison.Ordinal)) t.gameObject.SetActive(false);
            }
            foreach (var cam in cabin.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
            foreach (var listener in cabin.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            foreach (var col in cabin.GetComponentsInChildren<Collider>()) col.enabled = false;
            Drive.Feet = Feet = car.AddComponent<FootReceiver>();
            Skid = car.AddComponent<SkidController>();
            Skid.Drive = Drive;
            Skid.RearLeft = Find("Taxi_BackWheels");
            Skid.RearRight = Find("Taxi_BackWheels.001") ?? Skid.RearLeft;
            var rig = new GameObject("Seated XR player"); rig.transform.SetParent(car.transform, false);
            Player = rig.AddComponent<PlayerHands>(); Player.Assets = Assets; Player.SeatEye = new Vector3(-.405f, .92f, -.2f);
            Player.View = new GameObject("First person camera").AddComponent<Camera>(); Player.View.transform.SetParent(rig.transform, false);
            Player.View.transform.localPosition = Player.SeatEye; Player.View.tag = "MainCamera";
            Player.View.nearClipPlane = .025f; Player.View.farClipPlane = 185; Player.View.fieldOfView = 76;
            Player.View.backgroundColor = RenderSettings.fogColor; Player.View.clearFlags = CameraClearFlags.SolidColor;
            Player.View.gameObject.AddComponent<AudioListener>(); Drive.Player = Player;
            City = new GameObject("Ciudad infinita").AddComponent<EndlessCity>(); City.Assets = Assets; City.Taxi = car.transform;
            BuildCockpit(); BuildAudio();
            for (int i = 0; i < 8; i++)
            {
                bool ns = i % 2 == 0; int sign = i % 4 < 2 ? 1 : -1;
                var go = new GameObject("Trafico " + i); go.transform.SetParent(City.transform);
                go.transform.position = ns ? new Vector3((i / 4) * 64 + sign * 3, 0, -90 + i * 28) : new Vector3(-90 + i * 27, 0, (i / 4) * 64 - sign * 3);
                var model = Instantiate(Assets.Cars[i % Assets.Cars.Length], go.transform); model.transform.localPosition = Vector3.zero;
                go.transform.rotation = Quaternion.LookRotation(ns ? Vector3.forward * sign : Vector3.right * sign);
                var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
                var collider = go.AddComponent<BoxCollider>(); collider.center = new Vector3(0, .55f, 0); collider.size = new Vector3(1.8f, 1.1f, 4.2f);
                var traffic = go.AddComponent<CityTraffic>(); traffic.City = City; traffic.NorthSouth = ns; traffic.Sign = sign; traffic.CruiseSpeed = 5 + i * .3f;
            }
        }
        Transform Find(string name) => cabin.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        CockpitInteractable Bind(string name, CockpitKind kind, string caption, float radius = .07f)
        {
            var target = Find(name);
            if (target == null) throw new InvalidOperationException("Missing taxi pivot: " + name);
            var anchor = new GameObject(caption); anchor.transform.SetParent(Drive.transform, false); anchor.transform.position = target.position;
            anchor.transform.rotation = kind == CockpitKind.Wheel ? target.rotation : Drive.transform.rotation;
            target.SetParent(anchor.transform, true);
            var interaction = anchor.AddComponent<CockpitInteractable>(); interaction.Kind = kind; interaction.Caption = caption; interaction.Visual = target; interaction.Radius = radius;
            var sphere = anchor.AddComponent<SphereCollider>(); sphere.radius = radius; sphere.isTrigger = true; anchor.layer = 8;
            return interaction;
        }
        CockpitInteractable Button(string name, Vector3 position, string label, Action action, Material material)
        {
            var go = Shape.Part(name, Drive.transform, position, new Vector3(.052f, .027f, .018f), material, collider:true);
            // Keep interaction coordinates in metres, independent of visual dimensions.
            var anchor = new GameObject(name); anchor.transform.SetParent(Drive.transform, false); anchor.transform.localPosition = position;
            go.transform.SetParent(anchor.transform, true); anchor.layer = go.layer = 8;
            var c = anchor.AddComponent<CockpitInteractable>(); c.Kind = CockpitKind.Button; c.Caption = label; c.Radius = .033f; c.Visual = go.transform; c.Activated = action;
            Shape.Label(name + " label", Drive.transform, position + new Vector3(0, -.024f, -.015f), label, .0022f, Color.white, Assets.Font);
            return c;
        }
        void BuildCockpit()
        {
            Drive.Wheel = Bind("PIVOT_VOLANTE", CockpitKind.Wheel, "Volante: agarrar y girar", .17f);
            Bind("PIVOT_CAMBIOS_DR", CockpitKind.Gear, "Cambio D / R (detenerse)", .11f).Changed = value => Drive.ChangeDirection(value > 0 ? 1 : -1);
            var knob = Bind("PIVOT_RADIO_-0.067", CockpitKind.Knob, "Volumen: agarrar y girar", .055f); knob.Value = volume;
            knob.Changed = v => volume = v;
            var tune = Bind("PIVOT_RADIO_0.137", CockpitKind.Knob, "Sintonizar emisora", .05f);
            tune.Changed = v => SetStation(Mathf.Min(2, Mathf.FloorToInt(v * 3)));
            Button("Radio power", new Vector3(-.043f, .6f, .445f), "RADIO", () => radioOn = !radioOn, Assets.Yellow);
            Button("Station", new Vector3(.035f, .6f, .445f), "FM", () => SetStation((station + 1) % 3), Assets.Blue);
            Button("Cruise", new Vector3(-.185f, .737f, .487f), "CRUCERO", () => Drive.Cruise = !Drive.Cruise, Assets.Blue);
            Button("Brake", new Vector3(-.10f, .737f, .487f), "FRENAR", () => { Drive.Cruise = false; Drive.Body.linearVelocity = Vector3.zero; }, Assets.Red);
            var glass = Find("Taxi_L_FRONT_GLASS"); Vector3 originalGlass = glass.localPosition; bool open = false;
            Button("Window", new Vector3(-.659f, .598f, .39f), "VENTANA", () => { open = !open; glass.localPosition = originalGlass + glass.parent.InverseTransformVector(Vector3.down * (open ? .33f : 0)); }, Assets.Dark);
            speedText = Shape.Label("Velocimetro", Drive.transform, new Vector3(-.405f, .759f, .574f), "00 km/h", .007f, new Color(.7f, 1, .9f), Assets.Font);
            speedText.transform.localRotation = Quaternion.Euler(10, 0, 0);
            Shape.Part("Radio display backing", Drive.transform, new Vector3(.035f, .661f, .432f), new Vector3(.15f, .038f, .008f), Assets.Dark);
            radioText = Shape.Label("Radio display", Drive.transform, new Vector3(.035f, .661f, .423f), "RADIO", .003f, new Color(.45f, 1, .87f), Assets.Font);
            var gpsBase = new GameObject("GPS interactivo"); gpsBase.transform.SetParent(Drive.transform, false); gpsBase.transform.localPosition = new Vector3(.035f, .803f, .448f); gpsBase.transform.localRotation = Quaternion.Euler(15, 0, 0);
            Shape.Part("GPS frame", gpsBase.transform, Vector3.zero, new Vector3(.25f, .178f, .02f), Assets.Dark);
            var screen = Shape.Part("Mapa GPS", gpsBase.transform, new Vector3(0, 0, -.013f), new Vector3(.224f, .14f, 1), Assets.Glass, PrimitiveType.Quad);
            screen.GetComponent<Renderer>().material = new Material(Assets.UnlitShader);
            GPS = gpsBase.AddComponent<TaxiGPS>(); GPS.City = City; GPS.Drive = Drive; GPS.Screen = screen.GetComponent<Renderer>();
            GPS.Readout = Shape.Label("GPS instruction", gpsBase.transform, new Vector3(0, .083f, -.02f), "DESTINO", .0024f, Color.white, Assets.Font);
            Button("GPS on", new Vector3(-.066f, .704f, .415f), "GPS", () => GPS.Powered = !GPS.Powered, Assets.Blue);
            Button("GPS route", new Vector3(.03f, .704f, .415f), "RUTA", () => GPS.NextDestination(), Assets.Yellow);
            var gpsGrab = gpsBase.AddComponent<CockpitInteractable>(); gpsGrab.Kind = CockpitKind.Mirror; gpsGrab.Caption = "GPS: ajustar inclinacion"; gpsGrab.Radius = .1f; gpsGrab.Visual = gpsBase.transform;
            gpsBase.layer = 8; var gpsCollider = gpsBase.AddComponent<BoxCollider>(); gpsCollider.size = new Vector3(.25f, .178f, .04f); gpsCollider.isTrigger = true;
            Mirror("PIVOT_ESPEJO_INTERIOR", new Vector3(0, 1.015f, .343f), new Vector2(.23f, .065f));
            Mirror("PIVOT_RETROVISOR_ORIGINAL_L", new Vector3(-.81f, .76f, .79f), new Vector2(.11f, .073f));
            Mirror("PIVOT_RETROVISOR_ORIGINAL_R", new Vector3(.81f, .76f, .79f), new Vector2(.11f, .073f));
            Food("Hamburguesa", new Vector3(.23f, .49f, -.08f), true);
            Food("Cafe", new Vector3(.09f, .52f, -.19f), false);
            var note = Shape.Part("Ticket", Drive.transform, new Vector3(.4f, .5f, .05f), new Vector3(.16f, .22f, .003f), Assets.White);
            note.transform.localRotation = Quaternion.Euler(65, -15, 0);
            var paper = new GameObject("Hoja de instrucciones"); paper.transform.SetParent(Drive.transform, false); paper.transform.localPosition = note.transform.localPosition;
            note.transform.SetParent(paper.transform, true);
            var grab = paper.AddComponent<CockpitInteractable>(); grab.Kind = CockpitKind.Loose; grab.Caption = "Coger instrucciones"; grab.Radius = .13f; grab.Visual = paper.transform;
            paper.layer = 8; var pc = paper.AddComponent<BoxCollider>(); pc.size = new Vector3(.18f, .15f, .15f);
            var rb = paper.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.mass = .02f;
            var text = Shape.Label("Ticket text", paper.transform, new Vector3(0, .015f, -.035f), "TAXI VR\nRUTA LIBRE\n\nSIGUE EL GPS\nDETENTE EN AZUL\n\nBUEN VIAJE", .005f, Color.black, Assets.Font); text.transform.localRotation = Quaternion.Euler(65, -15, 0);
            Shape.Part("Cabin tray collision", Drive.transform, new Vector3(.28f, .37f, .06f), new Vector3(.65f, .06f, .65f), Assets.Dark, collider:true).layer = 8;
        }
        void Mirror(string pivot, Vector3 position, Vector2 size)
        {
            var interaction = Bind(pivot, CockpitKind.Mirror, "Retrovisor: agarrar y orientar", .1f);
            var quad = Shape.Part("Reflejo", Drive.transform, position, new Vector3(size.x, size.y, 1), Assets.Glass, PrimitiveType.Quad);
            quad.layer = 8; quad.transform.SetParent(interaction.Visual, true); quad.GetComponent<Renderer>().material = new Material(Assets.UnlitShader);
            var mirror = quad.AddComponent<TaxiMirror>(); mirror.Vehicle = Drive.transform; mirror.Adjustment = interaction.Visual; mirror.Surface = quad.GetComponent<Renderer>();
        }
        void Food(string name, Vector3 position, bool burger)
        {
            var root = new GameObject(name); root.layer = 8; root.transform.SetParent(Drive.transform, false); root.transform.localPosition = position;
            if (burger)
            {
                Shape.Part("Bread", root.transform, Vector3.zero, new Vector3(.115f, .065f, .115f), Assets.Facades[1], PrimitiveType.Sphere);
                Shape.Part("Filling", root.transform, new Vector3(0, -.008f, 0), new Vector3(.12f, .016f, .12f), Assets.Red, PrimitiveType.Cylinder);
                Shape.Part("Lettuce", root.transform, new Vector3(0, .008f, 0), new Vector3(.125f, .006f, .12f), Assets.Foliage, PrimitiveType.Cylinder);
            }
            else
            {
                Shape.Part("Cup", root.transform, Vector3.zero, new Vector3(.07f, .055f, .07f), Assets.White, PrimitiveType.Cylinder);
                Shape.Part("Lid", root.transform, new Vector3(0, .055f, 0), new Vector3(.08f, .006f, .08f), Assets.Dark, PrimitiveType.Cylinder);
            }
            var col = root.AddComponent<SphereCollider>(); col.radius = .055f;
            var rb = root.AddComponent<Rigidbody>(); rb.mass = .15f; rb.isKinematic = true; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var grab = root.AddComponent<CockpitInteractable>(); grab.Kind = CockpitKind.Loose; grab.Caption = "Coger " + name; grab.Radius = .1f; grab.Visual = root.transform;
        }
        AudioSource Source(string name)
        {
            var source = new GameObject(name).AddComponent<AudioSource>(); source.transform.SetParent(Drive.transform, false); source.spatialBlend = 0;
            return source;
        }
        void BuildAudio()
        {
            stations = new[] { Sound("Costa FM", 8, 110, 0), Sound("Noche Jazz", 8, 146.83f, 1), Sound("Ruta Electronica", 8, 130.81f, 2) };
            radio = Source("Radio"); radio.clip = stations[0]; radio.loop = true; radio.Play();
            engine = Source("Motor"); engine.clip = Sound("Engine", 2, 45, 3); engine.loop = true; engine.volume = .035f; engine.Play();
            effects = Source("Click"); click = Sound("Click", .055f, 720, 4);
        }
        AudioClip Sound(string name, float seconds, float frequency, int kind)
        {
            int count = (int)(22050 * seconds); var data = new float[count];
            float[] notes = { 1, 1.25f, 1.5f, 2, 1.5f, 1.25f, 1.125f, 1.5f };
            for (int i = 0; i < count; i++)
            {
                float t = i / 22050f; float f = frequency * (kind < 3 ? notes[(int)(t * 2) % notes.Length] : 1);
                float envelope = kind == 4 ? 1 - t / seconds : kind < 3 ? Mathf.Exp(-Mathf.Repeat(t, .5f) * 5) : 1;
                data[i] = (Mathf.Sin(t * f * Mathf.PI * 2) * .16f + Mathf.Sin(t * f * Mathf.PI * 4) * .035f) * envelope;
            }
            var clip = AudioClip.Create(name, count, 1, 22050, false); clip.SetData(data, 0); return clip;
        }
        void SetStation(int value) { station = value; if (radio != null) { radio.clip = stations[station]; radio.Play(); } }
        public void Click() { if (effects != null && click != null) effects.PlayOneShot(click, .6f); }
        void Update()
        {
            if (Keyboard.current?.f1Key.wasPressedThisFrame == true) debug = !debug;
            if (radio != null) { radio.volume = radioOn ? volume * .4f : 0; engine.pitch = .8f + Mathf.Abs(Drive.Speed) * .065f + (Skid == null ? 0 : Skid.Intensity * .5f); }
            speedText.text = $"{Mathf.Abs(Drive.Speed) * 3.6f:00} km/h  {(Drive.Direction > 0 ? "D" : "R")}";
            radioText.text = radioOn ? $"{new[] { "COSTA FM", "NOCHE JAZZ", "RUTA FM" }[station]}  {volume * 100:0}%" : "RADIO OFF";
        }
        void OnGUI()
        {
            if (Player == null) return;
            float scale = Mathf.Clamp(Screen.width / 1280f, .7f, 2); GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            var style = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.UpperLeft, padding = new RectOffset(16, 16, 12, 12) };
            style.normal.textColor = new Color(.88f, .95f, .97f);
            GUI.Box(new Rect(20, 20, 390, 65), $"TAXI VR  /  CIUDAD ABIERTA\n{Status}  |  Entregas {GPS.Deliveries}  |  {Mathf.Abs(Drive.Speed)*3.6f:0} km/h", style);
            if (debug) GUI.Box(new Rect(20, 95, 390, 320), DebugText(), style);
            var warning = Feet == null ? null : Feet.Warning;
            if (!string.IsNullOrEmpty(warning)) GUI.Box(new Rect(Screen.width / scale / 2 - 210, 24, 420, 40), warning, style);
            if (!string.IsNullOrEmpty(Player.HoverCaption)) GUI.Box(new Rect(Screen.width / scale / 2 - 210, Screen.height / scale - 75, 420, 48), Player.HoverCaption, style);
            if (Drive.Paused) GUI.Box(new Rect(Screen.width / scale / 2 - 180, Screen.height / scale / 2 - 40, 360, 80), "PAUSA\nESC para continuar", style);
            GUI.matrix = Matrix4x4.identity;
        }
        string DebugText()
        {
            var machine = Feet == null ? null : Feet.Machine;
            string Side(FootState state, bool valid) => valid ? state.ToString() : "sin datos";
            return string.Join("\n", new[]
            {
                $"FPS {1f / Mathf.Max(.0001f, Time.smoothDeltaTime):0}",
                $"Acelerador {Drive.Throttle:0.00}   Freno {Drive.Brake:0.00}   Derrape {(Drive.Skidding ? "si" : "no")}",
                $"Pie verde {Side(machine?.Green ?? FootState.Unknown, machine != null && machine.GreenValid)}",
                $"Pie rojo {Side(machine?.Red ?? FootState.Unknown, machine != null && machine.RedValid)}",
                $"Pies armados {(machine != null && machine.Armed ? "si" : "no")}   valido {(machine != null && machine.TrackingValid ? "si" : "no")}",
                $"Receptor de pies {(Feet == null ? "ausente" : Feet.SocketBound ? Feet.TrackingReceived ? "recibiendo" : "a la escucha" : "sin puerto")}",
                $"Volante {Drive.Wheel?.Value ?? 0:0} grados   {Mathf.Abs(Drive.Speed) * 3.6f:0} km/h   {(Drive.Direction > 0 ? "D" : "R")}",
                $"Colisiones {Drive.Collisions}",
                string.Empty,
                "W acelerar  /  S o ESPACIO frenar",
                "A / D girar  /  Q reversa  /  E avanzar",
                "R volver a la calle  /  H centrar vista",
                "Raton derecho + mover: mirar",
                "Clic: tocar  /  mantener y arrastrar: agarrar",
                "Rueda del raton: ajustar / acercar objetos",
                "C crucero suave  /  ESC pausa  /  F1 debug",
                "VR: grip o pinza para agarrar; dedo para tocar",
                "Pies: verde acelera, rojo frena",
            });
        }
        void OnDestroy()
        {
            if (initializedXR) { XRGeneralSettings.Instance.Manager.StopSubsystems(); XRGeneralSettings.Instance.Manager.DeinitializeLoader(); }
            if (Instance == this) Instance = null;
        }
    }
}
