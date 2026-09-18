using UnityEngine;
using TaxiVR.Gameplay;

namespace TaxiVR.Playable
{
    /// <summary>Gesto de la cara del pasajero. Se deriva de sus emociones en cada cuadro en lugar de fijarse a
    /// mano, porque es la unica forma de que el jugador lea el estado del pasajero sin apartar la vista de la
    /// carretera.</summary>
    public enum PassengerExpression { Calm, Happy, Concerned, Scared, Angry, Crying }

    /// <summary>Pasajero del viaje: cuerpo, animacion, emociones y voz. Vive colgado de la ciudad para que el
    /// origen flotante lo arrastre junto con los sectores, y a partir de que sube al taxi se coloca solo en el
    /// asiento leyendo la posicion del coche.
    ///
    /// El director unicamente le pide andar, correr, subir y reaccionar. Todo lo que se ve (zancada, postura,
    /// cara y queja) se decide aqui, porque el cuerpo del pasajero es un asunto suyo y no del guion del viaje.
    ///
    /// No reserva memoria por cuadro: la cara, las extremidades y el audio se montan una sola vez y el balanceo
    /// se alimenta de la velocidad que el director acaba de pedir, de modo que el pasajero se para solo cuando
    /// deja de caminar.</summary>
    public sealed class PassengerAgent : MonoBehaviour, ICityShiftable
    {
        /// <summary>Altura del suelo peatonal. Es la misma que usa el peaton de la ciudad: si cambiara, el
        /// pasajero caminaria medio metro por debajo del asfalto o levitando.</summary>
        const float GroundHeight = .2f;

        /// <summary>Paso y carrera. La carrera es casi el triple del paso, que es lo que hace que se note la
        /// diferencia desde el asiento cuando el jugador arranca sin esperar.</summary>
        public const float WalkSpeed = 1.3f;
        public const float RunSpeed = 3.6f;

        /// <summary>Distancia a la puerta por debajo de la cual el pasajero ya puede subir.</summary>
        public const float DoorRadius = 2.2f;

        /// <summary>Duracion de la reaccion del final. Coincide con lo que el director concede al cierre de la
        /// partida para que el gesto no se corte a medias.</summary>
        public const float CelebrationSeconds = 8f;

        /// <summary>Puerta trasera derecha: a donde camina y la boca de la animacion de subida.</summary>
        const float DoorSide = 1.15f;
        const float DoorBack = -1.6f;

        /// <summary>Asiento trasero derecho, el del pasajero. El conductor va a la izquierda, asi que el
        /// pasajero se sienta al otro lado y los dos se ven desde dentro de la cabina.</summary>
        const float SeatSide = .55f;
        const float SeatBack = -1f;

        /// <summary>Cuanto hay que bajar el origen del cuerpo al sentarse. El cuerpo se monta con los pies en el
        /// origen, asi que sentarlo es bajar ese origen hasta el asiento. El valor sale de la altura del ojo del
        /// conductor (0.92 sobre el origen del taxi) menos lo que hay de la cabeza a los pies en este cuerpo:
        /// asi la cara del pasajero queda a la misma altura que la del jugador dentro de la misma cabina.</summary>
        const float SeatDrop = .7f;

        /// <summary>Arco que describe el cuerpo al entrar, para que no cruce la puerta en linea recta.</summary>
        const float BoardingLift = .22f;

        /// <summary>Punto de aparicion: fuera del coche y ya sobre la acera, para que el pasajero llegue andando
        /// en lugar de aparecer pegado a la puerta.</summary>
        const float SpawnSide = 4f;
        const float SpawnBack = -6f;

        /// <summary>Altura a la que se ajustan los modelos del catalogo cuando no hay cuerpo de primitivas.</summary>
        const float ModelHeight = 1.75f;

        /// <summary>Franja de acera de la manzana, la misma que pisa el peaton de la ciudad.</summary>
        const float SidewalkMin = 10f;
        const float SidewalkMax = 54f;

        /// <summary>Marcha: cuanto tarda en quedarse quieto, cuanto abre las extremidades y a que ritmo avanza
        /// el ciclo. El balanceo se mide en grados porque las extremidades son bisagras.</summary>
        const float StrideRecovery = 6f;
        const float SwingDegrees = 27f;
        const float PhaseRate = 4f;
        const float PhasePerMetre = 1.8f;
        const float BobHeight = .03f;

