using System.Collections.Generic;
using UnityEngine;
using TaxiVR.Gameplay;

namespace TaxiVR.Playable
{
    /// <summary>Utillaje de decoracion urbana. Dura es lo que aguanta el impacto (farolas, pivotes, arboles,
    /// vallas) y blanda es lo que el taxi puede tirar y arrastrar con fisicas reales (contenedores, conos,
    /// cajas). La blanda lleva rigidbody con masa creible para que empujarla se note pero no frene al coche.</summary>
    public static class CityProps
    {
        /// <summary>Recicla la decoracion blanda: si el taxi la tira lejos o cae fuera del mundo, vuelve a su
        /// sitio. Sin esto una calle jugada dos veces acabaria cubierta de objetos y de cuerpos dormidos.</summary>
        public sealed class SoftProp : MonoBehaviour
        {
            public Vector3 Home;
            public Quaternion HomeRotation;
            public float Range = 45f;
            public float Drift = .35f;

            Rigidbody body;

            void Awake() { body = GetComponent<Rigidbody>(); }

            /// <summary>Fija el destino de retorno. Se llama al colocar la pieza, no al construirse, porque la
            /// ciudad reutiliza los sectores y la pieza cambia de calle.</summary>
            public void Anchor()
            {
                Home = transform.position;
                HomeRotation = transform.rotation;
            }

            void FixedUpdate()
            {
                if (!gameObject.activeInHierarchy) return;
                bool lost = (transform.position - Home).sqrMagnitude > Range * Range || transform.position.y < -1f;
                if (!lost) return;
                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                transform.SetPositionAndRotation(Home, HomeRotation);
            }
        }

        /// <summary>Material de farol encendido. Se comparte entre todas las farolas: crear uno por pieza
        /// multiplicaria los materiales sin limite, porque el streaming reconstruye las manzanas y nada los
        /// liberaria despues.</summary>
        static readonly Dictionary<Material, Material> emissive = new();

        static Material Emissive(Material source)
        {
            if (source == null) return null;
            if (emissive.TryGetValue(source, out var cached) && cached != null) return cached;
            var glow = new Material(source);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(1f, .86f, .55f) * 2.2f);
            glow.color = new Color(1f, .92f, .7f);
            glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            emissive[source] = glow;
            return glow;
        }

        public static GameObject Lamp(Transform parent, Vector3 position, float yaw, CityAssets assets)
        {
            var root = new GameObject("Farola");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            Shape.Part("Poste", root.transform, new Vector3(0, 2.2f, 0), new Vector3(.14f, 4.4f, .14f), assets.Dark, PrimitiveType.Cylinder, true);
            Shape.Part("Brazo", root.transform, new Vector3(0, 4.35f, .55f), new Vector3(.1f, .1f, 1.1f), assets.Dark);
            var head = Shape.Part("Farol", root.transform, new Vector3(0, 4.2f, 1.08f), new Vector3(.42f, .18f, .3f), assets.Yellow);
            head.transform.localRotation = Quaternion.Euler(12, 0, 0);
            var lit = Emissive(assets.Dark);
            if (lit != null) head.GetComponent<Renderer>().sharedMaterial = lit;
            Mark(root, PenaltyKind.Barrier);
            return root;
        }

        public static GameObject Bench(Transform parent, Vector3 position, float yaw, CityAssets assets)
        {
            var root = new GameObject("Banco");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            Shape.Part("Asiento", root.transform, new Vector3(0, .45f, 0), new Vector3(.52f, .07f, 1.8f), assets.Dark, PrimitiveType.Cube, true);
            Shape.Part("Respaldo", root.transform, new Vector3(.22f, .72f, 0), new Vector3(.07f, .5f, 1.8f), assets.Dark);
            Shape.Part("Pata izquierda", root.transform, new Vector3(0, .22f, -.7f), new Vector3(.44f, .44f, .07f), assets.Dark);
            Shape.Part("Pata derecha", root.transform, new Vector3(0, .22f, .7f), new Vector3(.44f, .44f, .07f), assets.Dark);
            Mark(root, PenaltyKind.Barrier);
            return root;
        }

