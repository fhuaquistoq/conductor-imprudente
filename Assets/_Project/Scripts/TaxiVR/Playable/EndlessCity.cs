using System.Collections.Generic;
using UnityEngine;
using TaxiVR.City;

namespace TaxiVR.Playable
{
    /// <summary>Objetos que viven en el mundo logico y deben seguir al taxi cuando el origen flotante salta.
    /// Sin esto, un pasajero o un coche de policia se quedaria atras al cruzar los 1024 metros.</summary>
    public interface ICityShiftable
    {
        void Shift(Vector3 delta);
    }

    /// <summary>Ciudad infinita por streaming. La rejilla de <see cref="CityGrid"/> dice que hay en cada sector
    /// y <see cref="CitySectorBuilder"/> lo levanta cuando toca, de modo que no se guarda el mundo: se deduce.
    ///
    /// El sector se monta en dos tiempos, uno por fotograma, para que cruzar de manzana no cueste un tiron: primero
    /// el suelo y el colisionador (que es lo que impide caerse) y despues las casas y la decoracion. El suelo de
    /// todo el anillo se levanta siempre, incluso lejos, porque una calzada que no esta dibujada es un agujero por
    /// el que el jugador se cae.</summary>
    public sealed class EndlessCity : MonoBehaviour
    {
        public CityAssets Assets;
        public Transform Taxi;
        public const int Radius = 3;

        /// <summary>Altura por debajo de la cual se da por hecho que el taxi se ha caido del mundo.</summary>
        const float FallLimit = -2f;

        public Vector2Int OriginSector { get; private set; }
        public int LoadedSectors => sectors.Count;
        public int DetailedSectors { get; private set; }
        public int PendingSectors => pending.Count;

        readonly Dictionary<Vector2Int, Sector> sectors = new();
        readonly Queue<Vector2Int> pending = new();
        readonly HashSet<Vector2Int> wanted = new();
        readonly HashSet<Vector2Int> queued = new();
        readonly List<Vector2Int> stale = new();
        readonly List<Vector2Int> ordered = new();
        Vector2Int last = new(int.MinValue, int.MinValue);
        TaxiDrive drive;

        CityCatalog Catalog => Assets == null ? null : Assets.Catalog;
        int LoadRadius => Catalog == null ? Radius : Mathf.Max(1, Catalog.LoadRadius);
        int DetailRadius => Catalog == null ? 1 : Mathf.Max(0, Catalog.DetailRadius);

        public Vector3 AbsolutePosition => Taxi.position + new Vector3(OriginSector.x * CityMath.Block, 0, OriginSector.y * CityMath.Block);
        public Vector3 LocalPosition(Vector2Int node) => new((node.x - OriginSector.x) * CityMath.Block, 0, (node.y - OriginSector.y) * CityMath.Block);

        Vector2Int CenterSector() => new(CityMath.Sector(Taxi.position.x) + OriginSector.x, CityMath.Sector(Taxi.position.z) + OriginSector.y);

        void Start()
        {
            // El taxi arranca sobre la calzada, asi que el primer reparto se hace con la posicion inicial ya
            // conocida y el suelo aparece antes del primer fotograma visible.
            Plan();
        }

        void Update()
        {
            if (Taxi == null) return;
            if (Mathf.Abs(Taxi.position.x) > 1024 || Mathf.Abs(Taxi.position.z) > 1024) Rebase();
            var center = CenterSector();
            // Si el catalogo de arte llega despues del primer reparto, el anillo esta vacio: hay que insistir.
            if (center != last || sectors.Count == 0) { last = center; Plan(); }
            BuildBudget(center);
            Rescue();
        }

        /// <summary>Levanta piezas mientras quede presupuesto de tiempo, en vez de una sola por fotograma: con
        /// una por frame el anillo 7x7 tarda mas de cincuenta fotogramas en aparecer.</summary>
        void BuildBudget(Vector2Int center)
        {
            float deadline = Time.realtimeSinceStartup + .002f;
            do { BuildOne(center); }
            while (pending.Count > 0 && Time.realtimeSinceStartup < deadline);
        }

        /// <summary>Reparte el anillo de sectores alrededor del taxi: suelta los que quedan fuera y encola los
        /// que faltan, de mas cerca a mas lejos, para que el mundo crezca desde el jugador hacia fuera. Un
        /// sector que ya tiene suelo pero aun no detalle se vuelve a encolar cuando el taxi se acerca.</summary>
        void Plan()
        {
            if (Taxi == null || Assets == null) return;
            var center = CenterSector();
            wanted.Clear();
            for (int x = -LoadRadius; x <= LoadRadius; x++)
                for (int z = -LoadRadius; z <= LoadRadius; z++) wanted.Add(center + new Vector2Int(x, z));

            stale.Clear();
            foreach (var pair in sectors) if (!wanted.Contains(pair.Key)) stale.Add(pair.Key);
            foreach (var key in stale) Release(key);

            ordered.Clear();
            ordered.AddRange(wanted);
            ordered.Sort((a, b) => (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude));
            foreach (var key in ordered)
            {
                if (!sectors.TryGetValue(key, out var sector)) { Enqueue(key); continue; }
                if (!sector.Detailed && DetailRange(key, center)) Enqueue(key);
            }
        }

