using UnityEngine;
using TaxiVR.Gameplay;

namespace TaxiVR.Playable
{
    /// <summary>Hoja impresa con el perfil del pasajero. Es papel de verdad: sale del rodillo creciendo en la
    /// bandeja, se puede coger y se puede arrugar con las dos manos hasta convertirse en una bola que ya no dice
    /// nada. El perfil del que sale es el mismo que puntua el viaje, asi que lo que el jugador lee es exactamente
    /// lo que se le va a exigir.
    ///
    /// La hoja se cuelga de lo que le de el director: de la cabina si el interior ya existe, y de la ciudad en
    /// caso contrario. Nunca reserva memoria por cuadro; el unico trabajo continuo es contar el tiempo que las
    /// dos manos pasan apretando cerca del papel.</summary>
    public sealed class PassengerSheet : MonoBehaviour
    {
        /// <summary>Separacion maxima entre las dos manos para considerar que estan comprimiendo el papel.</summary>
        public const float CrumpleDistance = .12f;

        /// <summary>Tiempo que hay que mantener la compresion. Un roce no arruga nada: hay que insistir.</summary>
        public const float CrumpleSeconds = .5f;

        const int InteractionLayer = 8;

        /// <summary>Medidas del papel y de la bola en la que acaba convertido.</summary>
        const float SheetWidth = .16f;
        const float SheetHeight = .22f;
        const float SheetThickness = .003f;
        const float SeedHeight = .02f;
        const float BallSize = .07f;
        const float BallRadius = .035f;

        /// <summary>Alto que se le da al colisionador para que la mano pueda alcanzar el papel y no solo su
        /// plano exacto, que seria imposible de agarrar con el mando.</summary>
        const float GrabDepth = .06f;

        /// <summary>Progreso a partir del cual la hoja esta entera: antes de eso no hay nada que leer ni que
        /// arrugar, asi que ni la etiqueta ni el agarre existen todavia.</summary>
        const float PrintedFraction = .98f;

        /// <summary>Tamano del texto de la hoja. La ruta del pasajero ocupa trece renglones en 22 cm de papel, asi
        /// que la letra tiene que ser mas pequena que la del boleto del taxi o el texto se sale del papel.</summary>
        const float TextSize = .0026f;

        /// <summary>Separacion del texto respecto al papel, en la direccion en la que se lee la hoja.</summary>
        const float LabelOffset = .006f;

        /// <summary>Distancia a la que las manos ya estan tocando la hoja.</summary>
        const float Reach = .25f;

        /// <summary>Radio con el que se agarra el papel, y masa que se le da a la bola para que al lanzarla se
        /// comporte como un papel y no como una piedra.</summary>
        const float GrabRadius = .13f;
        const float SheetMass = .02f;
        const float BallMass = .04f;

        /// <summary>Bandeja del salpicadero. Son las mismas coordenadas que ya usa el boleto del taxi, para que
        /// la hoja del pasajero aparezca justo donde el jugador tiene acostumbrado el papel.</summary>
        static readonly Vector3 TrayLocal = new(.4f, .5f, .05f);

        /// <summary>Inclinacion del papel y del texto. La misma que el boleto existente: el papel se apoya
        /// inclinado en el salpicadero en lugar de quedarse de pie como un cartel, y la cara impresa mira hacia
        /// el conductor.</summary>
        static readonly Quaternion Tilt = Quaternion.Euler(65f, -15f, 0f);

        const int SynthesisRate = 22050;
        const float SoundSeconds = .35f;

        public PassengerProfile Profile { get; private set; }
        public bool Crumpled { get; private set; }

        BoxCollider box;
        Rigidbody body;
        CockpitInteractable interaction;
        Transform paper;
        TextMesh label;
        GameObject ball;
        AudioSource voice;
        AudioClip crumpleClip;
        Material paperMaterial;
        PlayerHands player;
        float compression;
        bool printed, built;

        /// <summary>Monta la hoja en la cabina. El director la crea cuando arranca la impresora y la destruye al
        /// cerrar el viaje, de modo que cada pasajero tiene su propio papel y no queda nada del anterior.</summary>
        public static PassengerSheet Create(EndlessCity city, CityAssets assets, PassengerProfile profile, Transform cabin)
        {
            var go = new GameObject("Hoja del pasajero");
            go.layer = InteractionLayer;
            var sheet = go.AddComponent<PassengerSheet>();
            sheet.Build(city, assets, profile, cabin);
            return sheet;
        }

        /// <summary>Version sin cabina: el director todavia no conoce el interior cuando manda imprimir la hoja,
        /// asi que el papel se cuelga de la ciudad y aparece flotando delante del taxi.</summary>
        public static PassengerSheet Create(EndlessCity city, CityAssets assets, PassengerProfile profile) =>
            Create(city, assets, profile, null);

        /// <summary>Progreso de impresion 0..1. La hoja sale del rodillo creciendo hacia arriba, y solo cuando
        /// esta entera tiene colision y se puede coger: a medio imprimir no hay nada que leer.</summary>
        public void SetPrintProgress(float progress01)
        {
            float progress = Mathf.Clamp01(progress01);
            float height = Mathf.Lerp(SeedHeight, SheetHeight, progress);
            if (paper != null)
            {
                paper.localScale = new Vector3(SheetWidth, height, SheetThickness);
                paper.localPosition = new Vector3(0f, height * .5f, 0f);
            }
            if (label != null) label.transform.localPosition = new Vector3(0f, height * .5f, -LabelOffset);
            if (box != null) box.size = new Vector3(SheetWidth, height, GrabDepth);
            bool ready = progress >= PrintedFraction;
            printed = ready;
            if (label != null) label.gameObject.SetActive(ready);
            if (box != null) box.enabled = ready;
            if (interaction != null) interaction.enabled = ready;
        }

