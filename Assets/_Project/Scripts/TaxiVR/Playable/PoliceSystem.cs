using System.Collections.Generic;
using UnityEngine;
using TaxiVR.Gameplay;

namespace TaxiVR.Playable
{
    /// <summary>Desenlace de la persecucion. El director solo mira si ha dejado de ser None, asi que la policia
    /// puede resolver la partida sin tener que avisar a nadie.</summary>
    public enum PoliceVerdict { None, Caught, Escaped }

    /// <summary>Persecucion policial: aparecer fuera de la vista, cercar al taxi y decidir el desenlace. Vive
    /// aparte del director porque su unico asunto es el cerco: recibe el taxi, la ciudad y el grafo, y no toca ni
    /// el viaje ni el marcador.
    ///
    /// Las patrullas se crean una sola vez y se reciclan. La ciudad infinita reaprovecha sectores, y destruir y
    /// volver a instanciar modelos en cada persecucion costaria mas que reactivarlos, ademas de ensuciar la
    /// jerarquia.
    ///
    /// El componente tiene que colgar de la ciudad para que EndlessCity le mande Shift cuando el origen flotante
    /// salta; si no, la policia se quedaria atras al cruzar los 1024 metros.</summary>
    public sealed class PoliceSystem : MonoBehaviour, ICityShiftable
    {
        /// <summary>Ventaja de velocidad sobre el taxi. Es corta a proposito: con el taxi lanzado la persecucion
        /// se decide por trazada y no por potencia, y asi la huida sigue siendo posible.</summary>
        const float SpeedLead = 4f;

        /// <summary>Tope de los coches. Por encima del taxi para que no se queden clavados, pero lejos de una
        /// velocidad que haga imposible verlos venir.</summary>
        const float MaxSpeed = 24f;

        /// <summary>Un perseguidor y dos interceptores: con dos coches ya se cumple la captura y el tercero corta
        /// por el lado contrario.</summary>
        const int CarCount = 3;

        /// <summary>Altura del modelo de policia, la misma que la del resto de coches de la ciudad.</summary>
        const float CarHeight = 1.7f;

        /// <summary>Separacion del carril respecto al eje de la calle: el mismo desplazamiento lateral que ya usa
        /// el trafico de la ciudad, para que las patrullas circulen por donde circulan los demas.</summary>
        const float Lane = 3f;

        /// <summary>Distancia al cruce por debajo de la cual se permite girar. Un giro a mitad de cuadra sacaria
        /// al coche del asfalto y delataria que no sigue la rejilla.</summary>
        const float TurnTolerance = 4f;

        /// <summary>Margen del cambio de eje. Sin el, dos recorridos parecidos hacen que el coche dude entre ejes
        /// cada cuadro y avance a tirones.</summary>
        const float AxisMargin = 2f;

        /// <summary>Distancia a la que se abandona la trazada por carriles y se ataca en linea recta. Es la unica
        /// forma de cerrar el cerco hasta los cinco metros que pide la captura, y se lee como una embestida.</summary>
        const float PounceDistance = 16f;

        /// <summary>Puntos de corte de los interceptores a velocidad de crucero: delante del taxi y a los lados,
        /// para que lleguen antes que el a la salida de la manzana en lugar de limitarse a seguirle. Se encogen
        /// con la velocidad del taxi, asi que no son distancias fijas.</summary>
        const float CutAhead = 60f;
        const float CutSide = 40f;

        /// <summary>La aparicion tiene que sorprender: ni encima del taxi ni a un kilometro.</summary>
        const float MinSpawnDistance = 100f;
        const float MaxSpawnDistance = 150f;
        const int MaxSpawnAttempts = 32;

        /// <summary>Caja con la que se comprueba si un punto de aparicion cae dentro del encuadre. Es holgada a
        /// proposito: mas vale descartar un punto valido que dejar asomar un coche por el borde.</summary>
        static readonly Vector3 CarBounds = new(4f, 3f, 8f);

