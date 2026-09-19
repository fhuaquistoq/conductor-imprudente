using System.Collections.Generic;
using UnityEngine;
using TaxiVR.Gameplay;

namespace TaxiVR.Playable
{
    /// <summary>Mandos del habitaculo que el pasajero puede notar: radio, clima y la bandeja de comida.
    /// Se mantiene fuera del GameDirector para que el viaje solo lea valores logicos ya resueltos (emisora,
    /// temperatura, si le llego comida) y el interior pueda reconstruirse sin tocar la maquina de estados.</summary>
    public sealed class InteriorControls : MonoBehaviour
    {
        public const int ClimateCold = -1;
        public const int ClimateNone = 0;
        public const int ClimateWarm = 1;

        const int SynthesisRate = 22050;
        const float SongSeconds = 8f;
        const float BaseVolume = .3f;

        /// <summary>Una raiz distinta por cancion: cinco pistas offline que no comparten archivo ni carga de disco.</summary>
        static readonly float[] SongRoots = { 110f, 130.81f, 146.83f, 164.81f, 196f };
        static readonly float[] Melody = { 1f, 1.25f, 1.5f, 2f, 1.5f, 1.25f, 1.125f, 1.5f };

        /// <summary>Radio encendida o apagada. Apagada no suena nada aunque la emisora siga elegida.</summary>
        public bool RadioOn { get; private set; }

        /// <summary>Emisora actual, 0..4. Siempre dentro del catalogo de cinco canciones.</summary>
        public int Station { get; private set; }

        /// <summary>Volumen del mando, 0..1. Por defecto bajo para no tapar la conversacion.</summary>
        public float Volume { get; private set; } = BaseVolume;

        /// <summary>Temperatura logica: -1 frio, 0 neutro, +1 calido. Es lo unico que lee el viaje.</summary>
        public int Climate { get; private set; } = ClimateNone;

        CityAssets assets;
        Transform cabin;
        AudioSource radio;
        AudioClip[] stations;
        Renderer climatePanel;
        readonly List<CockpitInteractable> food = new();
        FoodItem offer;
        bool hasOffer;
        int climateMemory = ClimateWarm;
        bool built;

        /// <summary>Monta el interior una sola vez. La radio y la bandeja se cuelgan del cabin para que viajen
        /// con el coche; el director solo se usa como respaldo por si no llega el catalogo de materiales.</summary>
        public void Configure(CityAssets cityAssets, Transform cabinTransform, GameDirector director)
        {
            assets = cityAssets != null ? cityAssets : director == null ? null : director.Assets;
            cabin = cabinTransform != null ? cabinTransform : transform;
            if (built) return;
            built = true;
            BuildRadio();
            BuildClimate();
            BuildTray();
        }

        /// <summary>Ultima comida que el jugador entrego al pasajero, devuelta una sola vez para que el viaje
        /// no cuente la misma entrega en varios frames.</summary>
        public bool TryConsumeFoodOffer(out FoodItem item)
        {
            item = offer;
            if (!hasOffer) return false;
            hasOffer = false;
            return true;
        }

        /// <summary>Registra la entrega. Si el jugador ofrece varias cosas solo vale la mas reciente, que es
        /// con la que realmente se quedo el pasajero.</summary>
        public void OfferFood(FoodItem item)
        {
            offer = item;
            hasOffer = true;
        }

        /// <summary>Deja el habitaculo como recien limpiado: sin oferta, radio apagada, clima neutro y la
        /// bandeja devuelta a su sitio para el siguiente pasajero.</summary>
        public void ResetForNewPassenger()
        {
            hasOffer = false;
            RadioOn = false;
            if (radio != null) radio.Pause();
            ApplyVolume();
            climateMemory = ClimateWarm;
            SetClimate(ClimateNone);
            foreach (var grab in food) grab.ReturnHome();
        }