        /// <summary>Convierte la hoja en una bola de papel: cambia la caja por una esfera, borra el texto y la
        /// suelta. A partir de aqui el pasajero ya no puede leer nada, que es justo lo que pidio quien la
        /// arrugo, y el unico resto es un objeto que se puede lanzar.</summary>
        public void Crumple()
        {
            if (Crumpled) return;
            Crumpled = true;
            compression = 0f;
            if (interaction != null)
            {
                interaction.Release(0);
                interaction.Release(1);
                interaction.Release(2);
            }
            if (box != null) Destroy(box);
            if (paper != null) Destroy(paper.gameObject);
            if (label != null) Destroy(label.gameObject);
            ball = Shape.Part("Bola de papel", transform, Vector3.zero, Vector3.one * BallSize, paperMaterial, PrimitiveType.Sphere);
            var sphere = gameObject.AddComponent<SphereCollider>();
            sphere.radius = BallRadius;
            if (interaction != null)
            {
                interaction.Radius = BallRadius + .05f;
                interaction.Visual = ball.transform;
            }
            if (body != null) body.mass = BallMass;
            if (voice != null && crumpleClip != null) voice.PlayOneShot(crumpleClip, .7f);
        }

        /// <summary>Cuenta la compresion de las dos manos sobre el papel. El contador se reinicia en cuanto una
        /// mano se separa o deja de apretar, porque la especificacion pide medio segundo continuo y no medio
        /// segundo repartido.</summary>
        void Update()
        {
            if (Crumpled || !printed) return;
            var hands = Hands;
            if (hands == null) return;
            if (!hands.TryGetHandPoint(0, out var left) || !hands.TryGetHandPoint(1, out var right))
            {
                compression = 0f;
                return;
            }
            bool pinching = hands.HandGripping(0) && hands.HandGripping(1);
            bool together = Vector3.Distance(left, right) < CrumpleDistance;
            bool here = Vector3.Distance((left + right) * .5f, transform.position) < Reach;
            compression = pinching && together && here ? compression + Time.deltaTime : 0f;
            if (compression >= CrumpleSeconds) Crumple();
        }

        /// <summary>Manos del jugador. Se buscan una sola vez y se reintenta mientras no existan, porque la hoja
        /// puede imprimirse antes de que el jugador termine de montarse.</summary>
        PlayerHands Hands => player ??= PlayableRoot.Instance == null ? null : PlayableRoot.Instance.Player;

        void Build(EndlessCity city, CityAssets assets, PassengerProfile profile, Transform cabin)
        {
            if (built) return;
            built = true;
            Profile = profile;
            paperMaterial = assets == null ? null : assets.White;
            Place(city, cabin);
            paper = Shape.Part("Papel", transform, Vector3.zero, new Vector3(SheetWidth, SeedHeight, SheetThickness), paperMaterial).transform;
            if (assets != null && assets.Font != null)
                label = Shape.Label("Texto de la hoja", transform, new Vector3(0f, SeedHeight * .5f, -LabelOffset),
                    profile == null ? string.Empty : profile.SheetText(), TextSize, Color.black, assets.Font);
            box = gameObject.AddComponent<BoxCollider>();
            body = gameObject.AddComponent<Rigidbody>();
            body.mass = SheetMass;
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            interaction = gameObject.AddComponent<CockpitInteractable>();
            interaction.Kind = CockpitKind.Loose;
            interaction.Caption = "Hoja del pasajero";
            interaction.Radius = GrabRadius;
            interaction.Visual = transform;
            BuildVoice();
            SetPrintProgress(0f);
        }

        /// <summary>Coloca el papel en la bandeja del salpicadero, o delante del taxi cuando no hay cabina. La
        /// hoja nunca se cuelga del interior por su cuenta: si el director pasa una cabina se usa, y si no se
        /// cuelga de la ciudad para que exista igualmente.</summary>
        void Place(EndlessCity city, Transform cabin)
        {
            if (cabin != null)
            {
                transform.SetParent(cabin, false);
                transform.localPosition = TrayLocal;
                transform.localRotation = Tilt;
                return;
            }
            if (city != null) transform.SetParent(city.transform, false);
            var taxi = city == null ? null : city.Taxi;
            transform.position = taxi == null ? TrayLocal : taxi.TransformPoint(TrayLocal);
            transform.localRotation = Tilt;
        }

        /// <summary>Sonido de arrugar sintetizado, con el mismo truco que el resto del audio del juego: ruido con
        /// chasquidos sueltos y una envolvente corta, sin depender de ningun archivo importado.</summary>
        void BuildVoice()
        {
            crumpleClip = CrumpleSound("Papel arrugado");
            voice = gameObject.AddComponent<AudioSource>();
            voice.clip = crumpleClip;
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = .6f;
            voice.maxDistance = 10f;
            voice.volume = 1f;
        }

        static AudioClip CrumpleSound(string name)
        {
            int count = (int)(SynthesisRate * SoundSeconds);
            var data = new float[count];
            float crackle = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SynthesisRate;
                if (Random.value < .05f) crackle = Random.value * 2f - 1f;
                crackle *= .7f;
                float envelope = 1f - t / SoundSeconds;
                data[i] = ((Random.value * 2f - 1f) * .45f + crackle) * envelope * envelope;
            }
            var clip = AudioClip.Create(name, count, 1, SynthesisRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
