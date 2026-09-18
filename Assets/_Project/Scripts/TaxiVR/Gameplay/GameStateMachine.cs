using UnityEngine;

namespace TaxiVR.Gameplay
{
    /// <summary>Estados de la partida, en el mismo orden que la especificacion.</summary>
    public enum GamePhase
    {
        Boot,
        WaitForPedalTracker,
        WakeUp,
        BriefPrinted,
        WaitingPassenger,
        PassengerBoards,
        TripActive,
        Arrived,
        NormalEnding,
        PassengerChasing,
        PassengerBoardsWithPenalty,
        PoliceChase,
        PoliceCaught,
        PoliceEscaped
    }

    /// <summary>Maquina de estados principal. Logica pura: cada transicion se pide explicitamente y se
    /// rechaza si el estado actual no la permite, de modo que el orden del guion no dependa del orden en que
    /// lleguen los eventos de Unity.</summary>
    public sealed class GameStateMachine
    {
        public GamePhase Phase { get; private set; } = GamePhase.Boot;
        public float PhaseSeconds { get; private set; }
        public int PassengersServed { get; private set; }
        public int PoliceEscapes { get; private set; }
        public int TimesCaught { get; private set; }
        public EndingKind LastEnding { get; private set; } = EndingKind.GoodOnTime;

        public bool Terminal => Phase is GamePhase.NormalEnding or GamePhase.PoliceCaught or GamePhase.PoliceEscaped;
        public bool Driving => Phase == GamePhase.TripActive;
        public bool PassengerInside => Phase is GamePhase.PassengerBoards or GamePhase.PassengerBoardsWithPenalty or GamePhase.TripActive;
        public bool FreeToLeave => Phase is GamePhase.WaitingPassenger or GamePhase.PassengerChasing;

        public void Tick(float deltaTime)
        {
            PhaseSeconds += deltaTime;
            if (Phase == GamePhase.Boot) Enter(GamePhase.WaitForPedalTracker);
        }

        public bool TrackerReady() => Phase == GamePhase.WaitForPedalTracker && Enter(GamePhase.WakeUp);
        public bool WakeUpFinished() => Phase == GamePhase.WakeUp && Enter(GamePhase.BriefPrinted);
        public bool BriefReady() => Phase == GamePhase.BriefPrinted && Enter(GamePhase.WaitingPassenger);

        public bool PassengerBoards(bool penalised)
        {
            if (Phase == GamePhase.WaitingPassenger) { Enter(GamePhase.PassengerBoards); return true; }
            if (Phase == GamePhase.PassengerChasing) { Enter(GamePhase.PassengerBoardsWithPenalty); return true; }
            return false;
        }

        public bool BeginTrip()
        {
            if (Phase is GamePhase.PassengerBoards or GamePhase.PassengerBoardsWithPenalty) { Enter(GamePhase.TripActive); return true; }
            return false;
        }

        public bool PassengerFlees() => Phase == GamePhase.WaitingPassenger && Enter(GamePhase.PassengerChasing);

        public bool PoliceTriggered() => Phase == GamePhase.PassengerChasing && Enter(GamePhase.PoliceChase);

        public bool Arrive()
        {
            if (Phase != GamePhase.TripActive) return false;
            Enter(GamePhase.Arrived);
            PassengersServed++;
            return true;
        }

        public bool Finish(EndingKind ending)
        {
            LastEnding = ending;
            if (Phase == GamePhase.Arrived) { Enter(GamePhase.NormalEnding); return true; }
            if (Phase == GamePhase.PoliceChase)
            {
                if (ending == EndingKind.PoliceCaught) { TimesCaught++; Enter(GamePhase.PoliceCaught); return true; }
                PoliceEscapes++;
                Enter(GamePhase.PoliceEscaped);
                return true;
            }
            return false;
        }

        /// <summary>Vuelve a la espera de pasajero sin cambiar de escena.</summary>
        public void Restart() => Enter(GamePhase.WaitingPassenger);

        public void Reset()
        {
            PhaseSeconds = 0f;
            Phase = GamePhase.Boot;
        }

        bool Enter(GamePhase next)
        {
            Phase = next;
            PhaseSeconds = 0f;
            return true;
        }
    }

    /// <summary>Guion del arranque: pantalla negra, apertura de ojos y la impresora de la hoja. Se expone
    /// como funcion del tiempo para poder probarlo sin esperar nueve segundos reales.</summary>
    public static class StartupSequence
    {
        public const float BlackoutUntil = 5f;
        public const float EyeOpenUntil = 6.5f;
        public const float PrinterStarts = 7f;
        public const float PrinterEnds = 9f;
        public const float TotalSeconds = PrinterEnds;

        /// <summary>0 = negro absoluto, 1 = vision completa. La transicion radial ocurre entre 5 y 6.5 s.</summary>
        public static float Vision(float elapsed) =>
            elapsed <= BlackoutUntil ? 0f : elapsed >= EyeOpenUntil ? 1f : Mathf.SmoothStep(0f, 1f, (elapsed - BlackoutUntil) / (EyeOpenUntil - BlackoutUntil));

        /// <summary>0 = sin abrir, 1 = completamente abierto. Radio normalizado del iris.</summary>
        public static float Iris(float elapsed) => Vision(elapsed);

        public static bool PrinterRunning(float elapsed) => elapsed >= PrinterStarts && elapsed < PrinterEnds;

        /// <summary>Progreso de impresion 0..1.</summary>
        public static float PrintProgress(float elapsed) =>
            elapsed <= PrinterStarts ? 0f : elapsed >= PrinterEnds ? 1f : (elapsed - PrinterStarts) / (PrinterEnds - PrinterStarts);

        public static bool Finished(float elapsed) => elapsed >= TotalSeconds;
    }
}
