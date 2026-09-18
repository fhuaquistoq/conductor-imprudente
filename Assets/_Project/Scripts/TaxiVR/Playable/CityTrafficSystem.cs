using UnityEngine;
using TaxiVR.Gameplay;

namespace TaxiVR.Playable
{
    public enum TrafficLightState { Red, Amber, Green }

    /// <summary>Semaforo de la especificacion: 20 s verde, 3 s ambar, todas rojas 1 s, y el otro eje igual.</summary>
    public static class TrafficLights
    {
        public const float GreenSeconds = 20f;
        public const float AmberSeconds = 3f;
        public const float AllRedSeconds = 1f;
        public const float HalfCycle = GreenSeconds + AmberSeconds + AllRedSeconds;
        public const float Cycle = HalfCycle * 2f;

        /// <summary>Estado de un eje en un instante cualquiera. La hora se pliega sobre el ciclo, de modo que el
        /// semaforo no depende de cuando arranco la partida ni hay que reiniciarlo nunca.</summary>
        public static TrafficLightState State(bool northSouth, float time)
        {
            float phase = Mathf.Repeat(time, Cycle);
            float local = northSouth ? phase : Mathf.Repeat(phase - HalfCycle, Cycle);
            if (local < GreenSeconds) return TrafficLightState.Green;
            return local < GreenSeconds + AmberSeconds ? TrafficLightState.Amber : TrafficLightState.Red;
        }

        /// <summary>Solo el verde autoriza a cruzar. El ambar tambien frena porque el segundo de todo rojo
        /// existe justo para que el cruce quede vacio entre los dos ejes.</summary>
        public static bool IsGreen(bool northSouth, float time) => State(northSouth, time) == TrafficLightState.Green;

        /// <summary>Ambar del eje, expuesto aparte porque el HUD y las pruebas lo piden como booleano.</summary>
        public static bool IsAmber(bool northSouth, float time) => State(northSouth, time) == TrafficLightState.Amber;
    }

    /// <summary>Trafico civil y peatones de la ciudad infinita. Vive fuera de EndlessCity porque es el unico
    /// sistema que decide cuanta gente hay en la calle: asi la densidad puede subir y bajar con el jugador sin
    /// que la ciudad ni los demas sistemas se enteren.
    ///
    /// Todo el parque se construye una sola vez y se recicla, nunca se instancia ni se destruye mientras se
    /// conduce: en Quest el presupuesto por fotograma solo se sostiene si el numero de objetos en escena esta
    /// acotado de antemano y no depende del estado de la partida.</summary>
    public sealed class CityTrafficSystem : MonoBehaviour, ICityShiftable
    {
        /// <summary>Coches civiles que pueden circular a la vez. El resto de la reserva espera apagado.</summary>
        public const int MaxActiveCars = 18;

        /// <summary>Tamano de la reserva de coches: es el techo duro de objetos civiles en escena.</summary>
        public const int CarPoolSize = 32;

        /// <summary>Peatones caminando a la vez.</summary>
        public const int MaxActivePedestrians = 24;

        /// <summary>Tamano de la reserva de peatones.</summary>
        public const int PedestrianPoolSize = 48;

        const int VehicleLayer = 9;
        const float DrivenRadius = 35f;
        const float DrivenSqr = DrivenRadius * DrivenRadius;
        const float RecycleDistance = 120f;
        const float RecycleSqr = RecycleDistance * RecycleDistance;
        const float SpawnDistance = 138f;
        const float SpawnSpread = 30f;
        const float LaneOffset = 3f;
        const float CarGap = 26f;
        const float MinCruise = 4.5f;
        const float MaxCruise = 7.5f;
        const float SweepSeconds = .5f;
        const float DensitySeconds = 10f;
        const float DensityApproach = .2f;
        const float DefaultDensity = .5f;
        const float FastKmh = 40f;
        const int FastPedestrianCap = 12;
        const float MinActiveCars = 4f;
        const float MinActivePedestrians = 8f;
        const int DensitySalt = 149;
        const float StopNear = 6f;
        const float StopFar = 17f;
        const float StopRate = 8f;
        const float ResumeRate = 2f;
        const float ProbeOrigin = 2.5f;
        const float ProbeLength = 8f;
        const float ProbeHeight = .6f;
        const int FarProbeStride = 4;
        public const float PedestrianBaseSpeed = 1.15f;
        const float PedestrianHeight = CityGrid.CurbHeight + .01f;
        const float PedestrianModelHeight = 1.72f;