        const float CaptureKmh = 1f;
        const float CaptureSeconds = 4f;
        const float CaptureRadius = 5f;
        const int CaptureCars = 2;

        const float EscapeRadius = 120f;
        const float EscapeRoadMetres = 200f;
        const float EscapeSeconds = 45f;

        /// <summary>Cada cuanto se mide la distancia por carretera. La busqueda en el grafo reserva memoria, asi
        /// que no se puede consultar en cada cuadro.</summary>
        const float RoadCheckInterval = .5f;

        /// <summary>Parpadeos por segundo de la barra luminosa.</summary>
        const float FlashRate = 4f;

        /// <summary>Masa de los coches. El movimiento no depende de ella, pero un cuerpo con masa creible se
        /// comporta como se espera si algun dia vuelve a empujar o ser empujado.</summary>
        const float CarMass = 1500f;

        public EndlessCity City;
        public CityAssets Assets;
        public TaxiDrive Drive;
        [System.NonSerialized] public Gameplay.CityGraph Graph;

        /// <summary>Hay persecucion en curso. El director lo usa para saber si debe consultar el desenlace.</summary>
        public bool Active { get; private set; }

        /// <summary>Desenlace ya decidido, o None mientras la persecucion sigue abierta. Se congela en cuanto se
        /// resuelve: el director cierra la partida en el mismo cuadro y no tiene sentido seguir midiendo.</summary>
        public PoliceVerdict Verdict { get; private set; }

        PoliceCar[] cars;
        Plane[] frustum;
        float captureTimer, escapeTimer, roadTimer, roadDistance, nearestMetres;

        /// <summary>Papel dentro del cerco. El perseguidor va a por el taxi y los dos interceptores se reparten los
        /// lados para cortarle la salida, que es lo que convierte una persecucion en un cerco.</summary>
        enum PoliceRole { Pursuer, Interceptor }

        /// <summary>Un coche de policia y lo poco que hay que recordar de el entre cuadros: su cuerpo, sus dos
        /// focos y por que eje circula. Nada de esto se busca en la jerarquia ni se recalcula.</summary>
        sealed class PoliceCar
        {
            public Rigidbody Body;
            public Renderer Red;
            public Renderer Blue;
            public PoliceRole Role;
            public float Flank;
            public bool AlongX;
            public GameObject Root => Body.gameObject;
            public Vector3 Position => Body.position;
        }

        // ------------------------------------------------------------------ consulta

        /// <summary>Cuantos coches de policia hay a menos de esa distancia del taxi. La usa el propio sistema para
        /// decidir la captura y el HUD para avisar del cerco.</summary>
        public int NearbyCount(float metres)
        {
            int count = 0;
            if (cars == null) return count;
            Vector3 here = TaxiPoint;
            for (int i = 0; i < cars.Length; i++)
            {
                PoliceCar car = cars[i];
                if (!car.Root.activeInHierarchy) continue;
                if (Planar(car.Position - here) < metres) count++;
            }
            return count;
        }

        /// <summary>Distancia al coche mas cercano, infinita cuando no hay ninguno. El HUD la puede mostrar tal
        /// cual, sin comprobar antes si existe la policia.</summary>
        public float NearestDistance => Nearest(TaxiPoint);

        /// <summary>Lanza la persecucion. Los coches ya construidos se reactivan y se recolocan fuera del campo de
        /// vision, de modo que una segunda persecucion no vuelve a instanciar modelos.</summary>
        public void Begin(Vector3 origin)
        {
            if (City == null) return;
            EnsureCars();
            Active = true;
            Verdict = PoliceVerdict.None;
            captureTimer = escapeTimer = roadTimer = 0f;
            roadDistance = 0f;
            for (int i = 0; i < cars.Length; i++) Place(cars[i], origin, i);
        }