        /// <summary>Velocidad de vuelta a la calma, en unidades por segundo. Lenta a proposito: un susto tiene
        /// que durar lo suficiente para notarse, pero no toda la partida.</summary>
        const float FearDecay = .07f;
        const float AngerDecay = .06f;
        const float TrustDecay = .03f;
        const float ComfortDecay = .04f;

        /// <summary>Linea base de la calma. Confianza y comodidad arrancan a media asta para que una sola
        /// maniobra buena no baste para ver al pasajero contento.</summary>
        const float CalmFear = 0f;
        const float CalmAnger = 0f;
        const float CalmTrust = .5f;
        const float CalmComfort = .5f;

        /// <summary>Umbrales de la cara, en el orden en que se comprueban.</summary>
        const float CryingFear = .8f;
        const float ScaredFear = .55f;
        const float AngryAnger = .55f;
        const float WorriedEmotion = .3f;
        const float HappyComfort = .7f;
        const float HappyTrust = .6f;

        /// <summary>Una reaccion solo se grita si mueve las emociones de verdad, y como mucho cada dos segundos:
        /// el pasajero tiene que resultar pesado cuando pasa algo, no ser una radio encendida.</summary>
        const float StrongReaction = .15f;
        const float VoiceInterval = 2f;
        const float VoiceSeconds = .32f;

        /// <summary>Frecuencias base de las tres variantes de voz del catalogo.</summary>
        const int SynthesisRate = 22050;
        static readonly float[] VoiceRoots = { 168f, 196f, 226f };

        static readonly Vector3 EyeSize = new(.034f, .026f, .012f);
        static readonly Vector3 MouthSize = new(.045f, .014f, .012f);
        static readonly Vector2Int[] Crossings = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public PassengerProfile Profile { get; private set; }
        public float Fear { get; private set; }
        public float Anger { get; private set; }
        public float Trust { get; private set; } = CalmTrust;
        public float Comfort { get; private set; } = CalmComfort;
        public PassengerExpression Expression { get; private set; } = PassengerExpression.Calm;

        Transform rig, leftLeg, rightLeg, leftArm, rightArm, face;
        Renderer leftEye, rightEye, mouth;
        MaterialPropertyBlock facePaint;
        AudioSource voice;
        AudioClip voiceClip;
        CityGraph graph;
        Transform taxi;
        EndingKind celebration;
        PassengerExpression painted = (PassengerExpression)(-1);
        Vector3 boardingStart;
        float stride, phase, lean, voiceTimer, celebrateTimer;
        bool boarding, seated, celebrating, built;

        /// <summary>Monta un pasajero y lo deja ya colocado en la acera. El director crea uno por viaje y lo
        /// destruye al terminar, de modo que no hay estado sucio entre pasajeros.</summary>
        public static PassengerAgent Create(EndlessCity city, CityAssets assets, PassengerProfile profile, Transform taxi)
        {
            var go = new GameObject("Pasajero");
            if (city != null) go.transform.SetParent(city.transform, false);
            var agent = go.AddComponent<PassengerAgent>();
            agent.Build(assets, profile, taxi);
            return agent;
        }

        /// <summary>Avance por cuadro del pasajero: decae emociones, refresca la cara y resuelve la postura que
        /// toque. El grafo se guarda porque la eleccion del camino se consulta desde WalkTowards, que el
        /// director llama antes que a este metodo.</summary>
        public void Tick(float deltaTime, TaxiDrive drive, CityGraph graph)
        {
            this.graph = graph;
            if (taxi == null && drive != null) taxi = drive.transform;
            if (deltaTime > 0f)
            {
                Decay(deltaTime);
                voiceTimer = Mathf.Max(0f, voiceTimer - deltaTime);
            }
            UpdateExpression();
            if (seated)
            {
                Follow();
                if (celebrating) Rejoice(deltaTime);
                else Ride(drive, deltaTime);
                return;
            }
            if (boarding) return;
            March(deltaTime);
        }

        /// <summary>Acerca al pasajero a la puerta trasera. El director decide cuando anda, y aqui solo se
        /// traduce eso en metros por segundo y en zancada.</summary>
        public void WalkTowards(Transform taxi) => Approach(taxi, WalkSpeed);