        /// <summary>Radio del anillo de acera alrededor de la manzana: la acera esta a dos metros del borde de
        /// la manzana, asi que el anillo pasa por encima de ella y no por delante de las casas.</summary>
        const float SidewalkOffset = 2f;

        /// <summary>Manzanas que rodean al taxi. Reparte los peatones por el vecindario cercano en lugar de
        /// amontonarlos todos en la manzana de debajo, que es la unica que el jugador mira de verdad.</summary>
        static readonly Vector2Int[] Around =
        {
            new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
        };

        static readonly Vector3 CarBoundsSize = new Vector3(2.4f, 1.8f, 4.8f);
        static readonly Vector3 PedestrianBoundsSize = new Vector3(1f, 2f, 1f);

        public EndlessCity City;
        public CityAssets Assets;
        public Transform Taxi;
        public CityGraph Graph;

        /// <summary>Presion de trafico global, 0..1. Se sortea del cruce del taxi cada 10 s y se persigue con
        /// suavidad para que la ciudad no cambie de golpe al cruzar una esquina.</summary>
        public float Density { get; private set; }

        /// <summary>Coches civiles circulando ahora mismo.</summary>
        public int ActiveCount { get; private set; }

        /// <summary>Coches de la reserva que estan apagados y disponibles.</summary>
        public int PooledCount => CarPoolSize - ActiveCount;

        /// <summary>Peatones caminando ahora mismo. Lo lee la capa de diagnostico.</summary>
        public int ActivePedestrians { get; private set; }

        Vehicle[] fleet;
        Person[] crowd;
        Rigidbody taxiBody;
        Camera view;
        readonly Plane[] frustum = new Plane[6];
        float densityTarget = DefaultDensity;
        float densityTimer;
        float sweepTimer;
        int carCursor;
        int personCursor;
        bool built;
        Vector3 lastTaxiPosition;

        /// <summary>Inyeccion del bootstrap. Montar el parque aqui y no en Start deja la ciudad poblada desde
        /// el primer fotograma, sin el hueco de dos segundos que se veia al empezar a conducir.</summary>
        public void Configure(EndlessCity city, CityAssets assets, Transform taxi, CityGraph graph)
        {
            City = city;
            Assets = assets;
            Taxi = taxi;
            Graph = graph;
            Build();
        }

        void Start()
        {
            if (Taxi == null && City != null) Taxi = City.Taxi;
            if (Assets == null && City != null) Assets = City.Assets;
            Build();
        }

        void Update()
        {
            if (City == null || Taxi == null) return;
            Build();
            if (!built) return;

            float delta = Time.deltaTime;
            Vector3 taxiPosition = Taxi.position;
            float kmh = TaxiKmh(delta);

            densityTimer -= delta;
            if (densityTimer <= 0f)
            {
                densityTimer += DensitySeconds;
                Retarget();
            }
            Density = Mathf.MoveTowards(Density, densityTarget, DensityApproach * delta);

            sweepTimer -= delta;
            if (sweepTimer <= 0f)
            {
                sweepTimer += SweepSeconds;
                RefreshView();
                SweepCars(taxiPosition);
                SweepCrowd(taxiPosition, kmh);
            }

            StepCars(taxiPosition, delta);
            StepCrowd();
            lastTaxiPosition = taxiPosition;
        }

        /// <summary>Solo los coches cercanos gastan fisicas. Dentro de 35 m importa que el taxi los empuje y que
        /// se les vea el empujon; fuera de esa distancia nadie nota la diferencia, solo la factura de CPU.</summary>
        void FixedUpdate()
        {
            if (!built || City == null || Taxi == null) return;
            float delta = Time.fixedDeltaTime;
            for (int i = 0; i < fleet.Length; i++)
            {
                var car = fleet[i];
                if (!car.Active || !car.Driven) continue;
                Advance(car, delta, true);
                car.Body.MovePosition(car.Position);
            }
        }