        void Enqueue(Vector2Int key) { if (queued.Add(key)) pending.Enqueue(key); }

        /// <summary>Monta una pieza: el suelo de un sector nuevo, o su detalle si ya tiene suelo y el taxi esta
        /// lo bastante cerca. Nunca las dos cosas en el mismo fotograma, para que cruzar de manzana no cueste
        /// un tiron.</summary>
        void BuildOne(Vector2Int center)
        {
            if (pending.Count == 0) return;
            var key = pending.Dequeue();
            queued.Remove(key);
            if (!wanted.Contains(key)) return;

            if (!sectors.TryGetValue(key, out var sector))
            {
                sector = new Sector { Key = key, Root = new GameObject($"Sector {key.x},{key.y}") };
                sector.Root.transform.SetParent(transform, false);
                sector.Root.transform.localPosition = new Vector3((key.x - OriginSector.x) * CityMath.Block, 0, (key.y - OriginSector.y) * CityMath.Block);
                sector.Ground = CitySectorBuilder.BuildGround(Assets, key, sector.Root.transform);
                sector.Far = CitySectorBuilder.BuildFar(Assets, key, sector.Root.transform);
                sectors.Add(key, sector);
                // El detalle espera a otro fotograma: montar suelo y casas a la vez es justo el pico que se
                // quiere evitar.
                if (DetailRange(key, center)) Enqueue(key);
                return;
            }

            if (sector.Detailed || !DetailRange(key, center) || sector.Ground == null) return;
            sector.Detail = CitySectorBuilder.BuildDetail(Assets, key, sector.Root.transform);
            sector.Detailed = true;
            DetailedSectors++;
            // La silueta barata solo tiene sentido mientras la manzana real no esta montada.
            if (sector.Far != null) sector.Far.SetActive(false);
        }

        bool DetailRange(Vector2Int key, Vector2Int center) =>
            Mathf.Abs(key.x - center.x) <= DetailRadius && Mathf.Abs(key.y - center.y) <= DetailRadius;

        /// <summary>Suelta un sector y sus mallas. Las mallas se crean en tiempo de ejecucion, asi que destruir
        /// el objeto no basta: sin liberarlas explicitamente el streaming terminaria por agotar la memoria.</summary>
        void Release(Vector2Int key)
        {
            if (!sectors.TryGetValue(key, out var sector)) return;
            sector.Dispose();
            sectors.Remove(key);
            if (sector.Detailed) DetailedSectors--;
        }

        /// <summary>Ultimo recurso: si el taxi aparece por debajo del mundo (un sector que aun no se ha montado,
        /// un salto del origen flotante a media carga), se le devuelve a la calzada mas proxima. Es preferible a
        /// dejar al jugador cayendo sin fin fuera de la ciudad.</summary>
        void Rescue()
        {
            if (Taxi.position.y > FallLimit) return;
            drive ??= Taxi.GetComponent<TaxiDrive>();
            if (drive != null) drive.ResetToRoad();
            else Taxi.position = new Vector3(Mathf.Round(Taxi.position.x / CityMath.Block) * CityMath.Block + 3, .5f, Mathf.Round(Taxi.position.z / CityMath.Block) * CityMath.Block + 22);
        }

        void Rebase()
        {
            var delta = new Vector2Int(CityMath.Sector(Taxi.position.x), CityMath.Sector(Taxi.position.z));
            var shift = new Vector3(delta.x * CityMath.Block, 0, delta.y * CityMath.Block);
            OriginSector += delta;
            var body = Taxi.GetComponent<Rigidbody>();
            body.position -= shift;
            foreach (var sector in sectors.Values)
                if (sector.Root != null) sector.Root.transform.position -= shift;
            foreach (var shiftable in GetComponentsInChildren<ICityShiftable>(true)) shiftable.Shift(shift);
            Physics.SyncTransforms();
            last = new(int.MinValue, int.MinValue);
        }

        /// <summary>Un sector montado, con sus tres grupos: suelo (siempre), silueta lejana (mientras no hay
        /// detalle) y detalle (casas y decoracion, solo cerca).</summary>
        sealed class Sector
        {
            public Vector2Int Key;
            public GameObject Root, Ground, Far, Detail;
            public bool Detailed;

            public void Dispose()
            {
                if (Root != null)
                {
                    foreach (var filter in Root.GetComponentsInChildren<MeshFilter>(true))
                        if (filter.sharedMesh != null) Object.Destroy(filter.sharedMesh);
                    Object.Destroy(Root);
                }
                Root = Ground = Far = Detail = null;
            }
        }
    }

    /// <summary>Semaforo. El estado sale del mismo reloj que usan los coches civiles y la penalizacion por rojo,
    /// asi que lo que se ve y lo que se sanciona no pueden discrepar.</summary>
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
            previous = state;
            properties ??= new MaterialPropertyBlock();
            for (int i = 0; i < Lamps.Length; i++)
            {
                if (Lamps[i] == null) continue;
                Color color = i != state ? new Color(.045f, .05f, .05f) : i == 0 ? Color.red : i == 1 ? new Color(1, .55f, .02f) : Color.green;
                properties.SetColor("_BaseColor", color);
                properties.SetColor("_EmissionColor", color * 2);
                Lamps[i].SetPropertyBlock(properties);
            }
        }
    }
}