        public void ToggleRadio()
        {
            RadioOn = !RadioOn;
            if (radio == null) return;
            if (RadioOn)
            {
                if (radio.clip == null && stations != null && stations.Length > 0) radio.clip = stations[Station];
                radio.Play();
            }
            else radio.Pause();
            ApplyVolume();
        }

        public void NextStation() => SelectStation(Station + 1, true);

        public void PreviousStation() => SelectStation(Station - 1, true);

        /// <summary>Sintoniza una emisora concreta. La usa el mando de sintonía, que barre las cinco.</summary>
        public void SetStation(int value) => SelectStation(value, true);

        public void SetVolume(float value)
        {
            Volume = Mathf.Clamp01(value);
            ApplyVolume();
        }

        /// <summary>Fija la temperatura logica. Cualquier valor no nulo alimenta el recuerdo que usa el
        /// interruptor, de modo que apagar y volver a encender no pierde el ajuste del pasajero.</summary>
        public void SetClimate(int value)
        {
            Climate = value > 0 ? ClimateWarm : value < 0 ? ClimateCold : ClimateNone;
            if (Climate != ClimateNone) climateMemory = Climate;
            PaintClimate();
        }

        public void ToggleClimate() => SetClimate(Climate == ClimateNone ? climateMemory : ClimateNone);

        void SelectStation(int value, bool restart)
        {
            if (stations == null || stations.Length == 0) return;
            int count = stations.Length;
            int next = ((value % count) + count) % count;
            Station = next;
            if (radio == null) return;
            if (restart || radio.clip != stations[next]) radio.clip = stations[next];
            if (RadioOn) radio.Play();
        }

        void ApplyVolume()
        {
            if (radio != null) radio.volume = RadioOn ? Volume : 0;
        }

        void BuildRadio()
        {
            var source = new GameObject("Radio del pasajero").AddComponent<AudioSource>();
            source.transform.SetParent(cabin, false);
            source.spatialBlend = 0;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0;
            stations = new AudioClip[SongRoots.Length];
            for (int i = 0; i < stations.Length; i++) stations[i] = Song("Cancion " + (i + 1), SongRoots[i], i);
            radio = source;
            if (stations.Length > 0) radio.clip = stations[0];
            ApplyVolume();
        }