        /// <summary>El origen flotante de la ciudad salta en multiplos exactos de manzana; carriles y manzanas se
        /// reetiquetan restando ese desplazamiento para que el trafico siga pisando su carretera. Sin esto cada
        /// salto dejaria los coches a 64 m de su carril.</summary>
        public void Shift(Vector3 delta)
        {
            if (fleet != null)
                for (int i = 0; i < fleet.Length; i++)
                {
                    var car = fleet[i];
                    if (car.NorthSouth) { car.Cross -= delta.x; car.Along -= delta.z; }
                    else { car.Along -= delta.x; car.Cross -= delta.z; }
                    if (!car.Active) continue;
                    if (car.Driven) car.Body.MovePosition(car.Position);
                    else car.Transform.SetPositionAndRotation(car.Position, car.Heading);
                }

            if (crowd == null) return;
            var sectors = new Vector2Int(-Mathf.RoundToInt(delta.x / CityMath.Block), -Mathf.RoundToInt(delta.z / CityMath.Block));
            for (int i = 0; i < crowd.Length; i++)
            {
                var person = crowd[i];
                person.Sector += sectors;
                if (person.Active) StepPerson(person);
            }
        }

        // ------------------------------------------------------------------ montaje de las reservas

        /// <summary>Monta las dos reservas. Cualquier referencia que falte deja el sistema inerte en lugar de
        /// reventar, porque el bootstrap puede montar el trafico antes que el resto de la escena.</summary>
        void Build()
        {
            if (built || Taxi == null) return;
            built = true;
            taxiBody = Taxi.GetComponent<Rigidbody>();
            lastTaxiPosition = Taxi.position;

            fleet = new Vehicle[CarPoolSize];
            for (int i = 0; i < fleet.Length; i++) fleet[i] = BuildCar(i);

            crowd = new Person[PedestrianPoolSize];
            for (int i = 0; i < crowd.Length; i++) crowd[i] = BuildPerson(i);

            Retarget();
            Density = densityTarget;
        }

        Vehicle BuildCar(int index)
        {
            var go = new GameObject("Coche civil");
            go.transform.SetParent(transform, false);
            go.layer = VehicleLayer;
            var car = new Vehicle { GameObject = go, Transform = go.transform, Slice = index % FarProbeStride };

            var prefab = Assets == null || Assets.Cars == null || Assets.Cars.Length == 0
                ? null
                : Assets.Cars[index % Assets.Cars.Length];
            if (prefab != null)
            {
                var model = Instantiate(prefab, go.transform);
                model.transform.localPosition = Vector3.zero;
                car.Probe = model.GetComponentInChildren<Renderer>();
            }
            if (car.Probe == null) car.Probe = FallbackCar(go.transform);

            var collider = go.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, .55f, 0);
            collider.size = new Vector3(1.8f, 1.1f, 4.2f);

            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            car.Body = body;

            go.SetActive(false);
            return car;
        }

        /// <summary>Coche de caja para cuando el catalogo no trae modelos: el sistema sigue siendo jugable y los
        /// coches siguen viendose, aunque sean feos. Devuelve el renderer que se usara como sonda de visibilidad.</summary>
        Renderer FallbackCar(Transform parent)
        {
            var shell = Shape.Part("Carroceria", parent, new Vector3(0, .5f, 0), new Vector3(1.8f, .8f, 4.2f), Assets == null ? null : Assets.Dark);
            Shape.Part("Cabina", parent, new Vector3(0, 1.05f, -.25f), new Vector3(1.5f, .5f, 2f), Assets == null ? null : Assets.Glass);
            for (int side = -1; side <= 1; side += 2)
                for (int axle = -1; axle <= 1; axle += 2)
                {
                    var wheel = Shape.Part("Rueda", parent, new Vector3(side * .9f, .38f, axle * 1.4f), new Vector3(.62f, .2f, .62f),
                        Assets == null ? null : Assets.Dark, PrimitiveType.Cylinder);
                    wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
                }
            return shell.GetComponent<Renderer>();
        }

