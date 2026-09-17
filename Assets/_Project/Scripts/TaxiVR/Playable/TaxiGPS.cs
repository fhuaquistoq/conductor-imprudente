using System.Collections.Generic;
using UnityEngine;

namespace TaxiVR.Playable
{
    public sealed class TaxiGPS : MonoBehaviour
    {
        public EndlessCity City;
        public TaxiDrive Drive;
        public Renderer Screen;
        public TextMesh Readout;
        public Vector2Int Destination = new(1, 2);
        public int Deliveries { get; private set; }
        public bool Powered = true;
        public List<Vector2Int> Path { get; private set; } = new();
        Texture2D map;
        Color32[] pixels;
        float nextUpdate, arrivalTime;
        Transform marker;
        public float Distance => Vector3.Distance(City.AbsolutePosition, new Vector3(Destination.x * 64 + 3, 0, Destination.y * 64 + 14));
        void Start()
        {
            map = new Texture2D(256, 192, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Live GPS map" };
            pixels = new Color32[256 * 192]; Screen.material.mainTexture = map;
            marker = Shape.Part("Destino - zona de parada", City.transform, Vector3.zero, new Vector3(4, .025f, 9), City.Assets.Blue).transform;
        }
        public void NextDestination()
        {
            var pos = City.AbsolutePosition;
            var node = new Vector2Int(Mathf.RoundToInt(pos.x / 64), Mathf.RoundToInt(pos.z / 64));
            int hash = CityMath.Hash(Deliveries + 3, Destination.x + Destination.y);
            Destination = node + new Vector2Int(hash % 3 - 1, 2 + hash % 3);
            Powered = true; nextUpdate = 0;
        }
        void Update()
        {
            if (marker == null) return;
            marker.position = City.LocalPosition(Destination) + new Vector3(3, .035f, 14);
            if (Distance < 6 && Mathf.Abs(Drive.Speed) < .6f)
            {
                arrivalTime += Time.deltaTime;
                if (arrivalTime > 2) { Deliveries++; arrivalTime = 0; NextDestination(); PlayableRoot.Instance.Click(); }
            }
            else arrivalTime = 0;
            if (Time.time < nextUpdate) return;
            nextUpdate = Time.time + .25f;
            if (!Powered) { Readout.text = "GPS APAGADO"; Screen.enabled = false; return; }
            Screen.enabled = true;
            var position = City.AbsolutePosition;
            var current = new Vector2Int(Mathf.RoundToInt(position.x / 64), Mathf.RoundToInt(position.z / 64));
            Path = CityMath.Route(current, Destination);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(16, 32, 40, 255);
            Vector2Int Map(Vector3 world) => new(Mathf.RoundToInt(128 + (world.x - position.x) * .65f), Mathf.RoundToInt(70 + (world.z - position.z) * .65f));
            for (int i = -5; i <= 5; i++)
            {
                int x = Map(new Vector3((current.x + i) * 64, 0, 0)).x;
                int y = Map(new Vector3(0, 0, (current.y + i) * 64)).y;
                Line(x, 0, x, 191, new Color32(58, 77, 81, 255), 3); Line(0, y, 255, y, new Color32(58, 77, 81, 255), 3);
            }
            for (int i = 1; i < Path.Count; i++)
            {
                var a = Map(new Vector3(Path[i-1].x * 64, 0, Path[i-1].y * 64)); var b = Map(new Vector3(Path[i].x * 64, 0, Path[i].y * 64));
                Line(a.x, a.y, b.x, b.y, new Color32(45, 191, 255, 255), 2);
            }
            var end = Map(new Vector3(Destination.x * 64 + 3, 0, Destination.y * 64 + 14));
            Dot(Mathf.Clamp(end.x, 6, 249), Mathf.Clamp(end.y, 6, 185), 5, new Color32(255, 198, 69, 255));
            Dot(128, 70, 4, new Color32(255, 255, 255, 255));
            var dir = Drive.transform.forward; Line(128, 70, 128 + (int)(dir.x * 13), 70 + (int)(dir.z * 13), new Color32(255, 255, 255, 255), 1);
            map.SetPixels32(pixels); map.Apply(false);
            Readout.text = Distance < 6 ? "DETENTE 2 s PARA ENTREGAR" : $"DESTINO {Distance:0} m  |  ENTREGAS {Deliveries}";
        }
        void Dot(int x, int y, int radius, Color32 color)
        {
            for (int a = -radius; a <= radius; a++) for (int b = -radius; b <= radius; b++)
                if (x+a >= 0 && x+a < 256 && y+b >= 0 && y+b < 192) pixels[(y+b)*256+x+a] = color;
        }
        void Line(int x0, int y0, int x1, int y1, Color32 color, int width)
        {
            int steps = Mathf.Min(2048, Mathf.Max(Mathf.Abs(x1-x0), Mathf.Abs(y1-y0)));
            for (int i = 0; i <= steps; i++) { float t = steps == 0 ? 0 : (float)i / steps; Dot(Mathf.RoundToInt(Mathf.Lerp(x0,x1,t)), Mathf.RoundToInt(Mathf.Lerp(y0,y1,t)), width, color); }
        }
        void OnDestroy() { if (map != null) Destroy(map); if (marker != null) Destroy(marker.gameObject); }
    }
    public sealed class TaxiMirror : MonoBehaviour
    {
        public Transform Vehicle;
        public Transform Adjustment;
        public Renderer Surface;
        Camera rear;
        RenderTexture texture;
        Quaternion initial;
        static int nextCamera;
        int slot;
        void Start()
        {
            initial = Adjustment.localRotation;
            slot = nextCamera++ % 3;
            texture = new RenderTexture(256, 128, 16) { name = "Rear mirror" };
            Surface.material.mainTexture = texture;
            rear = new GameObject("Mirror camera").AddComponent<Camera>(); rear.transform.SetParent(Vehicle, false);
            rear.targetTexture = texture; rear.fieldOfView = 58; rear.nearClipPlane = .1f; rear.farClipPlane = 110;
            rear.cullingMask = ~((1 << 8) | (1 << 9));
            rear.renderingPath = RenderingPath.UsePlayerSettings;
            rear.allowHDR = false;
            rear.clearFlags = CameraClearFlags.SolidColor; rear.backgroundColor = RenderSettings.fogColor;
        }
        void LateUpdate()
        {
            if (rear == null) return;
            // One rear view per frame; mirrors retain the last rendered image in between.
            rear.enabled = Time.frameCount % 3 == slot;
            rear.transform.localPosition = new Vector3(0, 1, -2.15f);
            rear.transform.localRotation = Quaternion.Euler(0, 180, 0) * Quaternion.Inverse(initial) * Adjustment.localRotation;
        }
        void OnDestroy() { if (rear != null) Destroy(rear.gameObject); if (texture != null) { texture.Release(); Destroy(texture); } }
    }
}
