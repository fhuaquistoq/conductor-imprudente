using System;
using System.Globalization;
using UnityEngine;

namespace TaxiVR.Playable
{
    public enum FootState { Up, Down, Unknown }

    /// <summary>Salud del enlace con el FootTracker. Es diagnostico: no cambia quien manda sobre los pedales.</summary>
    public enum FootConnection { Connected, Unstable, Lost }

    public readonly struct FootPacket
    {
        public readonly int Sequence;
        public readonly double Timestamp;
        public readonly bool Calibrated;
        public readonly bool BrakePressed, AcceleratorPressed;
        public readonly float BrakeConfidence, AcceleratorConfidence;
        public readonly float BrakeValue, AcceleratorValue;
        public readonly int Version;

        public FootPacket(int sequence, double timestamp, bool calibrated,
            bool brakePressed, float brakeConfidence, float brakeValue,
            bool acceleratorPressed, float acceleratorConfidence, float acceleratorValue, int version)
        {
            Sequence = sequence; Timestamp = timestamp; Calibrated = calibrated;
            BrakePressed = brakePressed; BrakeConfidence = brakeConfidence; BrakeValue = brakeValue;
            AcceleratorPressed = acceleratorPressed; AcceleratorConfidence = acceleratorConfidence; AcceleratorValue = acceleratorValue;
            Version = version;
        }

        /// <summary>Confianza 0 significa que el marcador no se vio: "no se ve" nunca es "pisado".</summary>
        public bool BrakeValid => BrakeConfidence > 0f;
        public bool AcceleratorValid => AcceleratorConfidence > 0f;
        public FootState Brake => State(BrakeValid, BrakePressed);
        public FootState Accelerator => State(AcceleratorValid, AcceleratorPressed);

        static FootState State(bool valid, bool pressed) => !valid ? FootState.Unknown : pressed ? FootState.Down : FootState.Up;
    }

    public static class FootProtocol
    {
        public const int Version = 2;
        public const int Port = 5055;
        public const int RatePerSecond = 30;
        /// <summary>Sin datos frescos, el mando vuelve al teclado y a los gatillos.</summary>
        public const float StaleSeconds = 1f;
        /// <summary>Umbrales de §7 para el estado del enlace, solo informativos en la vista debug.</summary>
        public const float UnstableSeconds = .25f;
        public const float LostSeconds = .5f;

        [Serializable]
        sealed class PedalWire
        {
            public bool pressed;
            public float confidence;
            public float value;
        }

        [Serializable]
        sealed class PacketWire
        {
            public int version;
            public int sequence;
            public double timestamp;
            public bool calibrated;
            public PedalWire brake;
            public PedalWire accelerator;
        }

        public static bool TryParse(string json, out FootPacket packet)
        {
            packet = default;
            if (string.IsNullOrWhiteSpace(json) || json[0] != '{') return false;
            PacketWire parsed;
            try { parsed = JsonUtility.FromJson<PacketWire>(json); }
            catch (ArgumentException) { return false; }
            if (parsed == null) return false;
            // Sin `version` se asume la actual, igual que en protocol.py; cualquier otra se rechaza.
            if (parsed.version != 0 && parsed.version != Version) return false;
            if (parsed.brake == null || parsed.accelerator == null) return false;
            packet = new FootPacket(parsed.sequence, parsed.timestamp, parsed.calibrated,
                parsed.brake.pressed, parsed.brake.confidence, parsed.brake.value,
                parsed.accelerator.pressed, parsed.accelerator.confidence, parsed.accelerator.value,
                parsed.version == 0 ? Version : parsed.version);
            return true;
        }

        public static string Serialize(FootPacket packet) =>
            "{\"version\":" + packet.Version +
            ",\"sequence\":" + packet.Sequence +
            ",\"timestamp\":" + Number(packet.Timestamp) +
            ",\"calibrated\":" + (packet.Calibrated ? "true" : "false") +
            ",\"brake\":" + Pedal(packet.BrakePressed, packet.BrakeConfidence, packet.BrakeValue) +
            ",\"accelerator\":" + Pedal(packet.AcceleratorPressed, packet.AcceleratorConfidence, packet.AcceleratorValue) + "}";

        /// <summary>Atajo para el harness y las pruebas: un estado Up/Down/Unknown por pedal.</summary>
        public static FootPacket FromStates(int sequence, FootState brake, FootState accelerator, double timestamp = 0.0, bool calibrated = true) =>
            new FootPacket(sequence, timestamp, calibrated,
                brake == FootState.Down, brake == FootState.Unknown ? 0f : 1f, brake == FootState.Down ? 0f : 1f,
                accelerator == FootState.Down, accelerator == FootState.Unknown ? 0f : 1f, accelerator == FootState.Down ? 0f : 1f,
                Version);

