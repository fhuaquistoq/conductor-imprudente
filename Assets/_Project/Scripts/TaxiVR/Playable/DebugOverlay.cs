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
            string Side(FootState state, bool valid) => valid ? state.ToString() : "sin datos";
            return string.Join("\n", new[]
            {
                $"FPS {1f / Mathf.Max(.0001f, Time.smoothDeltaTime):0}   XR {(XRSettings.isDeviceActive ? "activo" : "escritorio")}",
                $"Modo {(root.Player.Desktop ? "teclado + raton" : root.Player.TrackingStatus)}",
                $"Acelerador {drive.Throttle:0.00} (rampa {drive.ThrottleAnalog:0.00})   Freno {drive.Brake:0.00} (rampa {drive.BrakeAnalog:0.00})",
                $"Derrape {(drive.Skidding ? "si" : "no")}   Colisiones {drive.Collisions}",
                $"Pie verde {Side(machine?.Green ?? FootState.Unknown, machine != null && machine.GreenValid)}   rojo {Side(machine?.Red ?? FootState.Unknown, machine != null && machine.RedValid)}",
                $"Pies armados {(machine != null && machine.Armed ? "si" : "no")}   valido {(machine != null && machine.TrackingValid ? "si" : "no")}",
                $"Receptor de pies {(root.Feet == null ? "ausente" : root.Feet.SocketBound ? root.Feet.TrackingReceived ? "recibiendo" : "a la escucha" : "sin puerto")}",
                $"Volante {drive.Wheel?.Value ?? 0:0} grados",
                $"Velocidad {drive.Speed * 3.6f:0} km/h   Marcha {(drive.Direction > 0 ? "D" : "R")}   {(drive.Paused ? "PAUSA" : "en marcha")}",
                $"Ruedas motor {drive.MotorWheelCount}   contacto {drive.GroundedWheels}/4",
                $"GPS {root.GPS.Distance:0} m   entregas {root.GPS.Deliveries}",
                string.Empty,
                "W / S acelerar y frenar   A / D girar   C crucero   ESC pausa y cursor",
                "Q reversa   E avanzar   R volver a la calle   H centrar vista",
                "Clic izquierdo: agarrar o tocar   Rueda: ajustar",
                "Pies: verde acelera, rojo frena",
            });
        }
    }
}
