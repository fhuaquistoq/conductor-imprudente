using UnityEngine;

namespace TaxiVR.Gameplay
{
    /// <summary>Un viaje en curso: pasajero, ruta, peticiones, conformidad y cobro. Es logica pura para poder
    /// probar el reglamento completo sin arrancar la escena. El MonoBehaviour que lo conduce solo aporta el
    /// tiempo y las acciones del jugador.</summary>
    public sealed class TripSession
    {
        public const float RequestWindow = 20f;

        public readonly struct RequestSlot
        {
            public readonly float OpensAt;
            public readonly RequestKind Kind;
            public RequestSlot(float opensAt, RequestKind kind) { OpensAt = opensAt; Kind = kind; }
        }

        /// <summary>Las tres peticiones programadas de la especificacion.</summary>
        public static readonly RequestSlot[] Schedule =
        {
            new(55f, RequestKind.Food),
            new(115f, RequestKind.Radio),
            new(175f, RequestKind.Temperature)
        };

        public PassengerProfile Profile { get; }
        public ScoreSystem Score { get; } = new ScoreSystem();
        public float OptimalEta { get; }
        public float RouteMetres { get; }
        public float Deadline { get; }
        public float Elapsed { get; private set; }

        public RequestKind? PendingRequest { get; private set; }
        public float RequestRemaining { get; private set; }
        public int PendingIndex { get; private set; } = -1;

        public readonly RequestOutcome[] RequestResults = { RequestOutcome.Ignored, RequestOutcome.Ignored, RequestOutcome.Ignored };

        public float SpeedCompliantSeconds { get; private set; }
        public float TemperatureComfortSeconds { get; private set; }
        public float TimedSeconds { get; private set; }
        public float SpeechSeconds { get; private set; }

        public bool Finished { get; private set; }
        public EndingKind Ending { get; private set; }
        public float Payment { get; private set; }

        int nextRequest;

        public TripSession(PassengerProfile profile, float optimalEtaSeconds, float routeMetres)
        {
            Profile = profile;
            OptimalEta = Mathf.Max(1f, optimalEtaSeconds);
            RouteMetres = Mathf.Max(0f, routeMetres);
            Deadline = Gameplay.Deadlines.Deadline(OptimalEta, profile.Urgent);
        }

        public bool Overdue => Elapsed > Deadline;
        public float Remaining => Deadline - Elapsed;

        /// <summary>Estacion preferida del pasajero. Da a la peticion de radio una respuesta correcta concreta
        /// en lugar de "cualquier emisora vale".</summary>
        public int PreferredStation => Profile.Voice % 3;

        /// <summary>Avanza el reloj del viaje. <paramref name="speedKmh"/> es la velocidad actual del taxi,
        /// <paramref name="climate"/> la temperatura logica (-1 frio, 0 neutro, +1 calido) y
        /// <paramref name="speaking"/> si el microfono detecta voz del conductor.</summary>
        public void Tick(float deltaTime, float speedKmh, int climate, bool speaking)
        {
            if (Finished) return;
            Elapsed += deltaTime;
            TimedSeconds += deltaTime;

            if (speedKmh >= Profile.MinimumSpeedKmh && speedKmh <= Profile.MaximumSpeedKmh) SpeedCompliantSeconds += deltaTime;

            int wanted = Profile.Temperature == TemperaturePreference.Cold ? -1 : 1;
            if (climate == wanted) TemperatureComfortSeconds += deltaTime;

            if (speaking) SpeechSeconds += deltaTime;

            UpdateRequests(deltaTime);
        }

        void UpdateRequests(float deltaTime)
        {
            if (PendingRequest.HasValue)
            {
                RequestRemaining -= deltaTime;
                if (RequestRemaining <= 0f)
                {
                    RequestResults[PendingIndex] = RequestOutcome.Ignored;
                    Score.Apply(Compliance.RequestPenalty(RequestOutcome.Ignored));
                    ClearRequest();
                }
                return;
            }
            while (nextRequest < Schedule.Length && Elapsed >= Schedule[nextRequest].OpensAt)
            {
                PendingRequest = Schedule[nextRequest].Kind;
                PendingIndex = nextRequest;
                RequestRemaining = RequestWindow;
                nextRequest++;
                return;
            }
        }

        public void ClearRequest()
        {
            PendingRequest = null;
            PendingIndex = -1;
            RequestRemaining = 0f;
        }

        /// <summary>Respuesta del jugador a la peticion en curso. <paramref name="option"/> es el indice del
        /// objeto entregado (comida), la emisora puesta (radio) o la temperatura logica (clima).</summary>
        public RequestOutcome Answer(RequestKind kind, int option)
        {
            if (!PendingRequest.HasValue || PendingRequest.Value != kind) return RequestOutcome.Ignored;
            bool correct = kind switch
            {
                RequestKind.Food => Profile.Accepts((FoodItem)option),
                RequestKind.Radio => option == PreferredStation,
                _ => option == (Profile.Temperature == TemperaturePreference.Cold ? -1 : 1)
            };
            var outcome = correct ? RequestOutcome.Correct : RequestOutcome.Incorrect;
            RequestResults[PendingIndex] = outcome;
            Score.Apply(Compliance.RequestPenalty(outcome));
            ClearRequest();
            return outcome;
        }

        public float SpeedCompliance => TimedSeconds <= 0f ? 1f : SpeedCompliantSeconds / TimedSeconds;
        public float TemperatureCompliance => TimedSeconds <= 0f ? 1f : TemperatureComfortSeconds / TimedSeconds;
        public float SpeechSecondsPerMinute => Elapsed <= 0f ? 0f : SpeechSeconds / Elapsed * 60f;

        /// <summary>Cierra el viaje: aplica conformidad y peticiones que quedaran sin resolver, decide calidad
        /// y calcula el cobro.</summary>
        public EndingKind Complete()
        {
            if (Finished) return Ending;
            Finished = true;

            // Una peticion viva al llegar se cuenta como ignorada.
            if (PendingRequest.HasValue)
            {
                RequestResults[PendingIndex] = RequestOutcome.Ignored;
                Score.Apply(Compliance.RequestPenalty(RequestOutcome.Ignored));
                ClearRequest();
            }

            Score.Apply(Compliance.SpeedPenalty(SpeedCompliance));
            Score.Apply(Compliance.TemperaturePenalty(TemperatureCompliance));
            Score.Apply(Compliance.ConversationPenalty(Profile.Conversation, SpeechSecondsPerMinute));

            var quality = Compliance.Quality(Score.Score);
            var time = Overdue ? TimeOutcome.Late : TimeOutcome.OnTime;
            Ending = Gameplay.Ending.Of(quality, time);

            float maximum = Fare.Maximum(RouteMetres, Profile.Urgent);
            Payment = Fare.Payment(maximum, quality, time);
            return Ending;
        }

        public ServiceQuality Quality => Compliance.Quality(Score.Score);
        public TimeOutcome Time => Overdue ? TimeOutcome.Late : TimeOutcome.OnTime;
        public float MaximumFare => Fare.Maximum(RouteMetres, Profile.Urgent);
    }
}
