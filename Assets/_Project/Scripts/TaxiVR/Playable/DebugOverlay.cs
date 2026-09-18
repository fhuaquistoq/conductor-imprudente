using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace TaxiVR.Playable
{
    // Solo se añade con -taxivr-debug o con "Show Debug In Editor". El juego no muestra UI.
    public sealed class DebugOverlay : MonoBehaviour
    {
        bool visible = true;

        void Update()
        {
            if (Keyboard.current?.f1Key.wasPressedThisFrame == true) visible = !visible;
        }

        void OnGUI()
        {
            if (!visible) return;
            var root = PlayableRoot.Instance;
            if (root == null || root.Player == null) return;
            float scale = Mathf.Clamp(Screen.width / 1280f, .7f, 2);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            var style = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.UpperLeft, padding = new RectOffset(16, 16, 12, 12) };
            style.normal.textColor = new Color(.88f, .95f, .97f);
            GUI.Box(new Rect(20, 20, 420, 340), Text(root), style);
            GUI.matrix = Matrix4x4.identity;
        }

        static string Text(PlayableRoot root)
        {
            var drive = root.Drive;
            var machine = root.Feet == null ? null : root.Feet.Machine;
            var director = root.Director;
            var trip = director == null ? null : director.Trip;
            string Side(FootState state, bool valid) => valid ? state.ToString() : "sin datos";
            return string.Join("\n", new[]
            {
                $"FPS {1f / Mathf.Max(.0001f, Time.smoothDeltaTime):0}   XR {(XRSettings.isDeviceActive ? "activo" : "escritorio")}",
                $"Modo {(root.Player.Desktop ? "teclado + raton" : root.Player.TrackingStatus)}",
                $"Fase {(director == null ? "-" : director.Flow.Phase.ToString())}   {director?.Status}",
                $"Puntuacion {director?.Score ?? 100}   Servicio {(trip == null ? "-" : (trip.Quality == Gameplay.ServiceQuality.Good ? "BUENO" : "MALO"))}",
                $"Peticion {(trip?.PendingRequest == null ? "ninguna" : trip.PendingRequest.Value + $" ({trip.RequestRemaining:0}s)")}",
                $"Acelerador {drive.Throttle:0.00} (rampa {drive.ThrottleAnalog:0.00})   Freno {drive.Brake:0.00} (rampa {drive.BrakeAnalog:0.00})",
                $"Derrape {(drive.Skidding ? "si" : "no")}   Colisiones {drive.Collisions}",
                $"Pie verde {Side(machine?.Green ?? FootState.Unknown, machine != null && machine.GreenValid)}   rojo {Side(machine?.Red ?? FootState.Unknown, machine != null && machine.RedValid)}",
                $"Mando {(drive.FootTracking ? "pies (tracker)" : "teclado / mandos")}   armados {(machine != null && machine.Armed ? "si" : "no")}   voz {(root.Player.Speaking ? "hablando" : "callado")}",
                $"AVISO pies: {root.Feet?.Warning ?? "sin avisos"}",
                $"Volante {drive.Wheel?.Value ?? 0:0} grados   Marcha {(drive.Direction > 0 ? "F" : drive.Direction < 0 ? "R" : "O")}",
                $"Velocidad {drive.Speed * 3.6f:0} km/h   Ruedas {drive.GroundedWheels}/4",
                $"GPS {root.GPS.Distance:0} m   ruta {root.GPS.Path.Count} cruces   {(root.GPS.Powered ? "on" : "off")}",
                $"Trafico {root.Traffic?.ActiveCount ?? 0} coches (densidad {root.Traffic?.Density ?? 0:0.00})   peaton {(root.Traffic?.ActivePedestrians ?? 0)}",
                $"Policia {(root.Police != null && root.Police.Active ? $"{root.Police.NearbyCount(120f)} cerca, {root.Police.NearestDistance:0} m" : "inactiva")}",
                string.Empty,
                "W / S acelerar y frenar   A / D girar   C crucero   ESC pausa",
                "Q / E marcha   R volver a la calle   H centrar   V hablar",
                "Clic izquierdo: agarrar o tocar   Rueda: ajustar",
                "Pies: verde acelera, rojo frena (con el tracker en linea se ignoran W/S y gatillos)",
            });
        }
    }
}