        Person BuildPerson(int index)
        {
            var go = new GameObject("Peaton");
            go.transform.SetParent(transform, false);
            var person = new Person
            {
                GameObject = go,
                Transform = go.transform,
                Offset = Around[index % Around.Length],
                Phase = index * 7.31f,
                Speed = PedestrianBaseSpeed + index % 5 * .06f
            };

            var body = Bodies(isHair: false, index);
            if (body != null)
            {
                var model = Shape.Model(body, go.transform, Vector3.zero, PedestrianModelHeight);
                person.Body = model.transform;
                var instance = model.transform.childCount > 0 ? model.transform.GetChild(0).gameObject : model;
                // El pelo se injerta sobre el mismo esqueleto del cuerpo: la malla se reengancha a los huesos
                // del cuerpo por nombre, asi que un peinado cuesta un dibujo y no un personaje entero.
                var hair = Bodies(isHair: true, index);
                if (hair != null) PedestrianLook.Attach(instance, hair);
                person.Walker = PedestrianLook.Walk(instance, Assets == null ? null : Assets.PedestrianWalk, person.Phase);
            }
            else BuildLimbs(person);

            go.SetActive(false);
            return person;
        }

        /// <summary>Cuerpo o peinado determinista para un peaton. El desfase de los peinados es distinto al de
        /// los cuerpos para que no se repita la misma pareja justo en el peaton de al lado.</summary>
        GameObject Bodies(bool isHair, int index)
        {
            if (Assets == null) return null;
            var list = isHair ? Assets.PedestrianHair : Assets.PedestrianBodies;
            if (list == null || list.Length == 0) return isHair ? null : Assets.PedestrianModel;
            return list[(isHair ? index * 5 + index / 3 : index) % list.Length];
        }

        /// <summary>Peaton de primitivas para cuando no hay modelo. Reutiliza la marcha de CityPedestrian, que ya
        /// esta ajustada a ojo, pero con las extremidades colgadas de una bisagra: sin bisagra el balanceo giraria
        /// cada miembro sobre su centro y el peaton pareceria desarmado.</summary>
        void BuildLimbs(Person person)
        {
            var root = person.Transform;
            var skin = Assets == null ? null : Assets.Skin;
            var shirt = Assets == null ? null : Assets.Blue;
            var trousers = Assets == null ? null : Assets.Dark;

            Shape.Part("Torso", root, new Vector3(0, 1.18f, 0), new Vector3(.38f, .65f, .22f), shirt);
            Shape.Part("Cabeza", root, new Vector3(0, 1.62f, 0), new Vector3(.2f, .22f, .2f), skin, PrimitiveType.Sphere);
            person.LeftLeg = Limb(root, "Pierna izquierda", new Vector3(-.09f, .85f, 0), new Vector3(.14f, .85f, .16f), trousers);
            person.RightLeg = Limb(root, "Pierna derecha", new Vector3(.09f, .85f, 0), new Vector3(.14f, .85f, .16f), trousers);
            person.LeftArm = Limb(root, "Brazo izquierdo", new Vector3(-.24f, 1.44f, 0), new Vector3(.11f, .56f, .12f), shirt);
            person.RightArm = Limb(root, "Brazo derecho", new Vector3(.24f, 1.44f, 0), new Vector3(.11f, .56f, .12f), shirt);
        }

