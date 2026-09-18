using System.Collections.Generic;
using UnityEngine;
using TaxiVR.City;

namespace TaxiVR.Playable
{
    /// <summary>Catalogo de arte del juego. Todo lo que el constructor de ciudad necesita para levantar un
    /// sector sin tocar codigo: edificios, arboles, decoracion dura y blanda, y los materiales compartidos.</summary>
    public sealed class CityAssets : ScriptableObject
    {
        public CityCatalog Catalog;
        public GameObject[] Buildings;
        public GameObject[] Cars;
        public GameObject Taxi;
        public GameObject PoliceCar;
        public GameObject PassengerModel;
        public GameObject PedestrianModel;

        [Header("Peatones")]
        [Tooltip("Cuerpos completos que se reparten entre los peatones para que no parezcan el mismo.")]
        public GameObject[] PedestrianBodies;
        [Tooltip("Peluqueros y barbas del mismo esqueleto, para multiplicar la variedad sin mas geometria.")]
        public GameObject[] PedestrianHair;
        [Tooltip("Controlador con el ciclo de caminata. Si falta, los peatones andan con la marcha procedural.")]
        public RuntimeAnimatorController PedestrianWalk;

        [Header("Decoracion")]
        [Tooltip("Arboles. Son decoracion dura: el taxi no los mueve.")]
        public GameObject[] Trees;
        [Tooltip("Banos, vallas, pivotes, jardineras... Tambien duros.")]
        public GameObject[] HardProps;
        [Tooltip("Contenedores, conos y cajas: el taxi los tira y los arrastra con fisicas.")]
        public GameObject[] SoftProps;
        [Tooltip("Genero del puesto de calle, tirado por el suelo cuando el coche lo atropella.")]
        public GameObject[] MarketProps;

        public Material Asphalt, Pavement, Dark, Yellow, White, Glass, Grass, Foliage, Skin, Red, Blue;
        public Material[] Facades;
        public Material DistantBuilding;
        public Shader UnlitShader;
        public Font Font;
    }

    public static class Shape
    {
        static readonly Dictionary<GameObject, Bounds> boundsCache = new();

        public static GameObject Part(string name, Transform parent, Vector3 position, Vector3 size, Material material, PrimitiveType type = PrimitiveType.Cube, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            // El collider se desactiva, nunca se destruye: destruirlo aqui reindexa los componentes
            // del objeto y corrompe la serializacion de la escena horneada.
            if (!collider)
            {
                var component = go.GetComponent<Collider>();
                if (component != null) component.enabled = false;
            }
            return go;
        }

        public static TextMesh Label(string name, Transform parent, Vector3 position, string text, float size, Color color, Font font)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
            var label = go.AddComponent<TextMesh>(); label.text = text; label.font = font; label.fontSize = 64;
            label.characterSize = size; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = color;
            if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return label;
        }

        /// <summary>Escala un modelo a una altura y lo apoya sobre el origen del pivote, centrado en planta. La
        /// medida se toma con el pivote sin girar: girarlo antes mezclaria espacio local y de mundo y el modelo
        /// saldria descolgado en cuanto la pieza no estuviera en el origen del mundo.</summary>
        public static GameObject Model(GameObject prefab, Transform parent, Vector3 position, float height, float yaw = 0)
        {
            var pivot = new GameObject(prefab.name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = position;
            var obj = Object.Instantiate(prefab, pivot.transform);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = Quaternion.identity;
            var bounds = RenderBounds(obj);
            float scale = height / Mathf.Max(.01f, bounds.size.y);
            obj.transform.localScale = Vector3.one * scale;
            obj.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
            pivot.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            return pivot;
        }

        /// <summary>Encaja un modelo en una parcela concreta: lo escala para que su huella no pase del tamano
        /// pedido, lo apoya sobre la rasante y lo centra en la parcela. La medida se toma antes de girar, de modo
        /// que la cuenta es exacta para cualquier giro de 90 grados.</summary>
        public static GameObject Fit(GameObject prefab, Transform parent, Vector3 centre, Vector2 footprint, float yaw, float fill = .92f, bool collider = true)
        {
            var bounds = CachedBounds(prefab);
            bool swapped = Mathf.Abs(Mathf.DeltaAngle(0, yaw)) > 45f && Mathf.Abs(Mathf.DeltaAngle(0, yaw)) < 135f;
            float limitX = swapped ? footprint.y : footprint.x;
            float limitZ = swapped ? footprint.x : footprint.y;
            float scale = 1f;
            if (bounds.size.x > .01f && bounds.size.z > .01f)
                scale = Mathf.Min(limitX / bounds.size.x, limitZ / bounds.size.z) * fill;

            var pivot = new GameObject(prefab.name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = centre;
            pivot.transform.localRotation = Quaternion.Euler(0, yaw, 0);

            var obj = Object.Instantiate(prefab, pivot.transform);
            obj.transform.localScale = Vector3.one * scale;
            obj.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
            if (collider)
            {
                // El collider cuelga del modelo y no del pivote para que la escala del propio modelo lo mida.
                var box = obj.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
            }
            return pivot;
        }

        public static Bounds RenderBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        /// <summary>Medida de un prefab, cacheada por prefab. Medirla exige instanciar una copia, y la misma casa
        /// se repite por toda la ciudad: sin cache, el streaming gastaria mas en medir que en dibujar.</summary>
        public static Bounds CachedBounds(GameObject prefab)
        {
            if (boundsCache.TryGetValue(prefab, out var cached)) return cached;
            var probe = Object.Instantiate(prefab);
            var bounds = RenderBounds(probe);
            Object.Destroy(probe);
            boundsCache[prefab] = bounds;
            return bounds;
        }

        /// <summary>Busca un descendiente por nombre. El kit nombra sus piezas en ingles y de forma estable.</summary>
        public static Transform Find(Transform root, string name)
        {
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true)) if (candidate.name == name) return candidate;
            return null;
        }

        /// <summary>Libera la cache de medidas al cerrar el mundo: guarda un Bounds por prefab medido.</summary>
        public static void ClearCache() => boundsCache.Clear();
    }

    /// <summary>Avisos de arte que falta, una sola vez por clave: la ciudad se levanta sector a sector y
    /// repetir el mismo Debug.LogWarning por cada manzana inundaria la consola.</summary>
    public static class ArtLog
    {
        static readonly HashSet<string> warned = new();

        public static void WarnOnce(string key, string message)
        {
            if (!warned.Add(key)) return;
            Debug.LogWarning(message);
        }
    }
}