        /// <summary>Persigue al taxi. Es lo que hace el pasajero cuando el jugador se ha ido sin el: corre, no
        /// pasea, porque si no no llegaria nunca a la puerta.</summary>
        public void RunTowards(Transform taxi) => Approach(taxi, RunSpeed);

        /// <summary>Distancia a la puerta medida sobre el asfalto. Es lo que usa el director para saber si el
        /// pasajero ya esta a tiro de subir o si se ha quedado atras.</summary>
        public float DistanceTo(Transform taxi)
        {
            if (taxi == null) return 0f;
            var door = DoorPoint(taxi);
            var here = transform.position;
            return new Vector2(here.x - door.x, here.z - door.z).magnitude;
        }

        /// <summary>Ya esta en la puerta y el taxi esta parado: entonces puede empezar la subida.</summary>
        public bool AtDoor(Transform taxi) => taxi != null && DistanceTo(taxi) < DoorRadius;

        /// <summary>Empieza la subida. La animacion no se resuelve aqui sino en SitIn, con el progreso que manda
        /// el director: asi la postura acompana al reloj del guion y no al de este componente. El punto de
        /// arranque se guarda relativo al taxi, de modo que un frenazo o un salto del origen flotante durante
        /// los cuatro segundos de subida no obligan al pasajero a empezar de nuevo desde otro sitio.</summary>
        public void BeginBoarding(Transform taxi)
        {
            if (taxi == null) return;
            this.taxi = taxi;
            boarding = true;
            seated = false;
            stride = 0f;
            boardingStart = taxi.InverseTransformPoint(transform.position);
            transform.rotation = Heading(Flat(taxi.forward));
        }

        /// <summary>Resuelve la subida en funcion del progreso 0..1 del director. El pasajero se desliza desde
        /// la puerta hasta el asiento con un arco, y las piernas y los brazos acompanan el movimiento.</summary>
        public void SitIn(float progress01)
        {
            if (!boarding || taxi == null) return;
            float progress = Mathf.Clamp01(progress01);
            float blend = Mathf.SmoothStep(0f, 1f, progress);
            var from = taxi.TransformPoint(boardingStart);
            var to = SeatPoint(taxi);
            transform.SetPositionAndRotation(
                Vector3.Lerp(from, to, blend) + taxi.up * (Mathf.Sin(progress * Mathf.PI) * BoardingLift),
                Quaternion.Slerp(Heading(Flat(taxi.forward)), taxi.rotation, blend));
            Fold(blend);
        }

        /// <summary>Da por terminada la subida y deja al pasajero sentado en su asiento. A proposito no se le
        /// emparenta al taxi: sigue colgando de la ciudad para no perder el origen flotante, y la posicion se
        /// recalcula en cada cuadro desde el coche.</summary>
        public void SitDown(Transform taxi)
        {
            if (taxi != null) this.taxi = taxi;
            boarding = false;
            seated = true;
            stride = 0f;
            lean = 0f;
            Fold(1f);
            Follow();
        }

        /// <summary>Reaccion del final. Dura lo mismo que el cierre de la partida y la conduce Tick, de modo que
        /// el director puede cerrarla sin quedarse esperando aqui.</summary>
        public void Celebrate(EndingKind ending)
        {
            celebration = ending;
            celebrating = true;
            celebrateTimer = 0f;
            Shout(.3f, ending switch
            {
                EndingKind.GoodOnTime or EndingKind.GoodLate => 1.25f,
                EndingKind.PoliceCaught or EndingKind.PoliceEscaped => 1.35f,
                _ => .78f
            });
        }

        /// <summary>Sigue al origen flotante. Sentado o subiendo no se toca nada: esas dos posturas se calculan
        /// sobre el taxi, que ya ha sido desplazado, y restar el salto otra vez las dejaria atras.</summary>
        public void Shift(Vector3 delta)
        {
            if (seated || boarding) return;
            transform.position -= delta;
        }

        // ------------------------------------------------------------------ emociones

