using UnityEngine;
using UnityEngine.Rendering;

namespace TaxiVR.Playable
{
    /// <summary>Persiana de vision del despertar: cubre la vista y la abre radialmente.</summary>
    public sealed class EyeShutter : MonoBehaviour
    {
        /// <summary>Distancia a la que se cuelga de la camara. Lo bastante lejos del plano cercano (2.5 cm) para
        /// no cortarse y lo bastante cerca para que el jugador no pueda ver el mundo por los bordes del encuadre.</summary>
        const float Distance = .15f;

        /// <summary>Tamano de cada panel. Con 76 grados de campo de vision y 15 cm de distancia, la mitad visible
        /// del encuadre mide unos 12 cm en vertical: 19 cm de medio panel cubren de sobra, y el ancho extra se
        /// anade porque en horizontal el encuadre es mas ancho que alto.</summary>
        const float HalfSize = .19f;
        const float ExtraWidth = .17f;

        /// <summary>Solape entre paneles contiguos. Sin el, una costura de un milimetro dejaria ver la escena
        /// entera por una rendija en mitad del despertar.</summary>
        const float Overlap = .02f;

        const float Thickness = .02f;

        /// <summary>Recorrido diagonal de cada panel al abrirse. Tiene que ser mayor que la mitad visible del
        /// encuadre (21 cm en horizontal) para que los cuatro salgan del todo de la vista.</summary>
        const float Travel = .45f;

        /// <summary>Componente diagonal de la unidad, para que los cuatro paneles se abran por las esquinas y no
        /// en cruz.</summary>
        const float Diagonal = .70711f;

        /// <summary>Cuanto se queda la persiana puesta una vez abierta del todo. Con vision = 1 los paneles ya
        /// estan fuera del encuadre, asi que este margen solo sirve para no destruir el objeto en el mismo cuadro
        /// en que se le pide abrirse.</summary>
        const float HoldSeconds = .4f;

        const int Quadrants = 4;

        /// <summary>Negro casi puro de la persiana. No puede ser el material oscuro del catalogo porque de estos
        /// paneles depende que el jugador no vea absolutamente nada durante el despertar.</summary>
        static readonly Color Ink = new(.008f, .008f, .012f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Transform[] panels;
        Vector3[] rest;
        Vector3[] direction;
        Renderer[] skins;
        MaterialPropertyBlock properties;
        Material material;
        float vision = 1f;
        float hold;
        bool painted, owned, destroyed;

        /// <summary>Cuelga la persiana de la camara y la deja cerrada del todo. Devuelve null cuando no hay
        /// camara: sin vista no hay nada que tapar, y el director ya encadena las llamadas con el operador de
        /// nulo.</summary>
        public static EyeShutter Attach(Camera view)
        {
            if (view == null) return null;
            var root = new GameObject("Persiana de vision");
            root.transform.SetParent(view.transform, false);
            root.transform.localPosition = new Vector3(0f, 0f, Distance);
            root.transform.localRotation = Quaternion.identity;
            var shutter = root.AddComponent<EyeShutter>();
            shutter.Build();
            return shutter;
        }

        /// <summary>Abre o cierra el iris. 0 deja la vista tapada del todo y 1 aparta los cuatro paneles mas alla
        /// del encuadre; el movimiento es diagonal, por las esquinas, que es como se abre un parpado.</summary>
        public void SetVision(float vision01)
        {
            vision = Mathf.Clamp01(vision01);
            if (panels != null)
                for (int i = 0; i < panels.Length; i++)
                    if (panels[i] != null)
                        panels[i].localPosition = rest[i] + direction[i] * (Travel * vision);
            ShowPanels(vision < 1f);
        }

        /// <summary>Retira la persiana un momento despues de abrirse del todo. Se destruye en lugar de quedarse
        /// apagada para no dejar cuatro planos colgando de la camara durante toda la partida.</summary>
        void Update()
        {
            if (vision < 1f)
            {
                hold = 0f;
                return;
            }
            hold += Time.deltaTime;
            if (hold < HoldSeconds || destroyed) return;
            destroyed = true;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (owned && material != null) Destroy(material);
        }

        // ------------------------------------------------------------------ montaje

        void Build()
        {
            material = Darkness();
            panels = new Transform[Quadrants];
            rest = new Vector3[Quadrants];
            direction = new Vector3[Quadrants];
            skins = new Renderer[Quadrants];
            for (int i = 0; i < Quadrants; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float top = i < 2 ? -1f : 1f;
                rest[i] = new Vector3(side * (HalfSize + ExtraWidth - Overlap), top * (HalfSize - Overlap), 0f);
                direction[i] = new Vector3(side * Diagonal, top * Diagonal, 0f);
                var panel = Shape.Part("Panel del iris " + (i + 1), transform, rest[i], PanelSize, material, PrimitiveType.Cube);
                panels[i] = panel.transform;
                skins[i] = panel.GetComponent<Renderer>();
                Darken(skins[i]);
            }
            SetVision(0f);
        }

        static Vector3 PanelSize => new((HalfSize + ExtraWidth) * 2f, HalfSize * 2f, Thickness);

        /// <summary>Material opaco casi negro para los paneles. Se fabrica con un shader sin iluminacion porque el
        /// proyecto mezcla shaders de la tuberia integrada y de URP: un material con luz podria salir claro segun
        /// la escena, y cualquier transparencia seria una rendija por la que se veria el mundo.</summary>
        Material Darkness()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null)
            {
                var assets = PlayableRoot.Instance == null ? null : PlayableRoot.Instance.Assets;
                return assets == null ? null : assets.Dark;
            }
            var created = new Material(shader);
            if (created.HasProperty("_Color")) created.color = Ink;
            if (created.HasProperty("_BaseColor")) created.SetColor("_BaseColor", Ink);
            owned = true;
            return created;
        }

        /// <summary>Saca el panel de la iluminacion y lo pinta casi negro en el propio renderer. Asi la persiana se
        /// ve igual con cualquier material, y no depende de que el catalogo tenga uno oscuro ni de como este
        /// alumbrada la escena.</summary>
        void Darken(Renderer panel)
        {
            if (panel == null) return;
            panel.shadowCastingMode = ShadowCastingMode.Off;
            panel.receiveShadows = false;
            panel.lightProbeUsage = LightProbeUsage.Off;
            panel.reflectionProbeUsage = ReflectionProbeUsage.Off;
            properties ??= new MaterialPropertyBlock();
            properties.SetColor(BaseColorId, Ink);
            properties.SetColor(ColorId, Ink);
            panel.SetPropertyBlock(properties);
        }

        /// <summary>Deja de dibujar los paneles en cuanto la vista esta abierta del todo. Con vision = 1 ya estan
        /// fuera del encuadre, asi que apagarlos no cambia nada de lo que se ve, pero quita de en medio cuatro
        /// objetos que ya no hacen nada.</summary>
        void ShowPanels(bool visible)
        {
            if (painted == visible) return;
            painted = visible;
            if (skins == null) return;
            for (int i = 0; i < skins.Length; i++)
                if (skins[i] != null) skins[i].enabled = visible;
        }
    }
}
