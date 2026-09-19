using UnityEngine;

namespace TaxiVR.Playable
{
    /// <summary>El asiento del taxi es rigido, y por eso el jugador lo atraviesa en cuanto se echa atras: la
    /// cabeza entra en el respaldo. Este acompanante reclina el asiento solo lo que haga falta para que siga
    /// quedando por detras de la cabeza, y lo devuelve a su sitio cuando el jugador se incorpora.
    ///
    /// Los cuatro numeros se ajustan con el visor puesto, que es donde se ve: si el asiento se mueve al reves,
    /// se marca <see cref="Flip"/> y listo.</summary>
    public sealed class SeatCompanion : MonoBehaviour
    {
        public Transform Head;

        [Tooltip("Grados de reclinado por cada metro que la cabeza se echa atras.")]
        public float ReclinePerMetre = 40f;

        [Tooltip("Reclinado maximo, en grados: mas alla el asiento se despega del coche.")]
        public float MaxRecline = 14f;

        [Tooltip("Metros que se separa el asiento por cada metro que la cabeza se echa atras.")]
        public float BackPerMetre = .7f;

        [Tooltip("Separacion maxima, en metros.")]
        public float MaxBack = .24f;

        [Tooltip("Invierte el sentido del movimiento si al probarlo en el visor sale al reves.")]
        public bool Flip;

        [Tooltip("Suavizado del movimiento, en metros por segundo.")]
        public float Speed = 1.2f;

        Vector3 homePosition;
        Quaternion homeRotation;
        float rest;
        bool resting;
        float lean;

        void Awake()
        {
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
        }

        void LateUpdate()
        {
            if (Head == null) return;
            // La referencia es la postura con la que arranca la partida, no una posicion del coche: asi el
            // asiento queda quieto mientras se conduce y solo se mueve si el jugador se echa atras.
            var head = transform.parent.InverseTransformPoint(Head.position);
            if (!resting) { rest = head.z; resting = true; }
            lean = Mathf.MoveTowards(lean, Mathf.Max(0f, rest - head.z), Speed * Time.deltaTime);
            float sign = Flip ? -1f : 1f;
            transform.localRotation = homeRotation * Quaternion.Euler(-Mathf.Min(MaxRecline, lean * ReclinePerMetre) * sign, 0, 0);
            transform.localPosition = homePosition - Vector3.forward * Mathf.Min(MaxBack, lean * BackPerMetre) * sign;
        }

        /// <summary>Vuelve a tomar la postura de reposo, para cuando se recoloca al jugador.</summary>
        public void Recalibrate()
        {
            resting = false;
            lean = 0f;
            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
        }
    }
}