        /// <summary>Reaccion ante un evento del coche. Cada evento mueve lo que moveria en la vida real: lo que
        /// asusta resta comodidad, lo que atropella ademas rompe la confianza, y lo que se hace bien la
        /// recupera muy poco a poco.</summary>
        public void React(VehicleEventKind kind)
        {
            float fear = 0f, anger = 0f, comfort = 0f, trust = 0f;
            switch (kind)
            {
                case VehicleEventKind.SevereCrash:
                    fear = .35f; anger = .2f; comfort = -.3f;
                    break;
                case VehicleEventKind.MinorBump:
                    fear = .08f;
                    break;
                case VehicleEventKind.PedestrianHit:
                    fear = .5f; anger = .3f; trust = -.4f;
                    break;
                case VehicleEventKind.HardBrake:
                    fear = .15f;
                    break;
                case VehicleEventKind.NearMiss:
                    fear = .1f;
                    break;
                case VehicleEventKind.Speeding:
                    anger = .15f; comfort = -.15f;
                    break;
                case VehicleEventKind.GoodDriving:
                    comfort = .05f; trust = .03f;
                    break;
            }
            Fear = Mathf.Clamp01(Fear + fear);
            Anger = Mathf.Clamp01(Anger + anger);
            Comfort = Mathf.Clamp01(Comfort + comfort);
            Trust = Mathf.Clamp01(Trust + trust);
            Shout(fear + anger * .5f, Mathf.Clamp(1f + (fear - anger) * .4f, .8f, 1.35f));
        }

        /// <summary>Vuelta a la calma. Sin esto un choque a los dos minutos dejaria al pasajero asustado toda
        /// la partida y la cara perderia todo su valor informativo.</summary>
        void Decay(float deltaTime)
        {
            Fear = Mathf.MoveTowards(Fear, CalmFear, FearDecay * deltaTime);
            Anger = Mathf.MoveTowards(Anger, CalmAnger, AngerDecay * deltaTime);
            Trust = Mathf.MoveTowards(Trust, CalmTrust, TrustDecay * deltaTime);
            Comfort = Mathf.MoveTowards(Comfort, CalmComfort, ComfortDecay * deltaTime);
        }

        /// <summary>Traduce las emociones a un gesto y lo aplica. El orden de los umbrales es el de la
        /// especificacion: primero lo que se ve venir de lejos y al final la calma.</summary>
        void UpdateExpression()
        {
            SetExpression(Fear > CryingFear ? PassengerExpression.Crying
                : Fear > ScaredFear ? PassengerExpression.Scared
                : Anger > AngryAnger ? PassengerExpression.Angry
                : Fear > WorriedEmotion || Anger > WorriedEmotion ? PassengerExpression.Concerned
                : Comfort > HappyComfort && Trust > HappyTrust ? PassengerExpression.Happy
                : PassengerExpression.Calm);
        }

        /// <summary>Cambia el gesto de la cara. Solo se reescriben los tres planos cuando el gesto cambia de
        /// verdad, porque repintar la cara en cada cuadro costaria mas que todo el pasajero junto.</summary>
        public void SetExpression(PassengerExpression expression)
        {
            Expression = expression;
            if (expression == painted) return;
            painted = expression;
            var narrow = expression switch
            {
                PassengerExpression.Happy => .7f,
                PassengerExpression.Concerned => .6f,
                PassengerExpression.Scared => 1.25f,
                PassengerExpression.Angry => .5f,
                PassengerExpression.Crying => 1.3f,
                _ => 1f
            };
            Paint(leftEye, new Vector3(1f, narrow, 1f), EyeInk(expression));
            Paint(rightEye, new Vector3(1f, narrow, 1f), EyeInk(expression));
            Paint(mouth, MouthShape(expression), MouthInk(expression));
        }

        /// <summary>Los ojos se aclaran cuando el pasajero abre mucho la mirada: a esta escala es lo unico que
        /// separa una cara tranquila de una asustada.</summary>
        static Color EyeInk(PassengerExpression expression) => expression is PassengerExpression.Scared or PassengerExpression.Crying
            ? new Color(.4f, .42f, .47f)
            : new Color(.06f, .06f, .07f);

        /// <summary>La boca cambia de color solo donde aporta: rojo apretado en el enfado y tono humedo en el
        /// llanto.</summary>
        static Color MouthInk(PassengerExpression expression) => expression switch
        {
            PassengerExpression.Angry => new Color(.45f, .09f, .08f),
            PassengerExpression.Crying => new Color(.25f, .34f, .5f),
            PassengerExpression.Happy => new Color(.32f, .1f, .1f),
            _ => new Color(.2f, .09f, .09f)
        };