        static Transform Limb(Transform parent, string name, Vector3 hinge, Vector3 size, Material material)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = hinge;
            Shape.Part(name + " visible", pivot.transform, new Vector3(0, -size.y * .5f, 0), size, material);
            return pivot.transform;
        }

        // ------------------------------------------------------------------ densidad

        /// <summary>La densidad se sortea del cruce que ocupa el taxi: es determinista (el mismo recorrido ve el
        /// mismo trafico) y cambia sola al conducir, que es justo lo que pide el diseno. Sin grafo se queda en un
        /// valor medio para que la ciudad no aparezca vacia.</summary>
        void Retarget() => densityTarget = Graph == null ? DefaultDensity : Mathf.Clamp01(CityGraph.Unit(SectorAt(Taxi.position), DensitySalt));

        /// <summary>Velocidad del taxi para decidir cuanto peaton aguanta la maquina. Se lee del cuerpo y no del
        /// desplazamiento porque el reetiquetado del origen mueve el taxi de golpe y falsearia la medida.</summary>
        float TaxiKmh(float delta)
        {
            if (taxiBody != null) return taxiBody.linearVelocity.magnitude * 3.6f;
            return delta <= 0f ? 0f : (Taxi.position - lastTaxiPosition).magnitude / delta * 3.6f;
        }

        static Vector2Int SectorAt(Vector3 position) => new Vector2Int(CityMath.Sector(position.x), CityMath.Sector(position.z));

        static Vector3 PointAt(bool northSouth, float cross, float along) =>
            northSouth ? new Vector3(cross, 0, along) : new Vector3(along, 0, cross);

        // ------------------------------------------------------------------ reparto de la reserva

        /// <summary>Reparte la reserva entre coches circulando y coches aparcados. Se hace a golpes cada medio
        /// segundo porque la decision depende de la camara y de la densidad, y nada de eso cambia entre fotogramas;
        /// hacerlo por fotograma solo añadiria recorridos de listas completas sin cambiar el resultado.</summary>
        void SweepCars(Vector3 taxiPosition)
        {
            int wanted = Mathf.RoundToInt(Mathf.Lerp(MinActiveCars, MaxActiveCars, Density));
            for (int i = 0; i < fleet.Length; i++)
            {
                var car = fleet[i];
                if (!car.Active) continue;
                if ((car.Position - taxiPosition).sqrMagnitude < RecycleSqr) continue;
                if (InView(car.Position + Vector3.up * .7f, CarBoundsSize, car.Probe)) continue;
                Park(car);
            }
            for (int attempt = 0; attempt < fleet.Length && ActiveCount < wanted; attempt++)
            {
                var car = fleet[carCursor];
                carCursor = (carCursor + 1) % fleet.Length;
                if (car.Active) continue;
                if (Launch(car, taxiPosition)) ActiveCount++;
            }
        }

        /// <summary>Devuelve un coche a la reserva. Apagarlo es mas barato que destruirlo y conserva compilado el
        /// proceso de redibujado, que es lo que evita el tiron de la primera aparicion de un coche nuevo.</summary>
        void Park(Vehicle car)
        {
            if (car.Driven) SetDriven(car, false);
            car.Active = false;
            car.Speed = 0f;
            car.GameObject.SetActive(false);
            ActiveCount--;
        }

        /// <summary>Coloca un coche en su carril, justo fuera del alcance de reciclaje. Si ese punto queda a
        /// espaldas de la camara se usa el otro lado del mismo carril: nacer invisible desperdiciaria la activacion
        /// porque el barrido siguiente lo devolveria a la reserva, y asi en cambio entra de frente al jugador.</summary>
        bool Launch(Vehicle car, Vector3 taxiPosition)
        {
            bool northSouth = Random.value < .5f;
            int sign = Random.value < .5f ? 1 : -1;
            float cross = Mathf.Round((northSouth ? taxiPosition.x : taxiPosition.z) / CityMath.Block) * CityMath.Block
                + (northSouth ? sign * LaneOffset : -sign * LaneOffset);
            float taxiAlong = northSouth ? taxiPosition.z : taxiPosition.x;
            float distance = SpawnDistance + Random.value * SpawnSpread;
            float along = taxiAlong + sign * distance;
            if (view != null && !InView(PointAt(northSouth, cross, along) + Vector3.up * .7f, CarBoundsSize, null))
                along = taxiAlong - sign * distance;

            for (int i = 0; i < fleet.Length; i++)
            {
                var other = fleet[i];
                if (!other.Active || other.NorthSouth != northSouth || other.Cross != cross) continue;
                if (Mathf.Abs(other.Along - along) < CarGap) return false;
            }

            car.NorthSouth = northSouth;
            car.Sign = sign;
            car.Cross = cross;
            car.Along = along;
            car.Speed = car.Cruise = Random.Range(MinCruise, MaxCruise);
            car.Heading = Quaternion.LookRotation(car.Forward, Vector3.up);
            car.Transform.SetPositionAndRotation(car.Position, car.Heading);
            car.Active = true;
            car.Driven = false;
            car.Body.isKinematic = true;
            car.GameObject.SetActive(true);
            return true;
        }

        void SweepCrowd(Vector3 taxiPosition, float kmh)
        {
            int wanted = Mathf.RoundToInt(Mathf.Lerp(MinActivePedestrians, MaxActivePedestrians, Density));
            if (kmh > FastKmh) wanted = Mathf.Min(wanted, FastPedestrianCap);
            var taxiSector = SectorAt(taxiPosition);

            for (int i = 0; i < crowd.Length; i++)
            {
                var person = crowd[i];
                if (!person.Active) continue;
                if (InView(person.Transform.position + Vector3.up, PedestrianBoundsSize, null)) continue;
                var home = taxiSector + person.Offset;
                if (person.Sector != home) person.Sector = home;
                else if ((person.Transform.position - taxiPosition).sqrMagnitude > RecycleSqr) ParkPerson(person);
            }
            for (int attempt = 0; attempt < crowd.Length && ActivePedestrians < wanted; attempt++)
            {
                var person = crowd[personCursor];
                personCursor = (personCursor + 1) % crowd.Length;
                if (person.Active) continue;
                LaunchPerson(person, taxiSector);
                ActivePedestrians++;
            }
        }

        /// <summary>Los peatones cambian de manzana y se apagan solo cuando la camara no los mira: hacerlo a la
        /// vista seria un salto de 64 m delante del jugador.</summary>
        void ParkPerson(Person person)
        {
            person.Active = false;
            person.GameObject.SetActive(false);
            ActivePedestrians--;
        }

        void LaunchPerson(Person person, Vector2Int taxiSector)
        {
            person.Sector = taxiSector + person.Offset;
            // El desfase solo reparte a la gente por el anillo; el anillo real se mide en StepPerson, porque su
            // largo depende de si la manzana es cuadrada o rectangular.
            person.Phase = Random.value * 320f;
            person.Active = true;
            person.GameObject.SetActive(true);
            StepPerson(person);
        }

        // ------------------------------------------------------------------ conduccion

        /// <summary>Los coches lejanos son cinematicos y se deslizan por su carril de forma analitica: ni fisicas
        /// ni raycast por fotograma, solo la resta de la distancia que queda hasta el siguiente cruce.</summary>
        void StepCars(Vector3 taxiPosition, float delta)
        {
            for (int i = 0; i < fleet.Length; i++)
            {
                var car = fleet[i];
                if (!car.Active) continue;
                bool driven = (car.Position - taxiPosition).sqrMagnitude <= DrivenSqr;
                if (driven != car.Driven) SetDriven(car, driven);
                if (car.Driven) continue;
                Advance(car, delta, (Time.frameCount + car.Slice) % FarProbeStride == 0);
                car.Transform.SetPositionAndRotation(car.Position, car.Heading);
            }
        }

        /// <summary>Avanza un coche por su carril. La distancia se mide sobre el eje del carril, que es lo unico
        /// que el coche conoce: el mismo truco que usa CityTraffic, con el semaforo de la especificacion.</summary>
        void Advance(Vehicle car, float delta, bool probe)
        {
            float remaining = Mathf.Repeat(-car.Sign * car.Along, CityMath.Block);
            bool stop = !TrafficLights.IsGreen(car.NorthSouth, Time.time) && remaining > StopNear && remaining < StopFar;
            if (!stop && probe)
            {
                Vector3 forward = car.Forward;
                Vector3 origin = car.Position + Vector3.up * ProbeHeight + forward * ProbeOrigin;
                if (Physics.Raycast(origin, forward, out var hit, ProbeLength) && hit.rigidbody != car.Body) stop = true;
            }
            car.Speed = Mathf.MoveTowards(car.Speed, stop ? 0f : car.Cruise, (stop ? StopRate : ResumeRate) * delta);
            car.Along += car.Sign * car.Speed * delta;
        }

        /// <summary>Cambia el coche de calidad de fisicas. Al entrar en la zona del taxi se le devuelve la posicion
        /// al cuerpo, porque el carril se ha estado escribiendo en el transform mientras era cinematico.</summary>
        void SetDriven(Vehicle car, bool driven)
        {
            car.Driven = driven;
            car.Body.isKinematic = !driven;
            if (!driven) return;
            car.Body.position = car.Transform.position;
            car.Body.rotation = car.Transform.rotation;
        }

        // ------------------------------------------------------------------ peatones

        void StepCrowd()
        {
            for (int i = 0; i < crowd.Length; i++)
                if (crowd[i].Active) StepPerson(crowd[i]);
        }

        /// <summary>Recorre el anillo de acera de la manzana. El anillo se mide sobre la manzana y no sobre el
        /// sector, de modo que tambien funciona en las manzanas rectangulares, que ocupan dos sectores: el peaton
        /// rodea la manzana entera en lugar de cortar por dentro de las casas.
        ///
        /// El recorrido es analitico, sin navegacion ni raycast: la posicion sale del tiempo por la velocidad, y
        /// el animador solo ajusta su zancada a esa velocidad.</summary>
        void StepPerson(Person person)
        {
            var block = CityGrid.BlockAt(person.Sector);
            var size = block.SizeWorld;
            var origin = block.OriginWorld;
            float lowX = origin.x - SidewalkOffset, highX = origin.x + size.x + SidewalkOffset;
            float lowZ = origin.z - SidewalkOffset, highZ = origin.z + size.z + SidewalkOffset;
            float side = highX - lowX, depth = highZ - lowZ;
            float travel = Mathf.Repeat(Time.time * person.Speed + person.Phase, 2f * (side + depth));
            float x, z;
            int facing;
            if (travel < side) { x = lowX + travel; z = lowZ; facing = 0; }
            else if (travel < side + depth) { x = highX; z = lowZ + travel - side; facing = 1; }
            else if (travel < 2f * side + depth) { x = highX - (travel - side - depth); z = highZ; facing = 2; }
            else { x = lowX; z = highZ - (travel - 2f * side - depth); facing = 3; }

            person.Transform.SetPositionAndRotation(new Vector3(x, PedestrianHeight, z), Quaternion.Euler(0, 90 - facing * 90, 0));

            if (person.Walker != null && person.Walker.Ready) person.Walker.Pose(person.Speed);
            else if (person.LeftLeg != null)
            {
                float swing = Mathf.Sin(Time.time * 7f + person.Phase) * 27f;
                person.LeftLeg.localRotation = Quaternion.Euler(swing, 0, 0);
                person.RightLeg.localRotation = Quaternion.Euler(-swing, 0, 0);
                person.LeftArm.localRotation = Quaternion.Euler(-swing, 0, -7);
                person.RightArm.localRotation = Quaternion.Euler(swing, 0, 7);
            }
            else if (person.Body != null)
                person.Body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(Time.time * 3.5f + person.Phase)) * .03f, 0);
        }

        // ------------------------------------------------------------------ visibilidad

        void RefreshView()
        {
            if (view == null) view = Camera.main;
            if (view == null) return;
            GeometryUtility.CalculateFrustumPlanes(view, frustum);
        }

        /// <summary>Un objeto esta a la vista si su renderer se dibujo el fotograma anterior o si su caja cae
        /// dentro del frustum. Vale cualquiera de las dos y no las dos a la vez: prefiero conservar de mas a
        /// reciclar algo que el jugador podria estar viendo. Sin camara, lejos significa invisible.</summary>
        bool InView(Vector3 center, Vector3 size, Renderer probe)
        {
            if (probe != null && probe.isVisible) return true;
            if (view == null) return false;
            return GeometryUtility.TestPlanesAABB(frustum, new Bounds(center, size));
        }

        // ------------------------------------------------------------------ tipos de la reserva

        /// <summary>Un coche civil de la reserva. Guarda su carril en coordenadas del mundo para que el salto del
        /// origen sea una resta y no una conversion.</summary>
        sealed class Vehicle
        {
            public GameObject GameObject;
            public Transform Transform;
            public Rigidbody Body;
            public Renderer Probe;
            public Quaternion Heading;
            public bool Active;
            public bool Driven;
            public bool NorthSouth;
            public int Sign = 1;
            public int Slice;
            public float Along;
            public float Cross;
            public float Speed;
            public float Cruise = 5f;

            public Vector3 Forward => NorthSouth ? new Vector3(0, 0, Sign) : new Vector3(Sign, 0, 0);

            public Vector3 Position => PointAt(NorthSouth, Cross, Along);
        }

        /// <summary>Un peaton de la reserva. Su manzana es un dato propio y no la del padre porque el peaton
        /// cambia de manzana en caliente.</summary>
        sealed class Person
        {
            public GameObject GameObject;
            public Transform Transform, LeftLeg, RightLeg, LeftArm, RightArm, Body;
            public PedestrianWalker Walker;
            public Vector2Int Sector;
            public Vector2Int Offset;
            public float Phase;
            public float Speed = PedestrianBaseSpeed;
            public bool Active;
        }
    }
}
