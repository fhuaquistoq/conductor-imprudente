using System.Collections.Generic;
using UnityEngine;

namespace TaxiVR.Playable
{
    public sealed class EndlessCity : MonoBehaviour
    {
        public CityAssets Assets;
        public Transform Taxi;
        public const int Radius = 3;
        public Vector2Int OriginSector { get; private set; }
        public int LoadedSectors => sectors.Count;
        readonly Dictionary<Vector2Int, CitySector> sectors = new();
        readonly Queue<CitySector> pool = new();
        readonly List<Vector2Int> remove = new();
        Vector2Int last = new(int.MinValue, int.MinValue);
        public Vector3 AbsolutePosition => Taxi.position + new Vector3(OriginSector.x * 64f, 0, OriginSector.y * 64f);
        public Vector3 LocalPosition(Vector2Int node) => new((node.x - OriginSector.x) * 64f, 0, (node.y - OriginSector.y) * 64f);
        void Start() { Refresh(); }
        void Update()
        {
            if (Taxi == null) return;
            if (Mathf.Abs(Taxi.position.x) > 1024 || Mathf.Abs(Taxi.position.z) > 1024) Rebase();
            Refresh();
        }
        public void Refresh()
        {
            var center = new Vector2Int(CityMath.Sector(Taxi.position.x) + OriginSector.x, CityMath.Sector(Taxi.position.z) + OriginSector.y);
            if (center == last) return;
            last = center; remove.Clear();
            foreach (var pair in sectors)
                if (Mathf.Abs(pair.Key.x - center.x) > Radius || Mathf.Abs(pair.Key.y - center.y) > Radius) remove.Add(pair.Key);
            foreach (var key in remove) { pool.Enqueue(sectors[key]); sectors.Remove(key); }
            for (int x = -Radius; x <= Radius; x++) for (int z = -Radius; z <= Radius; z++)
            {
                var key = center + new Vector2Int(x, z);
                if (sectors.ContainsKey(key)) continue;
                CitySector sector;
                if (pool.Count > 0) sector = pool.Dequeue();
                else { var go = new GameObject("City sector"); go.transform.SetParent(transform); sector = go.AddComponent<CitySector>(); sector.Build(Assets); }
                sector.Place(key, LocalPosition(key)); sectors.Add(key, sector);
            }
            foreach (var pair in sectors)
            {
                bool near = Mathf.Abs(pair.Key.x - center.x) <= 1 && Mathf.Abs(pair.Key.y - center.y) <= 1;
                foreach (var pedestrian in pair.Value.GetComponentsInChildren<CityPedestrian>(true)) pedestrian.gameObject.SetActive(near);
            }
        }
        void Rebase()
        {
            var delta = new Vector2Int(CityMath.Sector(Taxi.position.x), CityMath.Sector(Taxi.position.z));
            var shift = new Vector3(delta.x * 64f, 0, delta.y * 64f);
            OriginSector += delta;
            var body = Taxi.GetComponent<Rigidbody>();
            body.position -= shift;
            foreach (var sector in sectors.Values) sector.transform.position -= shift;
            foreach (var car in GetComponentsInChildren<CityTraffic>()) car.Shift(shift);
            Physics.SyncTransforms();
        }
    }

