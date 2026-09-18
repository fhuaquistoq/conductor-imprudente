using System.Collections.Generic;
using UnityEngine;
using TaxiVR.Gameplay;

namespace TaxiVR.Playable
{
    /// <summary>Navegador diegetico. Se enciende solo cuando hay pasajero, dibuja desde <see cref="CityGraph"/>
    /// y solo recalcula la ruta cuando el taxi se aleja de ella (la especificacion pide un segundo de
    /// desvio), no en cada frame.</summary>
    public sealed class TaxiGPS : MonoBehaviour
    {
        public const float MapMetres = 600f;
        public const float OffRouteMetres = 10f;
        public const float OffRouteSeconds = 1f;

        public EndlessCity City;
        public TaxiDrive Drive;
        public CityGraph Graph;
        public Renderer Screen;
        public TextMesh Readout;

        public bool Powered;
        public Vector2Int Destination { get; private set; }
        public List<Vector2Int> Path { get; private set; } = new();

        public float Distance => Vector3.Distance(City.AbsolutePosition, new Vector3(Destination.x * CityGraph.BlockSize + 3, 0, Destination.y * CityGraph.BlockSize + 14));

        public bool Arrived => Path.Count > 0 && Distance < 8f;

        const int Width = 256;
        const int Height = 192;
        static readonly float PixelsPerMetre = Height / MapMetres;

        Texture2D map;
        Color32[] pixels;
        Transform marker;
        float offRouteTime;
        Vector2Int lastNode = new(int.MinValue, int.MinValue);
        bool routeDirty = true;
        // La textura es lo caro (256x192 RGBA): se repinta al cambiar de cruce, al recalcular la ruta o, como
        // mucho, diez veces por segundo. La lectura de texto si puede cambiar cada fotograma.
        const float RepaintInterval = .1f;
        float repaintTimer;
        Vector2Int paintedNode = new(int.MinValue, int.MinValue);

        public Vector2Int CurrentNode
        {
            get
            {
                var position = City.AbsolutePosition;
                return new Vector2Int(Mathf.RoundToInt(position.x / CityGraph.BlockSize), Mathf.RoundToInt(position.z / CityGraph.BlockSize));
            }
        }

        void Start()
        {
            map = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Mapa GPS" };
            pixels = new Color32[Width * Height];
            if (Screen != null) Screen.material.mainTexture = map;
            marker = Shape.Part("Destino - zona de parada", City.transform, Vector3.zero, new Vector3(5, .03f, 10), City.Assets.Yellow).transform;
            Shape.Part("Poste del destino", City.transform, Vector3.zero, new Vector3(.3f, 4f, .3f), City.Assets.Yellow).transform.SetParent(marker, true);
        }

        /// <summary>Fija un destino nuevo y traza la ruta desde el grafo.</summary>
        public void SetDestination(Vector2Int destination)
        {
            Destination = destination;
            routeDirty = true;
        }

        public void Power(bool on) => Powered = on;

        public void Replot() => routeDirty = true;

        /// <summary>Distancia del taxi a la polilinea de la ruta. Decide el recalculado.</summary>
        float DistanceToRoute(Vector3 position)
        {
            if (Path.Count == 0) return float.MaxValue;
            float best = float.MaxValue;
            var point = new Vector2(position.x, position.z);
            for (int i = 0; i < Path.Count; i++)
            {
                var node = new Vector2(Path[i].x * CityGraph.BlockSize, Path[i].y * CityGraph.BlockSize);
                if (i < Path.Count - 1)
                {
                    var next = new Vector2(Path[i + 1].x * CityGraph.BlockSize, Path[i + 1].y * CityGraph.BlockSize);
                    best = Mathf.Min(best, DistanceToSegment(point, node, next));
                }
                else best = Mathf.Min(best, Vector2.Distance(point, node));
            }
            return best;
        }