        public static GameObject Hydrant(Transform parent, Vector3 position, CityAssets assets)
        {
            var root = new GameObject("Boca de riego");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            Shape.Part("Cuerpo", root.transform, new Vector3(0, .38f, 0), new Vector3(.22f, .76f, .22f), assets.Red, PrimitiveType.Cylinder, true);
            Shape.Part("Tapa", root.transform, new Vector3(0, .79f, 0), new Vector3(.26f, .08f, .26f), assets.Yellow, PrimitiveType.Cylinder);
            Shape.Part("Boca", root.transform, new Vector3(.15f, .58f, 0), new Vector3(.16f, .16f, .16f), assets.Red, PrimitiveType.Sphere);
            Mark(root, PenaltyKind.Barrier);
            return root;
        }

        public static GameObject Sign(Transform parent, Vector3 position, float yaw, CityAssets assets, Material plate)
        {
            var root = new GameObject("Senal");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            Shape.Part("Poste", root.transform, new Vector3(0, 1.15f, 0), new Vector3(.08f, 2.3f, .08f), assets.Dark, PrimitiveType.Cylinder, true);
            Shape.Part("Placa", root.transform, new Vector3(0, 2.05f, .04f), new Vector3(.66f, .5f, .04f), plate);
            Mark(root, PenaltyKind.Barrier);
            return root;
        }

        /// <summary>Semaforo con tres lamparas. El estado lo gobierna <see cref="CitySignal"/>, que lee el mismo
        /// reloj que los coches civiles y que la penalizacion por rojo, asi que no pueden discrepar.</summary>
        public static GameObject TrafficLight(Transform parent, Vector3 position, float yaw, bool northSouth, CityAssets assets)
        {
            var root = new GameObject(northSouth ? "Semaforo NS" : "Semaforo EO");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            Shape.Part("Poste", root.transform, new Vector3(0, 1.7f, 0), new Vector3(.12f, 3.4f, .12f), assets.Dark, PrimitiveType.Cylinder, true);
            Shape.Part("Brazo", root.transform, new Vector3(0, 3.34f, .5f), new Vector3(.09f, .09f, 1f), assets.Dark);
            var box = Shape.Part("Caja", root.transform, new Vector3(0, 3.2f, .96f), new Vector3(.19f, .56f, .17f), assets.Dark);
            var signal = root.AddComponent<CitySignal>();
            signal.NorthSouth = northSouth;
            signal.Lamps = new Renderer[3];
            for (int i = 0; i < 3; i++)
                signal.Lamps[i] = Shape.Part("Luz", box.transform, new Vector3(0, .18f - i * .18f, -.55f), new Vector3(.7f, .22f, .6f), assets.Dark)
                    .GetComponent<Renderer>();
            return root;
        }

        public static GameObject Barrier(Transform parent, Vector3 position, float yaw, CityAssets assets)
        {
            var root = new GameObject("Valla");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            Shape.Part("Larguero", root.transform, new Vector3(0, .82f, 0), new Vector3(.08f, .16f, 2.4f), assets.White, PrimitiveType.Cube, true);
            Shape.Part("Larguero bajo", root.transform, new Vector3(0, .5f, 0), new Vector3(.06f, .12f, 2.4f), assets.White);
            Shape.Part("Pie izquierdo", root.transform, new Vector3(0, .4f, -1.1f), new Vector3(.5f, .8f, .08f), assets.Dark);
            Shape.Part("Pie derecho", root.transform, new Vector3(0, .4f, 1.1f), new Vector3(.5f, .8f, .08f), assets.Dark);
            return root;
        }

