using System;
using UnityEngine;

namespace TaxiVR.Playable
{
    public enum FootState { Up, Down, Unknown }

    public readonly struct FootPacket
    {
        public readonly int Sequence;
        public readonly FootState Red;
        public readonly FootState Green;
        public readonly bool RedValid;
        public readonly bool GreenValid;
        public readonly int Version;
        public FootPacket(int sequence, FootState red, FootState green, bool redValid, bool greenValid, int version)
        { Sequence = sequence; Red = red; Green = green; RedValid = redValid; GreenValid = greenValid; Version = version; }
    }

    public static class FootProtocol
    {
        public const int Version = 1;
        public const int Port = 5055;
        public const int RatePerSecond = 30;
        public const float StaleSeconds = 1f;

        [Serializable]
        sealed class Packet
        {
            public int version;
            public int seq;
            public string red;
            public string green;
            public bool redValid;
            public bool greenValid;
        }

        public static bool TryParse(string json, out FootPacket packet)
        {
            packet = default;
            if (string.IsNullOrWhiteSpace(json) || json[0] != '{') return false;
            Packet parsed;
            try { parsed = JsonUtility.FromJson<Packet>(json); }
            catch (ArgumentException) { return false; }
            if (parsed == null) return false;
            if (parsed.version != 0 && parsed.version != Version) return false;
            if (!TryState(parsed.red, out var red) || !TryState(parsed.green, out var green)) return false;
            packet = new FootPacket(parsed.seq, red, green, parsed.redValid, parsed.greenValid, parsed.version == 0 ? Version : parsed.version);
            return true;
        }

        public static string Serialize(FootPacket packet) =>
            $"{{\"version\":{packet.Version},\"seq\":{packet.Sequence},\"red\":\"{Text(packet.Red)}\",\"green\":\"{Text(packet.Green)}\",\"redValid\":{(packet.RedValid ? "true" : "false")},\"greenValid\":{(packet.GreenValid ? "true" : "false")}}}";

        public static string Text(FootState state) => state == FootState.Up ? "up" : state == FootState.Down ? "down" : "unknown";

        public static bool TryState(string value, out FootState state)
        {
            switch (value)
            {
                case "up": state = FootState.Up; return true;
                case "down": state = FootState.Down; return true;
                case "unknown": state = FootState.Unknown; return true;
                default: state = FootState.Unknown; return false;
            }
        }

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