        /// <summary>Termina la persecucion y devuelve los coches al fondo. Se apagan en vez de destruirse: la
        /// ciudad se reordena sola y volver a instanciarlos costaria mas que reactivarlos.</summary>
        public void Halt()
        {
            Active = false;
            Verdict = PoliceVerdict.None;
            captureTimer = escapeTimer = roadTimer = 0f;
            roadDistance = 0f;
            if (cars == null) return;
            for (int i = 0; i < cars.Length; i++)
            {
                PoliceCar car = cars[i];
                car.Body.linearVelocity = Vector3.zero;
                car.Body.angularVelocity = Vector3.zero;
                car.Root.SetActive(false);
            }
        }

        /// <summary>Sigue al origen flotante. Los coches no pueden colgar del sector que se recicla, asi que el
        /// desplazamiento se aplica a mano sobre el Rigidbody cuando la ciudad salta de bloque.</summary>
        public void Shift(Vector3 delta)
        {
            if (cars == null) return;
            for (int i = 0; i < cars.Length; i++)
            {
                Rigidbody body = cars[i].Body;
                if (body != null) body.position -= delta;
            }
        }

        // ------------------------------------------------------------------ ciclo de vida

        void Update()
        {
            if (!Active) return;
            Flash(Time.time);
            if (Verdict != PoliceVerdict.None) return;
            nearestMetres = Nearest(TaxiPoint);
            float delta = Time.deltaTime;
            RoadTimer(delta);
            Capture(delta);
            Escape(delta);
        }

        /// <summary>Conduce los coches desde aqui y no desde un componente por coche: asi se recorre un array
        /// cacheado sin buscar nada en la jerarquia ni reservar memoria en cada cuadro.</summary>
        void FixedUpdate()
        {
            if (!Active || cars == null || Verdict != PoliceVerdict.None) return;
            float taxiSpeed = TaxiSpeed;
            float step = Mathf.Min(taxiSpeed + SpeedLead, MaxSpeed) * Time.fixedDeltaTime;
            // El corte se mide en tiempo de reaccion, no en metros fijos: con el taxi parado los interceptores
            // se le echan encima, que es la unica forma de que el cerco llegue a cerrarse.
            float lead = Mathf.Clamp01(taxiSpeed / CityGraph.CruiseSpeed);
            Vector3 taxi = TaxiPoint;
            Vector3 forward = TaxiForward();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            for (int i = 0; i < cars.Length; i++)
            {
                PoliceCar car = cars[i];
                if (!car.Root.activeInHierarchy) continue;
                Vector3 aim = car.Role == PoliceRole.Pursuer
                    ? taxi
                    : taxi + forward * (CutAhead * lead) + right * (CutSide * lead * car.Flank);
                Advance(car, aim, step);
            }
        }

        // ------------------------------------------------------------------ desenlace

        /// <summary>Cuenta atras de la captura: el taxi parado y rodeado. Por debajo de un kilometro por hora y
        /// con dos coches a menos de cinco metros no hay escapatoria, y el contador se reinicia en cuanto el taxi
        /// vuelve a moverse, asi que un frenazo suelto no cuenta como detencion.</summary>
        void Capture(float delta)
        {
            bool surrounded = TaxiKmh < CaptureKmh && NearbyCount(CaptureRadius) >= CaptureCars;
            captureTimer = surrounded ? captureTimer + delta : 0f;
            if (captureTimer >= CaptureSeconds) Verdict = PoliceVerdict.Caught;
        }

        /// <summary>Cuenta atras de la huida: hay que perder de vista a todos los coches y ademas ganarles
        /// distancia por carretera, no solo en linea recta, porque un sentido unico o una calle cortada separan
        /// mucho mas de lo que sugiere el mapa. Hay que aguantar 45 segundos seguidos.</summary>
        void Escape(float delta)
        {
            if (Verdict != PoliceVerdict.None) return;
            bool lost = nearestMetres > EscapeRadius && roadDistance > EscapeRoadMetres;
            escapeTimer = lost ? escapeTimer + delta : 0f;
            if (escapeTimer >= EscapeSeconds) Verdict = PoliceVerdict.Escaped;
        }

