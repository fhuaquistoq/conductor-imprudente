using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TaxiVR.Gameplay;

namespace TaxiVR.Playable
{
    /// <summary>Marca un obstaculo con la penalizacion que corresponde al chocar con el.</summary>
    public sealed class PenaltyMarker : MonoBehaviour
    {
        public PenaltyKind Kind;
        public VehicleEventKind Event;
    }

    /// <summary>Recupera el taxi cuando queda volcado e inmovil. Sin esto el viaje se queda clavado en un
    /// giro imposible y el jugador no tiene forma de desatascarse dentro de la cabina.</summary>
    public sealed class TaxiRecovery : MonoBehaviour
    {
        public const float TippedDegrees = 70f;
        public const float MaximumSpeedKmh = 2f;
        public const float HoldSeconds = 5f;

        public TaxiDrive Drive;
        public GameDirector Director;

        public bool Recovering { get; private set; }

        const float FadeOutSeconds = .35f;
        const float BlackSeconds = .5f;
        const float FadeInSeconds = .45f;

        float held;
        GameObject fade;
        Material fadeMaterial;

        void Update()
        {
            if (Recovering || Drive == null || Drive.Body == null) return;
            bool tipped = Vector3.Angle(Drive.transform.up, Vector3.up) > TippedDegrees;
            bool stuck = Mathf.Abs(Drive.Speed) * 3.6f < MaximumSpeedKmh;
            held = tipped && stuck ? held + Time.deltaTime : 0f;
            if (held < HoldSeconds) return;
            held = 0f;
            StartCoroutine(Recover());
        }

        /// <summary>Cancela cualquier secuencia en curso y limpia el fundido, para reutilizar el componente
        /// entre pasajeros sin dejar una cortinilla negra colgada de la camara.</summary>
        public void Reset()
        {
            StopAllCoroutines();
            held = 0f;
            Recovering = false;
            ClearFade();
        }

        IEnumerator Recover()
        {
            Recovering = true;
            yield return Fade(0f, .95f, FadeOutSeconds);
            Drive.ResetToRoad();
            Director?.Report(PenaltyKind.TaxiRecovery);
            yield return new WaitForSeconds(BlackSeconds);
            yield return Fade(.95f, 0f, FadeInSeconds);
            ClearFade();
            Recovering = false;
        }

        IEnumerator Fade(float from, float to, float seconds)
        {
            bool visible = BuildFade();
            for (float elapsed = 0; elapsed < seconds; elapsed += Time.deltaTime)
            {
                if (visible) Paint(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / seconds)));
                yield return null;
            }
            if (visible) Paint(to);
        }

        /// <summary>Cortinilla negra pegada a la camara a 0.12 m del plano cercano y ajustada al campo de
        /// vision real, para que tape el salto de posicion sin depender de pantallas completas de otro sistema.</summary>
        bool BuildFade()
        {
            if (fade != null) return true;
            var view = Drive != null && Drive.Player != null ? Drive.Player.View : Camera.main;
            if (view == null) return false;
            var assets = Director != null ? Director.Assets : PlayableRoot.Instance == null ? null : PlayableRoot.Instance.Assets;
            float distance = view.nearClipPlane + .12f;
            float height = 2f * distance * Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad);
            var quad = Shape.Part("Fundido de recuperacion", view.transform, new Vector3(0, 0, distance),
                new Vector3(height * view.aspect * 1.05f, height * 1.05f, 1), assets == null ? null : assets.Dark, PrimitiveType.Quad);
            fadeMaterial = assets != null && assets.Dark != null ? new Material(assets.Dark) : DefaultMaterial();
            if (fadeMaterial != null)
            {
                fadeMaterial.renderQueue = 3000;
                quad.GetComponent<Renderer>().material = fadeMaterial;
            }
            Paint(0f);
            fade = quad;
            return true;
        }

        static Material DefaultMaterial()
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            return shader == null ? null : new Material(shader);
        }

        void Paint(float alpha)
        {
            if (fadeMaterial != null) fadeMaterial.color = new Color(0, 0, 0, alpha);
        }

        void ClearFade()
        {
            if (fade != null) Destroy(fade);
            if (fadeMaterial != null) Destroy(fadeMaterial);
            fade = null;
            fadeMaterial = null;
        }
    }

    /// <summary>Traduce las colisiones del taxi en penalizaciones y reacciones del pasajero.</summary>
    public sealed class CollisionReporter : MonoBehaviour
    {
        public TaxiDrive Drive;
        public GameDirector Director;

        const float MinimumImpact = 1.5f;
        const float SevereImpact = 6f;
        const float WorldImpact = 3f;
        const float RepeatSeconds = 1f;

        readonly Dictionary<PenaltyKind, float> recent = new();

        void OnCollisionEnter(Collision collision)
        {
            if (Director == null || collision.collider == null) return;
            float magnitude = collision.relativeVelocity.magnitude;
            if (magnitude < MinimumImpact) return;
            var marker = collision.collider.GetComponentInParent<PenaltyMarker>();
            if (marker != null)
            {
                var kind = marker.Kind;
                if (kind == PenaltyKind.CivilianVehicle || kind == PenaltyKind.SevereVehicleCollision)
                    kind = magnitude > SevereImpact ? PenaltyKind.SevereVehicleCollision : PenaltyKind.CivilianVehicle;
                Report(kind);
                return;
            }
            if (magnitude > WorldImpact && collision.rigidbody == null) Report(PenaltyKind.TreeOrBuilding);
        }

        /// <summary>Una sola penalizacion por tipo y segundo: un roce lateral genera varios contactos seguidos
        /// y no debe vaciar el marcador de puntuacion.</summary>
        void Report(PenaltyKind kind)
        {
            if (recent.TryGetValue(kind, out float last) && Time.time - last < RepeatSeconds) return;
            recent[kind] = Time.time;
            Director.Report(kind);
        }
    }
}