        /// <summary>Sintetiza una melodia en memoria, igual que el resto del juego, para no depender de
        /// ningun archivo de audio importado.</summary>
        static AudioClip Song(string name, float root, int voice)
        {
            int count = (int)(SynthesisRate * SongSeconds);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SynthesisRate;
                float note = Melody[(int)(t * (2 + voice)) % Melody.Length];
                float frequency = root * note;
                float envelope = Mathf.Exp(-Mathf.Repeat(t, .5f) * 4f);
                data[i] = (Mathf.Sin(t * frequency * Mathf.PI * 2) * .16f + Mathf.Sin(t * frequency * Mathf.PI * 4) * .03f) * envelope;
            }
            var clip = AudioClip.Create(name, count, 1, SynthesisRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void BuildClimate()
        {
            var panel = Shape.Part("Panel de clima", cabin, new Vector3(-.115f, .6f, .445f), new Vector3(.05f, .016f, .008f), assets == null ? null : assets.Dark);
            climatePanel = panel.GetComponent<Renderer>();
            PaintClimate();
        }

        void PaintClimate()
        {
            if (climatePanel == null || assets == null) return;
            climatePanel.sharedMaterial = Climate switch
            {
                ClimateCold => Safe(assets.Blue, assets.White),
                ClimateWarm => Safe(assets.Red, assets.White),
                _ => Safe(assets.White, assets.Dark)
            };
        }

        void BuildTray()
        {
            for (int i = 0; i < PassengerProfile.Menu.Length; i++)
            {
                int column = i % 3;
                int row = i / 3;
                BuildFood(PassengerProfile.Menu[i], new Vector3(.08f + column * .17f, .47f + row * .02f, -.1f + row * .28f));
            }
        }

        void BuildFood(FoodItem item, Vector3 position)
        {
            var root = new GameObject(item.ToString());
            root.layer = Layers.Interaction;
            root.transform.SetParent(cabin, false);
            root.transform.localPosition = position;
            Fill(root.transform, item);
            var collider = root.AddComponent<SphereCollider>();
            collider.radius = .055f;
            // Solido y no trigger: con un trigger el Rigidbody la atravesaba todo y la comida se caia al vacio.
            collider.isTrigger = false;
            var body = root.AddComponent<Rigidbody>();
            body.mass = .12f;
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var grab = root.AddComponent<CockpitInteractable>();
            grab.Kind = CockpitKind.Loose;
            grab.Caption = "Ofrecer " + PassengerProfile.NameOf(item);
            grab.Radius = .1f;
            grab.Visual = root.transform;
            food.Add(grab);
        }

        void Fill(Transform parent, FoodItem item)
        {
            var white = assets == null ? null : assets.White;
            var dark = assets == null ? null : assets.Dark;
            var red = assets == null ? null : assets.Red;
            var green = assets == null ? null : assets.Foliage;
            var yellow = assets == null ? null : assets.Yellow;
            switch (item)
            {
                case FoodItem.MeatSandwich:
                    Sandwich(parent, Safe(red, white));
                    break;
                case FoodItem.EggSandwich:
                    Sandwich(parent, Safe(yellow, white));
                    break;
                case FoodItem.CheeseSandwich:
                    Sandwich(parent, Safe(Facade(0), yellow));
                    break;
                case FoodItem.Jerky:
                    Shape.Part("Plato", parent, Vector3.zero, new Vector3(.12f, .008f, .12f), Safe(white, dark), PrimitiveType.Cylinder);
                    Shape.Part("Cecina", parent, new Vector3(-.012f, .011f, .01f), new Vector3(.085f, .005f, .045f), Safe(red, dark), PrimitiveType.Cube);
                    Shape.Part("Cecina", parent, new Vector3(.014f, .017f, -.012f), new Vector3(.07f, .005f, .04f), Safe(dark, red), PrimitiveType.Cube);
                    break;
                case FoodItem.VegetableSalad:
                    Shape.Part("Ensaladera", parent, Vector3.zero, new Vector3(.13f, .04f, .13f), Safe(white, dark), PrimitiveType.Cylinder);
                    Shape.Part("Verduras", parent, new Vector3(0, .026f, 0), new Vector3(.115f, .022f, .115f), Safe(green, white), PrimitiveType.Cylinder);
                    break;
                default:
                    Shape.Part("Manzana", parent, new Vector3(0, .014f, 0), new Vector3(.085f, .085f, .085f), Safe(red, white), PrimitiveType.Sphere);
                    Shape.Part("Tallo", parent, new Vector3(0, .06f, 0), new Vector3(.008f, .02f, .008f), Safe(dark, red), PrimitiveType.Cylinder);
                    break;
            }
        }

        void Sandwich(Transform parent, Material filling)
        {
            var bread = Facade(1);
            Shape.Part("Pan inferior", parent, new Vector3(0, -.012f, 0), new Vector3(.11f, .05f, .11f), bread, PrimitiveType.Sphere);
            Shape.Part("Relleno", parent, new Vector3(0, -.002f, 0), new Vector3(.12f, .014f, .12f), Safe(filling, bread), PrimitiveType.Cylinder);
            Shape.Part("Pan superior", parent, new Vector3(0, .014f, 0), new Vector3(.115f, .048f, .115f), bread, PrimitiveType.Sphere);
        }

        Material Facade(int index)
        {
            if (assets == null) return null;
            var facades = assets.Facades;
            if (facades == null || facades.Length == 0) return assets.White;
            var material = facades[((index % facades.Length) + facades.Length) % facades.Length];
            return material != null ? material : assets.White;
        }

        static Material Safe(Material primary, Material fallback) => primary != null ? primary : fallback;
    }
}