        /// <summary>Refresca la distancia por carretera al coche mas cercano a intervalos, no en cada cuadro. Solo
        /// consulta el grafo cuando hace falta: por encima de 200 m en linea recta, la distancia vial tambien los
        /// supera, y la busqueda se queda en el entorno del taxi.</summary>
        void RoadTimer(float delta)
        {
            roadTimer -= delta;
            if (roadTimer > 0f) return;
            roadTimer = RoadCheckInterval;
            roadDistance = nearestMetres > EscapeRoadMetres ? nearestMetres : RoadDistance();
        }

        /// <summary>Lo que hay que conducir de la patrulla mas cercana al taxi. Sin grafo se cae a la linea recta,
        /// que es lo unico medible; con grafo se cuenta la ruta real entre sus cruces.</summary>
        float RoadDistance()
        {
            Vector3 closest = ClosestPosition();
            if (Graph == null) return Planar(closest - TaxiPoint);
            List<Vector2Int> path = Graph.Shortest(Node(closest), Node(TaxiPoint));
            return path.Count < 2 ? Planar(closest - TaxiPoint) : Graph.PathLength(path);
        }

        /// <summary>Cruce que corresponde a una posicion del mundo, en la misma rejilla que usa el grafo.</summary>
        static Vector2Int Node(Vector3 world) =>
            new(Mathf.RoundToInt(world.x / CityGraph.BlockSize), Mathf.RoundToInt(world.z / CityGraph.BlockSize));

        // ------------------------------------------------------------------ conduccion

        /// <summary>Lleva un coche a su objetivo siguiendo la rejilla: avanza por el eje con mas recorrido
        /// pendiente y se mantiene en el carril del otro, y solo gira cuando esta en un cruce. En el ultimo tramo
        /// ataca en linea recta, porque respetando carriles nunca se acercaria lo bastante para cerrar el cerco.</summary>
        void Advance(PoliceCar car, Vector3 aim, float step)
        {
            Rigidbody body = car.Body;
            Vector3 point = body.position;
            Vector3 target = new(aim.x, point.y, aim.z);
            Vector3 next = Planar(aim - point) < PounceDistance
                ? Vector3.MoveTowards(point, target, step)
                : Route(car, point, aim, step);
            next.y = point.y;
            body.MovePosition(next);
            Vector3 heading = next - point;
            heading.y = 0f;
            if (heading.sqrMagnitude > .000001f) body.MoveRotation(Quaternion.LookRotation(heading, Vector3.up));
        }

        /// <summary>Elige el tramo del siguiente cuadro. El eje con mas recorrido pendiente manda, pero el cambio
        /// de eje se pospone hasta el cruce: asi el coche siempre circula por una calle y gira donde se gira.</summary>
        Vector3 Route(PoliceCar car, Vector3 point, Vector3 aim, float step)
        {
            float dx = aim.x - point.x;
            float dz = aim.z - point.z;
            bool alongX = car.AlongX;
            bool turn = alongX
                ? AtCrossing(point.x) && Mathf.Abs(dz) > Mathf.Abs(dx) + AxisMargin
                : AtCrossing(point.z) && Mathf.Abs(dx) > Mathf.Abs(dz) + AxisMargin;
            if (turn) alongX = !alongX;
            car.AlongX = alongX;
            if (alongX)
                return new Vector3(Mathf.MoveTowards(point.x, aim.x, step), 0f,
                    Mathf.MoveTowards(point.z, LaneZ(NearLine(point.z), dx), step));
            return new Vector3(Mathf.MoveTowards(point.x, LaneX(NearLine(point.x), dz), step), 0f,
                Mathf.MoveTowards(point.z, aim.z, step));
        }