        static string Pedal(bool pressed, float confidence, float value) =>
            "{\"pressed\":" + (pressed ? "true" : "false") +
            ",\"confidence\":" + Number(confidence) +
            ",\"value\":" + Number(value) + "}";

        static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        public static string Text(FootState state) => state == FootState.Up ? "up" : state == FootState.Down ? "down" : "unknown";

        public static bool IsNewer(int previous, int next)
        {
            uint difference = unchecked((uint)(next - previous));
            return difference != 0 && difference < 0x80000000u;
        }
    }

    public sealed class FootStateMachine
    {
        public const int DebounceSamples = 3;
        public const float GracePeriod = .1f;
        public const float LossWarningTime = .5f;

        sealed class Side
        {
            public FootState Stable = FootState.Down;
            public FootState Candidate = FootState.Down;
            public int Count;
            public bool Known;
            public float Lost;
            public bool Available => Known && Lost <= GracePeriod;
        }

        readonly Side red = new();
        readonly Side green = new();
        float invalidTime;
        bool tracked;

        public bool Armed { get; private set; }
        public FootState Red => red.Stable;
        public FootState Green => green.Stable;
        public bool RedValid => red.Available;
        public bool GreenValid => green.Available;
        public bool TrackingValid { get; private set; } = true;
        public bool Warning { get; private set; }
        public bool Throttle { get; private set; }
        public bool Brake { get; private set; }

        public void Tick(FootState rawRed, FootState rawGreen, bool redValid, bool greenValid, float deltaTime)
        {
            Observe(red, redValid ? rawRed : FootState.Unknown, deltaTime);
            Observe(green, greenValid ? rawGreen : FootState.Unknown, deltaTime);
            if (!Armed && (red.Stable == FootState.Up || green.Stable == FootState.Up)) Armed = true;
            TrackingValid = red.Available && green.Available;
            // El aviso es para quien estaba siguiendo y pierde los marcadores, no para quien no ha enchufado
            // nunca la camara: sin datos el juego no dice nada y el mando vuelve al teclado.
            if (TrackingValid) { tracked = true; invalidTime = 0; Warning = false; }
            else { invalidTime += deltaTime; if (tracked && invalidTime > LossWarningTime) Warning = true; }
            Pedals(out bool throttle, out bool brake);
            Throttle = throttle; Brake = brake;
        }

        public void Pedals(out bool throttle, out bool brake) =>
            FootPedals.Table(Armed, green.Available ? green.Stable : FootState.Unknown, red.Available ? red.Stable : FootState.Unknown, out throttle, out brake);

        public void Reset()
        {
            Armed = false; TrackingValid = true; Warning = false; Throttle = false; Brake = false; invalidTime = 0; tracked = false;
            Reset(red); Reset(green);
        }

        static void Reset(Side side) { side.Stable = side.Candidate = FootState.Down; side.Count = 0; side.Known = false; side.Lost = 0; }

        static void Observe(Side side, FootState value, float deltaTime)
        {
            if (value == FootState.Unknown) { side.Lost += deltaTime; return; }
            side.Lost = 0; side.Known = true;
            Step(side, value);
        }

        static void Step(Side side, FootState value)
        {
            if (value == side.Stable) { side.Candidate = value; side.Count = 0; return; }
            if (value != side.Candidate) { side.Candidate = value; side.Count = 1; return; }
            side.Count++;
            if (side.Count >= DebounceSamples) { side.Stable = value; side.Count = 0; }
        }
    }

    public static class FootPedals
    {
        public const float RampSeconds = .2f;

        public static void Table(bool armed, FootState green, FootState red, out bool throttle, out bool brake)
        {
            throttle = armed && green == FootState.Down;
            brake = armed && red == FootState.Down;
        }

        public static float Approach(float current, float target, float rampSeconds, float deltaTime) =>
            rampSeconds <= 0 ? target : Mathf.MoveTowards(current, target, deltaTime / rampSeconds);

        public static bool Skid(bool throttle, bool brake) => throttle && brake;

        /// <summary>Reparto de mando entre el tracker y el resto de controles. Es una prioridad, no una suma:
        /// mientras llegan paquetes frescos manda el tracker y el teclado y los mandos no tocan acelerador ni
        /// freno; cuando el socket calla, vuelven ellos. Si se sumaran, pisar el freno con el pie no impediria
        /// que W acelerase a la vez.</summary>
        public static void Source(bool tracking, bool footThrottle, bool footBrake, float manualThrottle, float manualBrake, out float throttle, out float brake)
        {
            throttle = tracking ? (footThrottle ? 1f : 0f) : manualThrottle;
            brake = tracking ? (footBrake ? 1f : 0f) : manualBrake;
        }
    }
}
