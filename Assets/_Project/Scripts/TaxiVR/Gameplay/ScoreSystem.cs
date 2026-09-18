using UnityEngine;

namespace TaxiVR.Gameplay
{
    public enum PenaltyKind
    {
        TrashOrProp,
        Barrier,
        TreeOrBuilding,
        CivilianVehicle,
        SevereVehicleCollision,
        PedestrianHit,
        RedLight,
        WrongWay,
        TaxiRecovery,
        LatePickupAfterFleeing
    }

    public enum RequestOutcome { Correct, Ignored, Incorrect }
    public enum ServiceQuality { Good, Bad }
    public enum TimeOutcome { OnTime, Late }

    /// <summary>Desenlaces de la partida. Los cuatro normales mas los dos policiales.</summary>
    public enum EndingKind { GoodOnTime, GoodLate, BadOnTime, BadLate, PoliceCaught, PoliceEscaped }

    /// <summary>Tabla de penalizaciones de la especificacion. Valores fijos, sin tunear por escena.</summary>
    public static class Penalties
    {
        public static int Of(PenaltyKind kind) => kind switch
        {
            PenaltyKind.TrashOrProp => -1,
            PenaltyKind.Barrier => -2,
            PenaltyKind.TreeOrBuilding => -3,
            PenaltyKind.CivilianVehicle => -3,
            PenaltyKind.SevereVehicleCollision => -6,
            PenaltyKind.PedestrianHit => -15,
            PenaltyKind.RedLight => -3,
            PenaltyKind.WrongWay => -1,
            PenaltyKind.TaxiRecovery => -10,
            _ => -10
        };
    }

    /// <summary>Acumulador de puntuacion. Empieza en 100 y nunca sale de [0,100].</summary>
    public sealed class ScoreSystem
    {
        public const int Initial = 100;
        public const int Minimum = 0;
        public const int Maximum = 100;

        public int Score { get; private set; } = Initial;

        public void Reset() => Score = Initial;

        public int Apply(PenaltyKind kind) => Apply(Penalties.Of(kind));

        public int Apply(int amount)
        {
            Score = Mathf.Clamp(Score + amount, Minimum, Maximum);
            return Score;
        }
    }

    /// <summary>Reglas de conformidad. Se aislan del viaje para poder probarlas sin simular una partida.</summary>
    public static class Compliance
    {
        /// <summary>Fraccion de tiempo dentro de la banda de velocidad preferida.</summary>
        public static int SpeedPenalty(float compliantFraction)
        {
            if (compliantFraction >= .75f) return 0;
            if (compliantFraction >= .60f) return -3;
            if (compliantFraction >= .40f) return -7;
            return -12;
        }

        /// <summary>Fraccion de tiempo con la temperatura a gusto del pasajero.</summary>
        public static int TemperaturePenalty(float comfortFraction)
        {
            if (comfortFraction >= .75f) return 0;
            if (comfortFraction >= .50f) return -3;
            if (comfortFraction >= .25f) return -6;
            return -10;
        }

        /// <summary>Segundos de habla del jugador por minuto de viaje.</summary>
        public static int ConversationPenalty(ConversationPreference preference, float secondsPerMinute)
        {
            if (preference == ConversationPreference.Talkative)
                return secondsPerMinute >= 12f ? 0 : secondsPerMinute >= 6f ? -3 : -8;
            return secondsPerMinute <= 5f ? 0 : secondsPerMinute <= 12f ? -3 : -8;
        }

        public static int RequestPenalty(RequestOutcome outcome) => outcome switch
        {
            RequestOutcome.Correct => 0,
            RequestOutcome.Ignored => -5,
            _ => -8
        };

        public static ServiceQuality Quality(int score) => score >= 60 ? ServiceQuality.Good : ServiceQuality.Bad;
    }

    /// <summary>Tarifa y cobro. La tarifa maxima depende de la distancia real y de la urgencia.</summary>
    public static class Fare
    {
        public const float Base = 8f;
        public const float PerKilometre = 6f;
        public const float UrgentBonus = 5f;
        public const float MinimumFare = 18f;
        public const float MaximumFare = 35f;

        public static float Maximum(float routeMetres, bool urgent)
        {
            float amount = Base + routeMetres / 1000f * PerKilometre + (urgent ? UrgentBonus : 0f);
            return Mathf.Clamp(amount, MinimumFare, MaximumFare);
        }

        public static float Payment(float maximum, ServiceQuality quality, TimeOutcome time)
        {
            float multiplier = quality == ServiceQuality.Good
                ? (time == TimeOutcome.OnTime ? 1f : .80f)
                : (time == TimeOutcome.OnTime ? .60f : .35f);
            return maximum * multiplier;
        }

        public static float Multiplier(ServiceQuality quality, TimeOutcome time) =>
            quality == ServiceQuality.Good
                ? (time == TimeOutcome.OnTime ? 1f : .80f)
                : (time == TimeOutcome.OnTime ? .60f : .35f);
    }

    /// <summary>Plazos del viaje. El urgente tiene muy poco margen.</summary>
    public static class Deadlines
    {
        public const float NormalGrace = 75f;
        public const float UrgentGrace = 20f;

        public static float Allowance(bool urgent) => urgent ? UrgentGrace : NormalGrace;

        public static float Deadline(float initialEtaSeconds, bool urgent) => initialEtaSeconds + Allowance(urgent);
    }

    public static class Ending
    {
        public static EndingKind Of(ServiceQuality quality, TimeOutcome time) => (quality, time) switch
        {
            (ServiceQuality.Good, TimeOutcome.OnTime) => EndingKind.GoodOnTime,
            (ServiceQuality.Good, TimeOutcome.Late) => EndingKind.GoodLate,
            (ServiceQuality.Bad, TimeOutcome.OnTime) => EndingKind.BadOnTime,
            _ => EndingKind.BadLate
        };

        public static string NameOf(EndingKind kind) => kind switch
        {
            EndingKind.GoodOnTime => "Buen servicio, a tiempo",
            EndingKind.GoodLate => "Buen servicio, con retraso",
            EndingKind.BadOnTime => "Mal servicio, a tiempo",
            EndingKind.BadLate => "Mal servicio, con retraso",
            EndingKind.PoliceCaught => "Atrapado por la policia",
            _ => "Escapaste de la policia"
        };
    }
}