        /// <summary>Eje de la calle mas cercana, en metros.</summary>
        static float NearLine(float value) => Mathf.Round(value / CityMath.Block) * CityMath.Block;

        /// <summary>Esta el coche sobre un cruce. Es la unica posicion en la que puede cambiar de calle.</summary>
        static bool AtCrossing(float value) => Mathf.Abs(value - NearLine(value)) < TurnTolerance;

        /// <summary>Carril de una calle este-oeste: quien va hacia +X circula por el lado -Z, como el trafico.</summary>
        static float LaneZ(float gridZ, float sign) => gridZ - Mathf.Sign(sign) * Lane;

        /// <summary>Carril de una calle norte-sur: quien va hacia +Z circula por el lado +X.</summary>
        static float LaneX(float gridX, float sign) => gridX + Mathf.Sign(sign) * Lane;

        /// <summary>Barra luminosa alterna. Los focos se encienden y se apagan en vez de recolorearse, para no
        /// tocar el material compartido del catalogo ni reescribir el renderer en cada cuadro.</summary>
        void Flash(float time)
        {
            if (cars == null) return;
            bool red = Mathf.Repeat(time * FlashRate, 2f) < 1f;
            for (int i = 0; i < cars.Length; i++)
            {
                PoliceCar car = cars[i];
                if (!car.Root.activeInHierarchy) continue;
                if (car.Red != null) car.Red.enabled = red;
                if (car.Blue != null) car.Blue.enabled = !red;
            }
        }

        // ------------------------------------------------------------------ construccion

        /// <summary>Construye las patrullas la primera vez que hacen falta. Se crean apagadas: la ciudad las
        /// enciende al colocarlas, y asi su primer cuadro no las dibuja en el origen del mundo.</summary>
        void EnsureCars()
        {
            if (cars != null) return;
            cars = new PoliceCar[CarCount];
            Transform container = City != null ? City.transform : transform;
            for (int i = 0; i < CarCount; i++)
            {
                PoliceRole role = i == 0 ? PoliceRole.Pursuer : PoliceRole.Interceptor;
                cars[i] = BuildCar(container, i, role, i == 2 ? -1f : 1f);
            }
        }

        /// <summary>Un coche listo para conducir. Lleva Rigidbody para que MovePosition pase por la fisica, pero se
        /// le apagan los colliders a proposito: la captura se decide por velocidad y distancia, y un cuerpo solido
        /// empujando al taxi contaria como choque del jugador y le cobraria una penalizacion que no ha cometido.</summary>
        PoliceCar BuildCar(Transform container, int index, PoliceRole role, float flank)
        {
            GameObject root = Assets != null && Assets.PoliceCar != null
                ? Shape.Model(Assets.PoliceCar, container, Vector3.zero, CarHeight)
                : PrimitiveCar(container);
            root.name = $"Policia {index + 1}";
            var car = new PoliceCar { Body = root.AddComponent<Rigidbody>(), Role = role, Flank = flank };
            Rigidbody body = car.Body;
            body.isKinematic = false;
            body.useGravity = false;
            body.mass = CarMass;
            body.linearDamping = .05f;
            body.angularDamping = 1.2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            LightBar(car, root.transform);
            root.SetActive(false);
            return car;
        }

        /// <summary>Coche de repuesto con primitivas, para cuando el catalogo no trae modelo de policia. Respeta
        /// las medidas del modelo real para que la persecucion no cambie de tacto.</summary>
        GameObject PrimitiveCar(Transform container)
        {
            var root = new GameObject("Policia");
            root.transform.SetParent(container, false);
            Material dark = Assets == null ? null : Assets.Dark;
            Material white = Assets == null ? null : Assets.White;
            Shape.Part("Carroceria", root.transform, new Vector3(0f, .62f, 0f), new Vector3(1.86f, .64f, 4.30f), white);
            Shape.Part("Techo", root.transform, new Vector3(0f, 1.14f, -.30f), new Vector3(1.70f, .56f, 2.00f), dark);
            Wheel(root.transform, new Vector3(-.92f, .34f, 1.32f), dark);
            Wheel(root.transform, new Vector3(.92f, .34f, 1.32f), dark);
            Wheel(root.transform, new Vector3(-.92f, .34f, -1.32f), dark);
            Wheel(root.transform, new Vector3(.92f, .34f, -1.32f), dark);
            return root;
        }

