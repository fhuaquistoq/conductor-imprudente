using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using TaxiVR.Gameplay;

namespace TaxiVR.Playable
{
    public sealed class PlayableRoot : MonoBehaviour
    {
        public static PlayableRoot Instance { get; private set; }
        public CityAssets Assets;
        public bool ForceDesktop;
        public bool ShowDebugInEditor;
        /// <summary>Semilla raiz de la partida. Fija por defecto para que una corrida sea reproducible; se puede
        /// cambiar con -seed=N en la linea de comandos.</summary>
        public const int DefaultSeed = 20260918;
        public int Seed { get; private set; } = DefaultSeed;
        public TaxiDrive Drive { get; private set; }
        public EndlessCity City { get; private set; }
        public PlayerHands Player { get; private set; }
        public TaxiGPS GPS { get; private set; }
        public FootReceiver Feet { get; private set; }
        public SkidController Skid { get; private set; }
        public GameDirector Director { get; private set; }
        public CityTrafficSystem Traffic { get; private set; }
        public PoliceSystem Police { get; private set; }
        public InteriorControls Interior { get; private set; }
        public CityGraph Graph { get; private set; }
        public string Status => Player.TrackingStatus;
        AudioSource engine, effects;
        AudioClip click;
        TextMesh speedText, radioText;
        Transform cabin;
        bool initializedXR;
        // Valores ya pintados: el velocimetro y la radio solo se reescriben cuando cambian, para no rehacer
        // la malla de texto ni asignar dos cadenas en cada fotograma.
        float lastSpeedKmh = float.NaN;
        int lastDirection = int.MinValue;
        bool lastRadioOn;
        int lastStation = int.MinValue, lastVolume = int.MinValue;
        void Awake()
        {
            Instance = this;
            var editorPreview = GameObject.Find("Editor City Preview");
            if (editorPreview != null) editorPreview.SetActive(false);
            Application.targetFrameRate = GameConstants.TargetFrameRate; Application.runInBackground = true;
            QualitySettings.vSyncCount = 0; Time.fixedDeltaTime = 1f / GameConstants.TargetFrameRate;
            Physics.IgnoreLayerCollision(Layers.Interaction, Layers.Vehicle, true);
            // Una sola semilla raiz: el grafo, el trafico, la policia y el pasajero salen de aqui, de modo que
            // la misma corrida se puede reproducir. Se registra para poder copiarla de un reporte.
            Seed = SeedFromArguments();
            UnityEngine.Random.InitState(Seed);
            Debug.Log("TaxiVR semilla " + Seed);
            BuildWorld();
        }
        IEnumerator Start()
        {
            bool desktop = ForceDesktop || Array.IndexOf(Environment.GetCommandLineArgs(), "-taxivr-desktop") >= 0;
            if (!desktop)
            {
                var manager = XRGeneralSettings.Instance?.Manager;
                if (manager != null)
                {
                    yield return manager.InitializeLoader();
                    if (manager.activeLoader != null)
                    {
                        manager.StartSubsystems();
                        // El visor tarda unos frames en engancharse, sobre todo el simulador. Antes se daba por
                        // perdido tras un solo frame y el juego caia a escritorio sin avisar de nada util.
                        for (int frame = 0; frame < 120 && !XRSettings.isDeviceActive; frame++) yield return null;
                        initializedXR = XRSettings.isDeviceActive;
                        if (!initializedXR)
                        {
                            manager.StopSubsystems(); manager.DeinitializeLoader();
                            Debug.LogWarning("No hay visor XR activo. Comprueba que el Meta XR Simulator este abierto y activado (Window > Meta > Meta XR Simulator > Activate), o que el Quest este conectado y con Link iniciado. Se activan los controles de teclado y raton.");
                        }
                    }
                    else Debug.LogWarning("No hay runtime XR disponible. Activa el simulador (Window > Meta > Meta XR Simulator > Activate) o conecta el Quest. Se activan los controles de teclado y raton.");
                }
                else Debug.LogWarning("No hay XRGeneralSettings activo en el proyecto. Se activan los controles de teclado y raton.");
                desktop = !initializedXR;
            }
            Player.Desktop = desktop;
            var arguments = Environment.GetCommandLineArgs();
            Director.SkipTrackerGate = Array.IndexOf(arguments, "-taxivr-verify") >= 0;
            if (Array.IndexOf(arguments, "-taxivr-verify") >= 0) gameObject.AddComponent<PlayableVerification>();
            if (Array.IndexOf(arguments, "-taxivr-debug") >= 0 || Application.isEditor && ShowDebugInEditor) gameObject.AddComponent<DebugOverlay>();
        }
        void BuildWorld()
        {
            var car = new GameObject("Taxi"); car.transform.position = new Vector3(3, .1f, 22);
            car.AddComponent<Rigidbody>();
            var collision = car.AddComponent<BoxCollider>(); collision.center = new Vector3(0, .62f, .02f); collision.size = new Vector3(1.62f, .92f, 3.9f);
            car.layer = Layers.Vehicle;
            Drive = car.AddComponent<TaxiDrive>();
            cabin = Instantiate(Assets.Taxi, car.transform).transform;
            cabin.name = "Cabina Taxi Full";
            foreach (var t in cabin.GetComponentsInChildren<Transform>())
            {
                t.gameObject.layer = Layers.Vehicle;
                if (t.name.StartsWith("PREVIEW", StringComparison.Ordinal)) t.gameObject.SetActive(false);
            }
            foreach (var cam in cabin.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
            foreach (var listener in cabin.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            foreach (var col in cabin.GetComponentsInChildren<Collider>()) col.enabled = false;
            Drive.BindVisualWheels(cabin);
            Drive.Feet = Feet = car.AddComponent<FootReceiver>();
            Skid = car.AddComponent<SkidController>();
            Skid.Drive = Drive;
            Skid.RearLeft = Find("Taxi_BackWheels");
            Skid.RearRight = Find("Taxi_BackWheels.001") ?? Skid.RearLeft;
            var rig = new GameObject("Seated XR player"); rig.transform.SetParent(car.transform, false);
            Player = rig.AddComponent<PlayerHands>(); Player.Assets = Assets; Player.SeatEye = new Vector3(-.405f, .92f, -.2f);
            Player.View = Camera.main;
            if (Player.View == null) Player.View = new GameObject("First person camera").AddComponent<Camera>();
            Player.View.name = "First person camera"; Player.View.transform.SetParent(rig.transform, false);
            Player.View.transform.localPosition = Player.SeatEye; Player.View.transform.localRotation = Quaternion.identity; Player.View.tag = "MainCamera";
            Player.View.enabled = true; Player.View.stereoTargetEye = StereoTargetEyeMask.Both; Player.View.targetDisplay = 0; Player.View.rect = new Rect(0, 0, 1, 1);
            Player.View.nearClipPlane = .025f; Player.View.farClipPlane = 185; Player.View.fieldOfView = 76;
            Player.View.backgroundColor = RenderSettings.fogColor; Player.View.clearFlags = CameraClearFlags.SolidColor; Player.View.cullingMask = ~0;
            if (Player.View.GetComponent<AudioListener>() == null) Player.View.gameObject.AddComponent<AudioListener>();
            Drive.Player = Player;
            City = FindAnyObjectByType<EndlessCity>();
            if (City == null) City = new GameObject("Ciudad infinita").AddComponent<EndlessCity>();
            City.Assets = Assets; City.Taxi = car.transform;
            BuildCockpit(); BuildAudio();
            BuildGameplay(car);
        }
        void BuildGameplay(GameObject car)
        {
            // Ciudad de rejilla: toda calle de la cuadricula existe y es de doble sentido salvo un cinco por
            // ciento de sentido unico, que es lo que mantiene viva la penalizacion por ir en sentido contrario.
            // El reparto se dejo en el cinco por ciento y no en el quince que pedia la especificacion original
            // porque un sentido unico de mas recorta las rutas alternativas que el selector de destino exige:
            // medido sobre la rejilla, con cinco por ciento se consiguen cuatro rutas viables en veinte de cada
            // veinticuatro destinos, y con quince en dieciseis. Las manzanas rectangulares se comen su calle
            // intermedia, y el grafo consulta esa misma decision para no ofrecer una calle que el mundo no dibuja.
            Graph = new CityGraph(Seed, 1f, .05f, 0f, 0f) { Blocked = CityGrid.IsBlocked };
            Traffic = new GameObject("Trafico").AddComponent<CityTrafficSystem>();
            Traffic.Configure(City, Assets, car.transform, Graph);
            Police = new GameObject("Policia").AddComponent<PoliceSystem>();
            Police.transform.SetParent(City.transform, false);
            Police.City = City; Police.Assets = Assets; Police.Drive = Drive; Police.Graph = Graph;
            Director = gameObject.AddComponent<GameDirector>();
            Director.Seed = Seed;
            Director.Drive = Drive; Director.City = City; Director.GPS = GPS; Director.Feet = Feet;
            Director.Player = Player; Director.Assets = Assets; Director.Graph = Graph;
            Director.Police = Police;
            Interior = gameObject.AddComponent<InteriorControls>();
            Interior.Configure(Assets, cabin, Director);
            Director.Interior = Interior;
            var recovery = car.AddComponent<TaxiRecovery>(); recovery.Drive = Drive; recovery.Director = Director;
            var collisions = car.AddComponent<CollisionReporter>(); collisions.Drive = Drive; collisions.Director = Director;
            var recenter = gameObject.AddComponent<SeatedCalibration>(); recenter.Player = Player; recenter.Drive = Drive;
        }
        static int SeedFromArguments()
        {
            const string prefix = "-seed=";
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(argument.Substring(prefix.Length), out var seed))
                    return seed;
            return DefaultSeed;
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
            var sphere = anchor.AddComponent<SphereCollider>(); sphere.radius = radius; sphere.isTrigger = true; anchor.layer = Layers.Interaction;
            return interaction;
        }
        CockpitInteractable Button(string name, Vector3 position, string label, Action action, Material material)
        {
            var go = Shape.Part(name, Drive.transform, position, new Vector3(.052f, .027f, .018f), material, collider:true);
            // Keep interaction coordinates in metres, independent of visual dimensions.
            var anchor = new GameObject(name); anchor.transform.SetParent(Drive.transform, false); anchor.transform.localPosition = position;
            go.transform.SetParent(anchor.transform, true); anchor.layer = go.layer = Layers.Interaction;
            var c = anchor.AddComponent<CockpitInteractable>(); c.Kind = CockpitKind.Button; c.Caption = label; c.Radius = .033f; c.Visual = go.transform; c.Activated = action;
            Shape.Label(name + " label", Drive.transform, position + new Vector3(0, -.024f, -.015f), label, .0022f, Color.white, Assets.Font);
            return c;
        }
        void BuildCockpit()
        {
            Drive.Wheel = Bind(CockpitAnchors.Wheel, CockpitKind.Wheel, "Volante: agarrar y girar", .17f);
            var gears = Bind(CockpitAnchors.Gear, CockpitKind.Gear, "Cambio F / O / R", .11f);
            gears.Value = 1;
            gears.Changed = value => { if (Drive.ChangeDirection(Mathf.RoundToInt(value))) gears.Value = value; };
            var knob = Bind(CockpitAnchors.VolumeKnob, CockpitKind.Knob, "Volumen: agarrar y girar", .055f);
            knob.Changed = v => Interior.SetVolume(v);
            var tune = Bind(CockpitAnchors.TuneKnob, CockpitKind.Knob, "Sintonizar emisora", .05f);
            tune.Changed = v => Interior.SetStation(Mathf.Min(4, Mathf.FloorToInt(v * 5)));
            Button("Radio power", CockpitAnchors.RadioPower, "RADIO", () => Interior.ToggleRadio(), Assets.Yellow);
            Button("Station", CockpitAnchors.Station, "FM", () => Interior.NextStation(), Assets.Blue);
            Button("Cruise", CockpitAnchors.Cruise, "CRUCERO", () => Drive.Cruise = !Drive.Cruise, Assets.Blue);
            Button("Brake", CockpitAnchors.Brake, "FRENAR", () => { Drive.Cruise = false; Drive.Body.linearVelocity = Vector3.zero; }, Assets.Red);
            var glass = Find(CockpitAnchors.FrontGlass);
            if (glass == null) ArtLog.WarnOnce("cristal", "Falta la pieza " + CockpitAnchors.FrontGlass + " en el taxi: el boton VENTANA no funcionara.");
            else
            {
                Vector3 originalGlass = glass.localPosition; bool open = false;
                Button("Window", CockpitAnchors.Window, "VENTANA", () => { open = !open; glass.localPosition = originalGlass + glass.parent.InverseTransformVector(Vector3.down * (open ? .33f : 0)); }, Assets.Dark);
            }
            speedText = Shape.Label("Velocimetro", Drive.transform, CockpitAnchors.Speedometer, "00 km/h", .007f, new Color(.7f, 1, .9f), Assets.Font);
            speedText.transform.localRotation = Quaternion.Euler(10, 0, 0);
            Shape.Part("Radio display backing", Drive.transform, CockpitAnchors.RadioDisplay, new Vector3(.15f, .038f, .008f), Assets.Dark);
            radioText = Shape.Label("Radio display", Drive.transform, CockpitAnchors.RadioText, "RADIO", .003f, new Color(.45f, 1, .87f), Assets.Font);
            var gpsBase = new GameObject("GPS interactivo"); gpsBase.transform.SetParent(Drive.transform, false); gpsBase.transform.localPosition = CockpitAnchors.GpsBase; gpsBase.transform.localRotation = Quaternion.Euler(15, 0, 0);
            Shape.Part("GPS frame", gpsBase.transform, Vector3.zero, new Vector3(.25f, .178f, .02f), Assets.Dark);
            var screen = Shape.Part("Mapa GPS", gpsBase.transform, new Vector3(0, 0, -.013f), new Vector3(.224f, .14f, 1), Assets.Glass, PrimitiveType.Quad);
            screen.GetComponent<Renderer>().material = new Material(Assets.UnlitShader);
            GPS = gpsBase.AddComponent<TaxiGPS>(); GPS.City = City; GPS.Drive = Drive; GPS.Screen = screen.GetComponent<Renderer>();
            GPS.Readout = Shape.Label("GPS instruction", gpsBase.transform, new Vector3(0, .083f, -.02f), "DESTINO", .0024f, Color.white, Assets.Font);
            Button("GPS on", CockpitAnchors.GpsOn, "GPS", () => GPS.Power(!GPS.Powered), Assets.Blue);
            Button("GPS route", CockpitAnchors.GpsRoute, "RECALCULAR", () => GPS.Replot(), Assets.Yellow);
            var gpsGrab = gpsBase.AddComponent<CockpitInteractable>(); gpsGrab.Kind = CockpitKind.Mirror; gpsGrab.Caption = "GPS: ajustar inclinacion"; gpsGrab.Radius = .1f; gpsGrab.Visual = gpsBase.transform;
            gpsBase.layer = Layers.Interaction; var gpsCollider = gpsBase.AddComponent<BoxCollider>(); gpsCollider.size = new Vector3(.25f, .178f, .04f); gpsCollider.isTrigger = true;
            Mirror(CockpitAnchors.InteriorMirror, CockpitAnchors.InteriorMirrorQuad, new Vector2(.23f, .065f), true);
            Mirror(CockpitAnchors.LeftMirror, CockpitAnchors.LeftMirrorQuad, new Vector2(.11f, .073f), false);
            Mirror(CockpitAnchors.RightMirror, CockpitAnchors.RightMirrorQuad, new Vector2(.11f, .073f), false);
            Shape.Part("Cabin tray collision", Drive.transform, CockpitAnchors.Tray, new Vector3(.65f, .06f, .65f), Assets.Dark, collider:true).layer = Layers.Interaction;
        }
        void Mirror(string pivot, Vector3 position, Vector2 size, bool detachable)
        {
            var interaction = Bind(pivot, CockpitKind.Mirror, "Retrovisor: agarrar y orientar", .1f);
            var quad = Shape.Part("Reflejo", Drive.transform, position, new Vector3(size.x, size.y, 1), Assets.Glass, PrimitiveType.Quad);
            quad.layer = Layers.Interaction; quad.transform.SetParent(interaction.Visual, true); quad.GetComponent<Renderer>().material = new Material(Assets.UnlitShader);
            var mirror = quad.AddComponent<TaxiMirror>();
            mirror.Vehicle = Drive.transform; mirror.Adjustment = interaction.Visual;
            mirror.Surface = quad.GetComponent<Renderer>();
            mirror.Detachable = detachable; mirror.Interaction = interaction;
        }
        AudioSource Source(string name)
        {
            var source = new GameObject(name).AddComponent<AudioSource>(); source.transform.SetParent(Drive.transform, false); source.spatialBlend = 0;
            return source;
        }
        void BuildAudio()
        {
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
        public void Click() { if (effects != null && click != null) effects.PlayOneShot(click, .6f); }
        void Update()
        {
            if (engine != null) engine.pitch = .8f + Mathf.Abs(Drive.Speed) * .065f + (Skid == null ? 0 : Skid.Intensity * .5f);
            float kmh = Mathf.Abs(Drive.Speed) * 3.6f;
            if (float.IsNaN(lastSpeedKmh) || Mathf.Abs(kmh - lastSpeedKmh) >= .5f || Drive.Direction != lastDirection)
            {
                lastSpeedKmh = kmh; lastDirection = Drive.Direction;
                speedText.text = $"{kmh:00} km/h  {(Drive.Direction > 0 ? "F" : Drive.Direction < 0 ? "R" : "O")}";
            }
            bool radioOn = Interior != null && Interior.RadioOn;
            int station = Interior?.Station ?? -1;
            int volume = Mathf.RoundToInt((Interior?.Volume ?? 0f) * 100f);
            if (radioOn != lastRadioOn || station != lastStation || volume != lastVolume)
            {
                lastRadioOn = radioOn; lastStation = station; lastVolume = volume;
                radioText.text = radioOn ? $"EMISORA {station + 1}  {volume}%" : "RADIO OFF";
            }
        }
        void OnDestroy()
        {
            if (initializedXR) { XRGeneralSettings.Instance.Manager.StopSubsystems(); XRGeneralSettings.Instance.Manager.DeinitializeLoader(); }
            Shape.ClearCache(); CityProps.ClearCache();
            if (Instance == this) Instance = null;
        }
    }
}