        public static GameObject Bin(Transform parent, Vector3 position, float yaw, CityAssets assets)
        {
            var root = SoftRoot("Contenedor", parent, position, yaw, 15f);
            Shape.Part("Cuerpo", root.transform, new Vector3(0, .45f, 0), new Vector3(.6f, .9f, .52f), assets.Dark, PrimitiveType.Cylinder);
            Shape.Part("Tapa", root.transform, new Vector3(0, .93f, 0), new Vector3(.66f, .07f, .58f), assets.Dark, PrimitiveType.Cylinder);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, .47f, 0);
            collider.size = new Vector3(.62f, .95f, .54f);
            return root;
        }

        public static GameObject Cone(Transform parent, Vector3 position, CityAssets assets)
        {
            var root = SoftRoot("Cono", parent, position, 0f, 3.5f);
            Shape.Part("Cuerpo", root.transform, new Vector3(0, .3f, 0), new Vector3(.42f, .6f, .42f), assets.Red, PrimitiveType.Cylinder);
            Shape.Part("Franja", root.transform, new Vector3(0, .38f, 0), new Vector3(.34f, .1f, .34f), assets.White, PrimitiveType.Cylinder);
            Shape.Part("Base", root.transform, new Vector3(0, .03f, 0), new Vector3(.48f, .06f, .48f), assets.Red, PrimitiveType.Cube);
            var collider = root.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0, .3f, 0);
            collider.height = .64f;
            collider.radius = .21f;
            return root;
        }

        public static GameObject Crate(Transform parent, Vector3 position, float yaw, CityAssets assets)
        {
            var root = SoftRoot("Caja", parent, position, yaw, 11f);
            Shape.Part("Caja", root.transform, new Vector3(0, .28f, 0), new Vector3(.56f, .56f, .56f), assets.Skin);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, .28f, 0);
            collider.size = new Vector3(.58f, .58f, .58f);
            return root;
        }

        public static GameObject Barrel(Transform parent, Vector3 position, CityAssets assets)
        {
            var root = SoftRoot("Bidon", parent, position, 0f, 22f);
            Shape.Part("Cuerpo", root.transform, new Vector3(0, .45f, 0), new Vector3(.56f, .9f, .56f), assets.Blue, PrimitiveType.Cylinder);
            Shape.Part("Aro", root.transform, new Vector3(0, .62f, 0), new Vector3(.6f, .06f, .6f), assets.Dark, PrimitiveType.Cylinder);
            var collider = root.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0, .45f, 0);
            collider.height = .92f;
            collider.radius = .29f;
            return root;
        }

        /// <summary>Genero del puesto. Masa minima para que el golpe lo disperse sin que se note en el coche.</summary>
        public static GameObject Produce(Transform parent, Vector3 position, GameObject model, CityAssets assets)
        {
            var root = SoftRoot("Genero", parent, position, 0f, .6f);
            if (model != null)
            {
                var visual = Shape.Model(model, root.transform, Vector3.zero, .22f);
                visual.name = model.name;
            }
            Shape.Part("Suelo", root.transform, new Vector3(0, .03f, 0), new Vector3(.18f, .06f, .18f), assets.Red);
            var collider = root.AddComponent<SphereCollider>();
            collider.center = new Vector3(0, .12f, 0);
            collider.radius = .15f;
            return root;
        }

        /// <summary>Pieza del kit a su tamano real, apoyada en la rasante y con colisionador. Las piezas del kit
        /// estan modeladas para la acera de 4 m, asi que escalarlas las desentonaria: se colocan tal cual.</summary>
        public static GameObject Fixed(GameObject prefab, Transform parent, Vector3 localPosition, float yaw)
        {
            var root = new GameObject(prefab.name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var model = Object.Instantiate(prefab, root.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            if (model.GetComponent<Collider>() == null)
            {
                var bounds = Shape.CachedBounds(prefab);
                var box = model.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
            }
            return root;
        }

        /// <summary>Pieza del kit suelta: misma colocacion que <see cref="Fixed"/> pero con rigidbody, que es lo
        /// que la convierte en decoracion que el taxi puede tirar.</summary>
        public static GameObject Loose(GameObject prefab, Transform parent, Vector3 localPosition, float yaw, float mass)
        {
            var root = Fixed(prefab, parent, localPosition, yaw);
            var body = root.AddComponent<Rigidbody>();
            body.mass = mass;
            body.linearDamping = .05f;
            body.angularDamping = .35f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // El colisionador cuelga del modelo, y el rigidbody del envoltorio: sin esto el centro de masas
            // quedaria en el pivote del kit y la pieza giraria de forma rara al empujarla.
            var collider = root.GetComponentInChildren<Collider>();
            if (collider != null && collider.transform != root.transform)
            {
                body.centerOfMass = root.transform.InverseTransformPoint(collider.bounds.center);
            }
            root.AddComponent<SoftProp>().Anchor();
            Mark(root, PenaltyKind.TrashOrProp);
            return root;
        }

        static GameObject SoftRoot(string name, Transform parent, Vector3 position, float yaw, float mass)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            var body = root.AddComponent<Rigidbody>();
            body.mass = mass;
            body.linearDamping = .05f;
            body.angularDamping = .35f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<SoftProp>().Anchor();
            // La decoracion blanda tambien puntua: tirar un contenedor cuesta menos que arrancar una farola.
            Mark(root, PenaltyKind.TrashOrProp);
            return root;
        }

        /// <summary>Marca la pieza con la penalizacion que corresponde al chocar con ella.</summary>
        public static void Mark(GameObject root, PenaltyKind kind)
        {
            var marker = root.GetComponent<PenaltyMarker>();
            if (marker == null) marker = root.AddComponent<PenaltyMarker>();
            marker.Kind = kind;
        }
    }
}