        /// <summary>Rueda cilindrica tumbada sobre su eje: el cilindro de Unity crece en Y, asi que hay que girarlo
        /// para que ruede en el sentido de la marcha.</summary>
        static void Wheel(Transform parent, Vector3 position, Material material) =>
            Shape.Part("Rueda", parent, position, new Vector3(.68f, .15f, .68f), material, PrimitiveType.Cylinder)
                .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        /// <summary>Barra de luces sobre el techo. La emision se sube con un bloque de propiedades, que solo afecta
        /// a este renderer: escribir en el material del catalogo encenderia todas las farolas de la ciudad.</summary>
        void LightBar(PoliceCar car, Transform parent)
        {
            Material red = Assets == null ? null : Assets.Red;
            Material blue = Assets == null ? null : Assets.Blue;
            car.Red = Shape.Part("Faro rojo", parent, new Vector3(-.42f, 1.46f, -.30f), new Vector3(.62f, .16f, .34f), red)
                .GetComponent<Renderer>();
            car.Blue = Shape.Part("Faro azul", parent, new Vector3(.42f, 1.46f, -.30f), new Vector3(.62f, .16f, .34f), blue)
                .GetComponent<Renderer>();
            Emissive(car.Red, red);
            Emissive(car.Blue, blue);
        }

        static void Emissive(Renderer lamp, Material source)
        {
            if (lamp == null || source == null) return;
            Color tint = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
            MaterialPropertyBlock properties = new();
            properties.SetColor("_BaseColor", tint);
            properties.SetColor("_EmissionColor", tint * 3f);
            lamp.SetPropertyBlock(properties);
        }

        // ------------------------------------------------------------------ aparicion

        /// <summary>Coloca una patrulla en su puesto de salida: entre 100 y 150 m del taxi y fuera del encuadre,
        /// porque ver aparecer un coche de la nada rompe la persecucion. Tambien decide por que eje empieza.</summary>
        void Place(PoliceCar car, Vector3 origin, int index)
        {
            Vector3 point = FindSpawn(origin, index);
            Rigidbody body = car.Body;
            car.Root.SetActive(true);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = point;
            body.rotation = Quaternion.identity;
            Vector3 taxi = TaxiPoint;
            car.AlongX = Mathf.Abs(taxi.x - point.x) >= Mathf.Abs(taxi.z - point.z);
        }

        /// <summary>Busca un punto de aparicion sesgado hacia espaldas del taxi y descarta los que caen dentro del
        /// encuadre. Se prueba sobre la camara del jugador y, si no la hay, sobre la vista del propio jugador; sin
        /// camara, la distancia es lo unico que se puede exigir.</summary>
        Vector3 FindSpawn(Vector3 origin, int index)
        {
            Camera view = ViewCamera();
            Plane[] planes = null;
            if (view != null)
            {
                frustum ??= new Plane[6];
                GeometryUtility.CalculateFrustumPlanes(view, frustum);
                planes = frustum;
            }
            Vector3 behind = -TaxiForward();
            for (int attempt = 0; attempt < MaxSpawnAttempts; attempt++)
            {
                float angle = index * 40f + Random.Range(-70f, 70f);
                float distance = Random.Range(MinSpawnDistance, MaxSpawnDistance);
                Vector3 candidate = SnapToRoad(origin + Quaternion.Euler(0f, angle, 0f) * behind * distance, origin);
                float away = Planar(candidate - origin);
                if (away < MinSpawnDistance || away > MaxSpawnDistance) continue;
                if (planes != null && GeometryUtility.TestPlanesAABB(planes, new Bounds(candidate + Vector3.up, CarBounds))) continue;
                return candidate;
            }
            return SnapToRoad(origin + behind * MaxSpawnDistance, origin);
        }