    public sealed class CitySector : MonoBehaviour
    {
        Transform[] plots = new Transform[4];
        GameObject[,] variations = new GameObject[4, 2];
        TextMesh street;
        public void Place(Vector2Int key, Vector3 position)
        {
            transform.position = position; name = $"Sector {key.x},{key.y}";
            int hash = CityMath.Hash(key.x, key.y);
            for (int i = 0; i < plots.Length; i++)
            {
                bool house = ((hash >> i) & 3) == 0;
                variations[i, 0].SetActive(!house); variations[i, 1].SetActive(house);
            }
            street.text = $"AV. {key.x + 101}\nCALLE {key.y + 101}";
        }
        public void Build(CityAssets a)
        {
            Shape.Part("Road surface", transform, new Vector3(26, -.15f, 26), new Vector3(64, .3f, 64), a.Asphalt, collider:true);
            Shape.Part("Sidewalk block", transform, new Vector3(32, .1f, 32), new Vector3(48, .2f, 48), a.Pavement, collider:true);
            Shape.Part("Courtyard", transform, new Vector3(32, .21f, 32), new Vector3(18, .04f, 18), a.Grass);
            // Road grid is shared at x/z = multiples of 64. Markings leave the junction clear.
            for (int i = 0; i < 8; i++)
            {
                float p = 12 + i * 6;
                Shape.Part("Lane line N", transform, new Vector3(-.12f, .012f, p), new Vector3(.11f, .012f, 3), a.Yellow);
                Shape.Part("Lane line E", transform, new Vector3(p, .012f, -.12f), new Vector3(3, .012f, .11f), a.Yellow);
                Shape.Part("Crosswalk N", transform, new Vector3(-4.5f + i * 1.3f, .018f, 8), new Vector3(.65f, .015f, 2.3f), a.White);
                Shape.Part("Crosswalk E", transform, new Vector3(8, .018f, -4.5f + i * 1.3f), new Vector3(2.3f, .015f, .65f), a.White);
            }
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(i % 2 == 0 ? 18 : 46, .21f, i < 2 ? 18 : 46);
                plots[i] = new GameObject("Lot").transform; plots[i].SetParent(transform, false); plots[i].localPosition = p;
                float h = i == 3 ? 23 : i == 2 ? 20 : 15;
                variations[i, 0] = Shape.Model(a.Buildings[i % a.Buildings.Length], plots[i], Vector3.zero, h, i < 2 ? 180 : 0);
                var buildingCollider = variations[i, 0].AddComponent<BoxCollider>();
                var renderers = variations[i, 0].GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                buildingCollider.center = variations[i, 0].transform.InverseTransformPoint(bounds.center); buildingCollider.size = bounds.size;
                var distant = Shape.Part("Distant building", variations[i, 0].transform, buildingCollider.center, bounds.size, a.DistantBuilding);
                var lod = variations[i, 0].AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(.19f, renderers), new LOD(.008f, new[] { distant.GetComponent<Renderer>() }) });
                lod.RecalculateBounds();
                variations[i, 1] = House(plots[i], a, i);
                for (int j = 0; i < 2 && j < 2; j++)
                {
                    var tree = new Vector3(i % 2 == 0 ? 9.5f : 54.5f, .2f, 20 + j * 22);
                    Shape.Part("Tree trunk", transform, tree + Vector3.up * 1.5f, new Vector3(.25f, 1.5f, .25f), a.Dark, PrimitiveType.Cylinder);
                    Shape.Part("Tree canopy", transform, tree + Vector3.up * 3.7f, new Vector3(3.2f, 4.1f, 3.2f), a.Foliage, PrimitiveType.Sphere);
                }
            }
            var sign = Shape.Part("Street sign", transform, new Vector3(9, 3.3f, 9), new Vector3(2.7f, .75f, .12f), a.Blue);
            street = Shape.Label("Street names", sign.transform, new Vector3(0, 0, -.55f), "AVENIDA", .08f, Color.white, a.Font);
            street.transform.localScale = new Vector3(1 / 2.7f, 1 / .75f, 1 / .12f);
            Signal(a, new Vector3(6.5f, 0, 6.5f), true, 180);
            Signal(a, new Vector3(-6.5f, 0, -6.5f), true, 0);
            Signal(a, new Vector3(6.5f, 0, -6.5f), false, 90);
            Signal(a, new Vector3(-6.5f, 0, 6.5f), false, -90);
            for (int i = 0; i < 3; i++)
            {
                var citizen = new GameObject("Peaton").AddComponent<CityPedestrian>(); citizen.transform.SetParent(transform, false);
                citizen.Build(a, i); citizen.Offset = i * 51;
            }
        }
        static GameObject House(Transform parent, CityAssets a, int seed)
        {
            var root = new GameObject("Casa"); root.transform.SetParent(parent, false);
            Shape.Part("Facade", root.transform, new Vector3(0, 2.3f, 0), new Vector3(10, 4.6f, 10), a.Facades[seed % a.Facades.Length], collider:true);
            Shape.Part("Roof", root.transform, new Vector3(0, 4.7f, 0), new Vector3(10.6f, .3f, 10.6f), a.Red);
            for (int side = 0; side < 2; side++)
            {
                for (int j = -1; j <= 1; j += 2) Shape.Part("Window", root.transform, new Vector3(j * 2.8f, 2.5f, side == 0 ? -5.01f : 5.01f), new Vector3(1.8f, 1.6f, .08f), a.Glass);
                Shape.Part("Door", root.transform, new Vector3(0, 1.1f, side == 0 ? -5.02f : 5.02f), new Vector3(1.3f, 2.2f, .1f), a.Dark);
            }
            return root;
        }
        void Signal(CityAssets a, Vector3 point, bool ns, float angle)
        {
            var go = new GameObject("Semaforo"); go.transform.SetParent(transform, false); go.transform.localPosition = point; go.transform.localRotation = Quaternion.Euler(0, angle, 0);
            Shape.Part("Pole", go.transform, new Vector3(0, 1.5f, 0), new Vector3(.12f, 1.5f, .12f), a.Dark, PrimitiveType.Cylinder);
            Shape.Part("Housing", go.transform, new Vector3(0, 3, 0), new Vector3(.48f, 1.25f, .35f), a.Dark);
            var signal = go.AddComponent<CitySignal>(); signal.NorthSouth = ns;
            signal.Lamps = new Renderer[3];
            for (int i = 0; i < 3; i++) signal.Lamps[i] = Shape.Part("Lamp", go.transform, new Vector3(0, 3.4f - i * .4f, -.19f), new Vector3(.27f, .27f, .08f), a.White, PrimitiveType.Sphere).GetComponent<Renderer>();
        }
    }
    public sealed class CitySignal : MonoBehaviour
    {
        public bool NorthSouth;
        public Renderer[] Lamps;
        MaterialPropertyBlock properties;
        int previous = -1;
        void Update()
        {
            int state = CityMath.SignalGreen(NorthSouth, Time.time) ? 2 : CityMath.SignalAmber(NorthSouth, Time.time) ? 1 : 0;
            if (previous == state || Lamps == null) return;
            previous = state; properties ??= new MaterialPropertyBlock();
            for (int i = 0; i < Lamps.Length; i++)
            {
                Color color = i != state ? new Color(.045f, .05f, .05f) : i == 0 ? Color.red : i == 1 ? new Color(1, .55f, .02f) : Color.green;
                properties.SetColor("_BaseColor", color); properties.SetColor("_EmissionColor", color * 2); Lamps[i].SetPropertyBlock(properties);
            }
        }
    }
    public sealed class CityPedestrian : MonoBehaviour
    {
        public float Offset;
        Transform leftLeg, rightLeg, leftArm, rightArm;
        public void Build(CityAssets a, int index)
        {
            var shirt = a.Facades[index % a.Facades.Length];
            Shape.Part("Torso", transform, new Vector3(0, 1.15f, 0), new Vector3(.4f, .6f, .25f), shirt, PrimitiveType.Capsule);
            Shape.Part("Head", transform, new Vector3(0, 1.62f, 0), new Vector3(.25f, .29f, .25f), a.Skin, PrimitiveType.Sphere);
            Shape.Part("Hair", transform, new Vector3(0, 1.73f, -.025f), new Vector3(.26f, .14f, .25f), a.Dark, PrimitiveType.Sphere);
            leftLeg = Limb("Left leg", -.12f, .89f, .72f, a.Dark); rightLeg = Limb("Right leg", .12f, .89f, .72f, a.Dark);
            leftArm = Limb("Left arm", -.26f, 1.41f, .57f, shirt); rightArm = Limb("Right arm", .26f, 1.41f, .57f, shirt);
        }
        Transform Limb(string label, float x, float y, float length, Material m)
        {
            var joint = new GameObject(label).transform; joint.SetParent(transform, false); joint.localPosition = new Vector3(x, y, 0);
            Shape.Part(label, joint, Vector3.down * length / 2, new Vector3(.13f, length / 2, .15f), m, PrimitiveType.Capsule);
            return joint;
        }
        void Update()
        {
            float distance = Mathf.Repeat(Time.time * 1.15f + Offset, 176);
            int side = Mathf.FloorToInt(distance / 44); float p = distance % 44;
            transform.localPosition = side switch { 0 => new Vector3(10 + p, .2f, 10), 1 => new Vector3(54, .2f, 10 + p), 2 => new Vector3(54 - p, .2f, 54), _ => new Vector3(10, .2f, 54 - p) };
            transform.localRotation = Quaternion.Euler(0, 90 - side * 90, 0);
            float swing = Mathf.Sin(Time.time * 7 + Offset) * 27;
            leftLeg.localRotation = Quaternion.Euler(swing, 0, 0); rightLeg.localRotation = Quaternion.Euler(-swing, 0, 0);
            leftArm.localRotation = Quaternion.Euler(-swing, 0, -7); rightArm.localRotation = Quaternion.Euler(swing, 0, 7);
        }
    }
    public sealed class CityTraffic : MonoBehaviour
    {
        public EndlessCity City;
        public bool NorthSouth;
        public int Sign = 1;
        public float CruiseSpeed = 6;
        Rigidbody body;
        float speed;
        void Start() { body = GetComponent<Rigidbody>(); }
        public void Shift(Vector3 shift) { if (body != null) body.position -= shift; else transform.position -= shift; }
        void FixedUpdate()
        {
            Vector3 p = body.position;
            if (Vector3.Distance(p, City.Taxi.position) > 160)
            {
                var taxi = City.Taxi.position;
                float cross = Mathf.Round((NorthSouth ? taxi.x : taxi.z) / 64) * 64 + Sign * (NorthSouth ? 3 : -3);
                p = NorthSouth ? new Vector3(cross, 0, taxi.z - Sign * 140) : new Vector3(taxi.x - Sign * 140, 0, cross);
            }
            float along = NorthSouth ? p.z : p.x;
            float distance = Mathf.Repeat(-Sign * along, 64);
            bool stop = !CityMath.SignalGreen(NorthSouth, Time.time) && distance > 6 && distance < 17;
            Vector3 forward = NorthSouth ? Vector3.forward * Sign : Vector3.right * Sign;
            if (Physics.Raycast(p + Vector3.up * .6f + forward * 2.5f, forward, out var hit, 8) && hit.rigidbody != body) stop = true;
            speed = Mathf.MoveTowards(speed, stop ? 0 : CruiseSpeed, (stop ? 8 : 2) * Time.fixedDeltaTime);
            body.MovePosition(p + forward * (speed * Time.fixedDeltaTime));
        }
    }
}