        /// <summary>Forma de la boca: ancha en la sonrisa, abierta en el susto y fina en el enfado.</summary>
        static Vector3 MouthShape(PassengerExpression expression) => expression switch
        {
            PassengerExpression.Happy => new Vector3(.075f, .022f, .012f),
            PassengerExpression.Scared => new Vector3(.03f, .026f, .012f),
            PassengerExpression.Crying => new Vector3(.038f, .03f, .012f),
            PassengerExpression.Angry => new Vector3(.06f, .012f, .012f),
            PassengerExpression.Concerned => new Vector3(.05f, .01f, .012f),
            _ => new Vector3(.045f, .012f, .012f)
        };

        /// <summary>Escala y color de un plano de la cara. El color va por bloque de propiedades para no clonar
        /// el material del catalogo, que es el mismo que viste media ciudad.</summary>
        void Paint(Renderer plane, Vector3 scale, Color color)
        {
            if (plane == null) return;
            plane.transform.localScale = Vector3.Scale(plane == mouth ? MouthSize : EyeSize, scale);
            facePaint ??= new MaterialPropertyBlock();
            facePaint.SetColor(BaseColorId, color);
            facePaint.SetColor(ColorId, color);
            plane.SetPropertyBlock(facePaint);
        }

        // ------------------------------------------------------------------ movimiento

        /// <summary>Da un paso hacia la puerta y orienta el cuerpo hacia donde va. El objetivo se recalcula
        /// sobre el taxi actual, asi que el pasajero corrige el rumbo solo si el coche se mueve.</summary>
        void Approach(Transform taxi, float speed)
        {
            if (taxi == null || seated || boarding) return;
            this.taxi = taxi;
            var door = DoorPoint(taxi);
            var goal = ApproachPoint(door);
            var current = transform.position;
            var next = Vector3.MoveTowards(current, new Vector3(goal.x, GroundHeight, goal.z), speed * Time.deltaTime);
            var step = next - current;
            if (step.sqrMagnitude > .0000001f)
            {
                transform.rotation = Heading(Flat(step));
                stride = speed;
            }
            transform.position = next;
        }

        /// <summary>Destino del paso. Normalmente la puerta; cuando el tramo del taxi esta cerrado se va por la
        /// acera de la manzana, porque la linea recta pasaria por dentro de un edificio.</summary>
        Vector3 ApproachPoint(Vector3 door) => graph == null || OnStreet(graph, door) ? door : Sidewalk(door);

        /// <summary>Hay al menos una calle transitable en el cruce del taxi. El grafo es la unica fuente que
        /// sabe que tramos estan cortados en esta semilla.</summary>
        static bool OnStreet(CityGraph graph, Vector3 point)
        {
            var node = Node(point);
            for (int i = 0; i < Crossings.Length; i++)
            {
                var next = node + Crossings[i];
                if (graph.Exists(node, next) && graph.Describe(node, next).Drivable) return true;
            }
            return false;
        }

        /// <summary>Proyecta un punto sobre la acera de su manzana, en la misma franja que pisa el peaton de la
        /// ciudad.</summary>
        static Vector3 Sidewalk(Vector3 point) => new(OnSidewalk(point.x), GroundHeight, OnSidewalk(point.z));

        static float OnSidewalk(float value)
        {
            float block = Mathf.Floor(value / CityMath.Block) * CityMath.Block;
            return block + Mathf.Clamp(value - block, SidewalkMin, SidewalkMax);
        }

        static Vector2Int Node(Vector3 point) =>
            new(Mathf.RoundToInt(point.x / CityGraph.BlockSize), Mathf.RoundToInt(point.z / CityGraph.BlockSize));