        /// <summary>Redondea un punto a la calle mas cercana y lo deja en el carril de la direccion que se le pide.
        /// Una patrulla aparcada en mitad de la manzana delataria que no circula por la rejilla.</summary>
        static Vector3 SnapToRoad(Vector3 point, Vector3 toward)
        {
            return Mathf.Abs(toward.x - point.x) >= Mathf.Abs(toward.z - point.z)
                ? new Vector3(point.x, 0f, LaneZ(NearLine(point.z), toward.x - point.x))
                : new Vector3(LaneX(NearLine(point.x), toward.z - point.z), 0f, point.z);
        }

        /// <summary>Camara del jugador. La principal puede no estar etiquetada en VR, asi que se cae a la vista del
        /// jugador y, si tampoco existe, se renuncia al test de encuadre en vez de fallar la aparicion.</summary>
        Camera ViewCamera()
        {
            Camera view = Camera.main;
            if (view != null) return view;
            return Drive == null || Drive.Player == null ? null : Drive.Player.View;
        }

        // ------------------------------------------------------------------ medidas

        /// <summary>Posicion del taxi con la que se mide todo. Se lee del Rigidbody para que coincida con la que
        /// usa el director cuando arranca la persecucion; sin coche se cae al propio transform para no lanzar.</summary>
        Vector3 TaxiPoint => Drive == null || Drive.Body == null ? transform.position : Drive.Body.position;

        /// <summary>Velocidad del taxi en m/s, en valor absoluto: la policia solo necesita cuanto corre.</summary>
        float TaxiSpeed => Drive == null || Drive.Body == null ? 0f : Mathf.Abs(Drive.Speed);

        /// <summary>Velocidad del taxi en km/h. Es la unidad en la que el director decide la captura.</summary>
        float TaxiKmh => TaxiSpeed * 3.6f;

        /// <summary>Rumbo del taxi. Reparte a los interceptores delante y a los lados y sesga la aparicion hacia
        /// atras; sin coche se mira hacia delante.</summary>
        Vector3 TaxiForward()
        {
            if (Drive == null) return Vector3.forward;
            Vector3 forward = Drive.transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude < .000001f ? Vector3.forward : forward.normalized;
        }

        /// <summary>Distancia al coche mas cercano desde un punto, infinita si no hay ninguno encendido.</summary>
        float Nearest(Vector3 point)
        {
            float nearest = float.PositiveInfinity;
            if (cars == null) return nearest;
            for (int i = 0; i < cars.Length; i++)
            {
                PoliceCar car = cars[i];
                if (!car.Root.activeInHierarchy) continue;
                float distance = Planar(car.Position - point);
                if (distance < nearest) nearest = distance;
            }
            return nearest;
        }

        /// <summary>Posicion del coche mas cercano. Solo se usa para medir la ruta en el grafo, que es un dato de
        /// bloque: no importa cual de las patrullas se elija cuando hay un empate.</summary>
        Vector3 ClosestPosition()
        {
            Vector3 closest = TaxiPoint;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < cars.Length; i++)
            {
                PoliceCar car = cars[i];
                if (!car.Root.activeInHierarchy) continue;
                float distance = Planar(car.Position - TaxiPoint);
                if (distance >= nearest) continue;
                nearest = distance;
                closest = car.Position;
            }
            return closest;
        }

        /// <summary>Distancia en el plano. Las persecuciones se miden sobre el asfalto: un desnivel no puede hacer
        /// que dos coches parezcan mas lejos de lo que estan.</summary>
        static float Planar(Vector3 delta) => new Vector2(delta.x, delta.z).magnitude;
    }
}