        static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float length = ab.sqrMagnitude;
            if (length < .0001f) return Vector2.Distance(point, a);
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / length);
            return Vector2.Distance(point, a + ab * t);
        }

        void Update()
        {
            if (marker != null) marker.position = City.LocalPosition(Destination) + new Vector3(3, .04f, 14);

            if (Graph == null) return;
            if (!Powered) { if (Readout != null) Readout.text = "GPS APAGADO"; if (Screen != null) Screen.enabled = false; return; }
            if (Screen != null) Screen.enabled = true;

            var node = CurrentNode;
            bool moved = node != lastNode;
            if (moved) { lastNode = node; routeDirty = true; }

            if (Path.Count > 0 && Drive != null)
            {
                float off = DistanceToRoute(Drive.Body.position);
                offRouteTime = off > OffRouteMetres ? offRouteTime + Time.deltaTime : 0f;
                if (offRouteTime > OffRouteSeconds) { offRouteTime = 0f; routeDirty = true; }
            }

            bool replotted = routeDirty;
            if (routeDirty)
            {
                routeDirty = false;
                Path = Graph.Shortest(node, Destination);
            }

            repaintTimer -= Time.deltaTime;
            bool dirty = replotted || moved || node != paintedNode;
            if (!dirty && repaintTimer > 0f) { UpdateReadout(); return; }
            repaintTimer = RepaintInterval;
            paintedNode = node;
            Paint(node);
        }

        void UpdateReadout()
        {
            if (Readout != null)
                Readout.text = Arrived ? "DETENTE 2 s PARA ENTREGAR" : $"DESTINO {Distance:0} m";
        }

        void Paint(Vector2Int node)
        {
            var here = new Vector2(node.x * CityGraph.BlockSize, node.y * CityGraph.BlockSize);
            var background = new Color32(16, 32, 40, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = background;

            int span = Mathf.CeilToInt(MapMetres * .5f / CityGraph.BlockSize) + 1;
            for (int x = -span; x <= span; x++)
                for (int z = -span; z <= span; z++)
                {
                    var a = new Vector2Int(node.x + x, node.y + z);
                    DrawRoad(a, a + Vector2Int.right, here);
                    DrawRoad(a, a + Vector2Int.up, here);
                }

            for (int i = 1; i < Path.Count; i++) Line(Map(Path[i - 1], here), Map(Path[i], here), new Color32(45, 191, 255, 255), 2);
            Dot(Map(Destination, here), 5, new Color32(255, 198, 69, 255));

            var forward = Drive == null ? Vector3.forward : Drive.transform.forward;
            var centre = new Vector2Int(Width / 2, Height / 2);
            Line(centre, centre + new Vector2Int(Mathf.RoundToInt(forward.x * 12), Mathf.RoundToInt(forward.z * 12)), new Color32(255, 255, 255, 255), 3);
            Dot(centre, 3, new Color32(255, 255, 255, 255));

            map.SetPixels32(pixels);
            map.Apply(false);
            UpdateReadout();
        }

        void DrawRoad(Vector2Int a, Vector2Int b, Vector2 here)
        {
            if (Graph == null || !Graph.Exists(a, b)) return;
            var edge = Graph.Describe(a, b);
            if (edge.Direction == RoadDirection.Closed) { Line(Map(a, here), Map(b, here), new Color32(122, 26, 26, 255), 2); return; }
            float traffic = Graph.Traffic(a, b);
            var colour = traffic >= .65f ? new Color32(214, 69, 58, 255) : new Color32(58, 77, 81, 255);
            Line(Map(a, here), Map(b, here), colour, edge.Kind == RoadKind.Alley ? 1 : 2);
        }

        Vector2Int Map(Vector2Int node, Vector2 here)
        {
            float dx = (node.x * CityGraph.BlockSize - here.x) * PixelsPerMetre;
            float dz = (node.y * CityGraph.BlockSize - here.y) * PixelsPerMetre;
            return new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(Width * .5f + dx), 2, Width - 3), Mathf.Clamp(Mathf.RoundToInt(Height * .5f + dz), 2, Height - 3));
        }

        void Line(Vector2Int a, Vector2Int b, Color32 colour, int width)
        {
            int steps = Mathf.Min(4096, Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : (float)i / steps;
                Dot(new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t)), Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t))), width, colour);
            }
        }

        void Dot(Vector2Int point, int radius, Color32 colour)
        {
            for (int a = -radius; a <= radius; a++)
                for (int b = -radius; b <= radius; b++)
                {
                    int x = point.x + a, y = point.y + b;
                    if (x < 0 || x >= Width || y < 0 || y >= Height) continue;
                    pixels[y * Width + x] = colour;
                }
        }

        void OnDestroy()
        {
            if (map != null) Destroy(map);
            if (marker != null) Destroy(marker.gameObject);
        }
    }

    /// <summary>Retrovisor ajustable. Solo el interior puede desprenderse, y solo si se tira de el con
    /// decision: 18 cm del anclaje durante 250 ms.</summary>
    public sealed class TaxiMirror : MonoBehaviour
    {
        public const float DetachDistance = .18f;
        public const float DetachSeconds = .25f;

        public Transform Vehicle;
        public Transform Adjustment;
        public Renderer Surface;
        public bool Detachable;
        public CockpitInteractable Interaction;

        Camera rear;
        RenderTexture texture;
        Quaternion initial;
        Vector3 anchor;
        float strain;
        bool detached;
        static int nextCamera;
        int slot;

        public bool Detached => detached;

        void Start()
        {
            initial = Adjustment.localRotation;
            anchor = transform.position;
            slot = nextCamera++ % 3;
            texture = new RenderTexture(256, 128, 16) { name = "Retrovisor" };
            Surface.material.mainTexture = texture;
            rear = new GameObject("Camara de retrovisor").AddComponent<Camera>();
            rear.transform.SetParent(Vehicle, false);
            rear.targetTexture = texture; rear.fieldOfView = 58;
            rear.nearClipPlane = .1f; rear.farClipPlane = 110;
            rear.cullingMask = ~((1 << Layers.Interaction) | (1 << Layers.Vehicle));
            rear.renderingPath = RenderingPath.UsePlayerSettings;
            rear.allowHDR = false;
            rear.clearFlags = CameraClearFlags.SolidColor;
            rear.backgroundColor = RenderSettings.fogColor;
        }

        void LateUpdate()
        {
            if (rear != null)
            {
                // Una vista trasera por frame; los retrovisores mantienen la ultima imagen mientras tanto.
                rear.enabled = Time.frameCount % 3 == slot;
                rear.transform.localPosition = new Vector3(0, 1, -2.15f);
                rear.transform.localRotation = Quaternion.Euler(0, 180, 0) * Quaternion.Inverse(initial) * Adjustment.localRotation;
            }
            if (detached || !Detachable || Interaction == null) return;
            if (!Interaction.IsHeld) { strain = 0f; return; }
            strain = Vector3.Distance(transform.position, anchor) > DetachDistance ? strain + Time.deltaTime : 0f;
            if (strain < DetachSeconds) return;
            Detach();
        }

        void Detach()
        {
            detached = true;
            strain = 0f;
            if (rear != null) { Destroy(rear.gameObject); rear = null; }
            transform.SetParent(null, true);
            var body = gameObject.AddComponent<Rigidbody>();
            body.mass = .4f;
            gameObject.layer = Layers.Interaction;
            var collider = gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(.24f, .07f, .03f);
            Interaction.enabled = false;
            PlayableRoot.Instance?.Click();
        }

        void OnDestroy()
        {
            if (rear != null) Destroy(rear.gameObject);
            if (texture != null) { texture.Release(); Destroy(texture); }
        }
    }
}