        /// <summary>Cuanto corre el pasajero cuando el director lo llama. Se apaga solo al dejar de recibir
        /// ordenes de caminar, asi que si el viaje se interrumpe las piernas se quedan quietas solas.</summary>
        void March(float deltaTime)
        {
            stride = Mathf.MoveTowards(stride, 0f, StrideRecovery * deltaTime);
            float amplitude = Mathf.Clamp01(stride / RunSpeed) * SwingDegrees;
            phase += deltaTime * (PhaseRate + stride * PhasePerMetre);
            float swing = Mathf.Sin(phase) * amplitude;
            Rotate(leftLeg, Quaternion.Euler(swing, 0f, 0f));
            Rotate(rightLeg, Quaternion.Euler(-swing, 0f, 0f));
            Rotate(leftArm, Quaternion.Euler(-swing, 0f, -7f));
            Rotate(rightArm, Quaternion.Euler(swing, 0f, 7f));
            if (rig == null) return;
            rig.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(phase)) * BobHeight, 0f);
            rig.localRotation = Quaternion.identity;
        }

        /// <summary>Postura sentada: piernas al frente y manos en el regazo. Es la que se mantiene durante todo
        /// el viaje, asi que se vuelve a fijar por si la subida la dejo a medias.</summary>
        void Fold(float blend)
        {
            float t = Mathf.Clamp01(blend);
            Rotate(leftLeg, Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-82f, 0f, 0f), t));
            Rotate(rightLeg, Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-82f, 0f, 0f), t));
            Rotate(leftArm, Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-55f, 0f, -8f), t));
            Rotate(rightArm, Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-55f, 0f, 8f), t));
            if (rig == null) return;
            rig.localPosition = Vector3.zero;
            rig.localRotation = Quaternion.identity;
        }

        /// <summary>Coloca al pasajero en su asiento. Se recalcula desde el taxi en cada cuadro para que el
        /// asiento siga siendo el asiento aunque el coche se mueva o el origen flotante salte.</summary>
        void Follow()
        {
            if (taxi == null) return;
            transform.SetPositionAndRotation(SeatPoint(taxi), taxi.rotation);
        }

        /// <summary>Sentado, el pasajero se inclina con la fuerza lateral real del coche. Es lo que lo mantiene
        /// pegado al asiento en una curva en lugar de parecer un maniqui clavado en el sitio.</summary>
        void Ride(TaxiDrive drive, float deltaTime)
        {
            Fold(1f);
            float target = 0f;
            if (drive != null && drive.Body != null)
                target = Mathf.Clamp(Vector3.Dot(drive.Body.linearVelocity, drive.transform.right) * 3.5f, -12f, 12f);
            lean = Mathf.Lerp(lean, target, Mathf.Clamp01(2.5f * deltaTime));
            if (rig != null) rig.localRotation = Quaternion.Euler(0f, 0f, lean);
        }

        /// <summary>Reaccion del final. Se elige por desenlace: brazos arriba si el servicio salio bien,
        /// desplome si salio mal y aspavientos si la partida acabo en persecucion, que es lo que el pasajero
        /// habria estado viendo desde el asiento.</summary>
        void Rejoice(float deltaTime)
        {
            celebrateTimer += deltaTime;
            float t = celebrateTimer;
            switch (celebration)
            {
                case EndingKind.PoliceCaught:
                case EndingKind.PoliceEscaped:
                    Panic(t);
                    break;
                case EndingKind.BadOnTime:
                case EndingKind.BadLate:
                    Slump(t);
                    break;
                default:
                    Cheer(t);
                    break;
            }
        }

        /// <summary>Brazos arriba. El vaiven acelera con el tiempo para que ocho segundos de celebracion no se
        /// queden planos.</summary>
        void Cheer(float t)
        {
            Fold(1f);
            float wave = Mathf.Sin(t * (6f + t)) * 16f;
            Rotate(leftArm, Quaternion.Euler(-165f + wave, 0f, -18f));
            Rotate(rightArm, Quaternion.Euler(-165f - wave, 0f, 18f));
            if (rig == null) return;
            rig.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(t * 5f)) * BobHeight, 0f);
            rig.localRotation = Quaternion.Euler(0f, 0f, wave * .3f);
        }

        /// <summary>Desplome: el cuerpo se vence hacia delante y los brazos caen. Es la cara del pasajero que ha
        /// llegado tarde o mal atendido.</summary>
        void Slump(float t)
        {
            float sag = Mathf.Clamp01(t / 2f);
            Fold(1f);
            Rotate(leftArm, Quaternion.Euler(-8f, 0f, -6f));
            Rotate(rightArm, Quaternion.Euler(-8f, 0f, 6f));
            if (rig != null) rig.localRotation = Quaternion.Euler(18f * sag, 0f, 0f);
        }

        /// <summary>Aspavientos: brazos altos y temblor. El pasajero esta viendo a la policia por la ventanilla,
        /// asi que la reaccion es de panico y no de queja.</summary>
        void Panic(float t)
        {
            Fold(1f);
            float shake = Mathf.Sin(t * 26f) * 10f;
            Rotate(leftArm, Quaternion.Euler(-150f + shake, 0f, -30f));
            Rotate(rightArm, Quaternion.Euler(-150f - shake, 0f, 30f));
            if (rig != null) rig.localRotation = Quaternion.Euler(0f, 0f, shake);
        }

        static void Rotate(Transform joint, Quaternion rotation)
        {
            if (joint != null) joint.localRotation = rotation;
        }

        /// <summary>Rumbo horizontal. Se descarta la componente vertical para que el pasajero no se incline al
        /// caminar por una calle con pendiente.</summary>
        static Vector3 Flat(Vector3 direction) => new(direction.x, 0f, direction.z);

        static Quaternion Heading(Vector3 direction) =>
            direction.sqrMagnitude < .000001f ? Quaternion.identity : Quaternion.LookRotation(direction, Vector3.up);

        /// <summary>Punto de la puerta trasera derecha, siempre a la altura de la acera.</summary>
        static Vector3 DoorPoint(Transform taxi)
        {
            var point = taxi.TransformPoint(new Vector3(DoorSide, 0f, DoorBack));
            return new Vector3(point.x, GroundHeight, point.z);
        }

        /// <summary>Asiento trasero derecho, en coordenadas del taxi.</summary>
        static Vector3 SeatPoint(Transform taxi) => taxi.TransformPoint(new Vector3(SeatSide, -SeatDrop, SeatBack));

        // ------------------------------------------------------------------ montaje

        void Build(CityAssets assets, PassengerProfile profile, Transform taxi)
        {
            if (built) return;
            built = true;
            Profile = profile;
            this.taxi = taxi;

            rig = new GameObject("Cuerpo").transform;
            rig.SetParent(transform, false);
            var skin = assets == null ? null : assets.Skin;
            var shirt = Shirt(assets);
            var trousers = assets == null ? null : assets.Dark;
            if (assets != null && assets.PassengerModel != null) Shape.Model(assets.PassengerModel, rig, Vector3.zero, ModelHeight);
            else Limbs(shirt, skin, trousers);
            BuildFace(skin, trousers);
            BuildVoice();
            Place();
            SetExpression(PassengerExpression.Calm);
        }

        /// <summary>Camisa del pasajero: el catalogo reparte tres variantes y cada indice debe verse distinto
        /// sin cargar un modelo por persona.</summary>
        Material Shirt(CityAssets assets)
        {
            if (assets == null) return null;
            var facades = assets.Facades;
            if (facades == null || facades.Length == 0) return assets.Blue;
            int index = Mathf.Abs(Profile == null ? 0 : Profile.Look) % facades.Length;
            return facades[index] != null ? facades[index] : assets.Blue;
        }

        /// <summary>Cuerpo de primitivas para cuando el catalogo no trae modelo de pasajero. Respeta las medidas
        /// de una persona de 1.75 m para que el asiento y la puerta sigan cuadrando.</summary>
        void Limbs(Material shirt, Material skin, Material trousers)
        {
            Shape.Part("Torso", rig, new Vector3(0f, 1.18f, 0f), new Vector3(.38f, .65f, .22f), shirt);
            Shape.Part("Cabeza", rig, new Vector3(0f, 1.62f, 0f), new Vector3(.2f, .22f, .2f), skin, PrimitiveType.Sphere);
            leftLeg = Joint("Pierna izquierda", new Vector3(-.09f, .85f, 0f), new Vector3(.14f, .85f, .16f), trousers);
            rightLeg = Joint("Pierna derecha", new Vector3(.09f, .85f, 0f), new Vector3(.14f, .85f, .16f), trousers);
            leftArm = Joint("Brazo izquierdo", new Vector3(-.24f, 1.44f, 0f), new Vector3(.11f, .56f, .12f), shirt);
            rightArm = Joint("Brazo derecho", new Vector3(.24f, 1.44f, 0f), new Vector3(.11f, .56f, .12f), shirt);
        }

        /// <summary>Extremidad colgada de una bisagra. Sin bisagra el balanceo giraria el miembro sobre su
        /// propio centro y el pasajero pareceria desarmado.</summary>
        Transform Joint(string name, Vector3 hinge, Vector3 size, Material material)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(rig, false);
            pivot.transform.localPosition = hinge;
            Shape.Part(name + " visible", pivot.transform, new Vector3(0f, -size.y * .5f, 0f), size, material);
            return pivot.transform;
        }

        /// <summary>Cara del pasajero: una placa con dos ojos y una boca. Con el modelo del catalogo o con las
        /// primitivas son siempre los mismos tres planos, asi que la expresion se lee igual en los dos casos.</summary>
        void BuildFace(Material skin, Material dark)
        {
            var plate = new GameObject("Cara");
            face = plate.transform;
            face.SetParent(rig, false);
            face.localPosition = new Vector3(0f, 1.645f, .1f);
            Shape.Part("Placa", face, Vector3.zero, new Vector3(.15f, .13f, .01f), skin);
            leftEye = FacePlane("Ojo izquierdo", new Vector3(-.042f, 0f, .012f), dark);
            rightEye = FacePlane("Ojo derecho", new Vector3(.042f, 0f, .012f), dark);
            mouth = FacePlane("Boca", new Vector3(0f, -.072f, .012f), dark);
        }

        /// <summary>Plano de la cara. Se busca su renderer una sola vez al montarlo: la cara se repinta muchas
        /// veces y volver a buscarlo en la jerarquia seria trabajo por cuadro.</summary>
        Renderer FacePlane(string name, Vector3 position, Material material) =>
            Shape.Part(name, face, position, EyeSize, material).GetComponent<Renderer>();

        /// <summary>Voz sintetizada. El catalogo no trae audios y el juego entero funciona asi: un tono con
        /// envolvente por pasajero, con la frecuencia base segun su variante de voz. La fuente se deja a volumen
        /// uno a proposito, porque cada queja llega como un PlayOneShot y ese factor se multiplica por este.</summary>
        void BuildVoice()
        {
            voiceClip = VoiceClip("Voz " + (Profile == null ? "anonima" : Profile.FullName), VoiceRoot(Profile));
            voice = gameObject.AddComponent<AudioSource>();
            voice.clip = voiceClip;
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 1f;
            voice.maxDistance = 16f;
            voice.dopplerLevel = 0f;
            voice.volume = 1f;
        }

        static float VoiceRoot(PassengerProfile profile)
        {
            if (profile == null) return VoiceRoots[0];
            return VoiceRoots[Mathf.Abs(profile.Voice) % VoiceRoots.Length];
        }

        /// <summary>Queja corta. Suena solo en reacciones fuertes y como mucho cada dos segundos, y la altura
        /// del tono la decide el peso del miedo frente al enfado.</summary>
        void Shout(float strength, float pitch)
        {
            if (voice == null || voiceClip == null || strength < StrongReaction || voiceTimer > 0f) return;
            voiceTimer = VoiceInterval;
            voice.pitch = Mathf.Clamp(pitch, .7f, 1.4f);
            voice.PlayOneShot(voiceClip, Mathf.Clamp01(.25f + strength));
        }

        /// <summary>Sintetiza un quejido en memoria, con el mismo truco que la radio del juego, para no depender
        /// de ningun archivo importado.</summary>
        static AudioClip VoiceClip(string name, float root)
        {
            int count = (int)(SynthesisRate * VoiceSeconds);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SynthesisRate;
                float envelope = Mathf.Sin(Mathf.Clamp01(t / VoiceSeconds) * Mathf.PI);
                float vibrato = 1f + Mathf.Sin(t * 24f) * .06f;
                data[i] = (Mathf.Sin(t * root * vibrato * Mathf.PI * 2f) * .22f + Mathf.Sin(t * root * 2f * Mathf.PI * 2f) * .06f) * envelope;
            }
            var clip = AudioClip.Create(name, count, 1, SynthesisRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Sitio de aparicion: en la acera, a un lado y por detras del taxi. Si no hay taxi se deja en
        /// el origen, que es lo unico razonable sin coche.</summary>
        void Place()
        {
            if (taxi == null)
            {
                transform.position = new Vector3(0f, GroundHeight, 0f);
                return;
            }
            var door = DoorPoint(taxi);
            var start = Sidewalk(door + Flat(taxi.right) * SpawnSide + Flat(taxi.forward) * SpawnBack);
            transform.position = start;
            transform.rotation = Heading(Flat(door - start));
        }
    }
}
