using UnityEngine;
using TaxiVR.Gameplay;

namespace TaxiVR.Playable
{
    /// <summary>Director de la partida. Posee la maquina de estados, el viaje en curso y el guion de arranque,
    /// de modo que el orden del guion vive en un solo sitio y el resto de sistemas solo reaccionan.</summary>
    public sealed class GameDirector : MonoBehaviour
    {
        public const float TrackerGraceSeconds = 20f;
        public const float BoardingSeconds = 4f;
        public const float GroundedToBoardKmh = 1f;
        public const float FleeKmh = 10f;
        public const float PoliceDistanceMetres = 120f;
        public const float PoliceFleeSeconds = 20f;
        public const float PassengerLeadSeconds = 3f;
        public const float ArrivalHoldSeconds = 2f;
        public const float EndingSeconds = 8f;
        public const float WrongWayInterval = 3f;

        public TaxiDrive Drive;
        public EndlessCity City;
        public TaxiGPS GPS;
        public FootReceiver Feet;
        public PlayerHands Player;
        public InteriorControls Interior;
        public PoliceSystem Police;
        public CityAssets Assets;
        public CityGraph Graph;
        public bool SkipTrackerGate;

        public GameStateMachine Flow { get; } = new GameStateMachine();
        public TripSession Trip { get; private set; }
        public PassengerAgent Passenger { get; private set; }
        public PassengerSheet Sheet { get; private set; }
        public float StartupSeconds { get; private set; }
        public string Status { get; private set; } = "Arrancando";
        public string EndingText { get; private set; }

        EyeShutter shutter;
        float boardingTimer, arrivalTimer, endingTimer, fleeTimer, spawnTimer, trackerTimer, wrongWayTimer;
        Vector2Int lastNode = new(int.MinValue, int.MinValue);
        Vector2Int? lastRedLightNode;
        int passengerIndex;
        int sessionSeed = 20260918;
        float lastStation;
        int lastClimate;

        public int Score => Trip?.Score.Score ?? 100;

        void Start()
        {
            shutter = EyeShutter.Attach(Player?.View);
            shutter?.SetVision(0f);
            sessionSeed = Random.Range(1, 1 << 30);
            Status = "Esperando al FootTracker";
        }

        void Update()
        {
            float delta = Time.deltaTime;
            Flow.Tick(delta);
            switch (Flow.Phase)
            {
                case GamePhase.WaitForPedalTracker: WaitingForTracker(delta); break;
                case GamePhase.WakeUp: WakingUp(delta); break;
                case GamePhase.BriefPrinted: Printing(delta); break;
                case GamePhase.WaitingPassenger: WaitingPassenger(delta); break;
                case GamePhase.PassengerChasing: Chasing(delta); break;
                case GamePhase.PassengerBoards:
                case GamePhase.PassengerBoardsWithPenalty: Boarding(delta); break;
                case GamePhase.TripActive: Driving(delta); break;
                case GamePhase.Arrived: Ending(delta); break;
                case GamePhase.PoliceChase: Pursued(); break;
                case GamePhase.NormalEnding:
                case GamePhase.PoliceCaught:
                case GamePhase.PoliceEscaped: Ending(delta); break;
            }
            Passenger?.Tick(delta, Drive, Graph);
        }

        // ------------------------------------------------------------------ arranque

        void WaitingForTracker(float delta)
        {
            trackerTimer += delta;
            bool ready = SkipTrackerGate || (Feet != null && Feet.TrackingReceived && Feet.Machine.Armed);
            if (!ready && trackerTimer < TrackerGraceSeconds) return;
            trackerTimer = 0f;
            StartupSeconds = 0f;
            Flow.TrackerReady();
            Status = "Despertando";
        }

        void WakingUp(float delta)
        {
            StartupSeconds += delta;
            shutter?.SetVision(StartupSequence.Vision(StartupSeconds));
            if (StartupSequence.Finished(StartupSeconds)) Flow.WakeUpFinished();
        }

        void Printing(float delta)
        {
            StartupSeconds += delta;
            if (Sheet == null && StartupSequence.PrinterRunning(StartupSeconds)) BuildSheet();
            if (Sheet != null) Sheet.SetPrintProgress(StartupSequence.PrintProgress(StartupSeconds));
            if (StartupSequence.Finished(StartupSeconds)) Flow.BriefReady();
        }

        void BuildSheet()
        {
            Sheet = PassengerSheet.Create(City, Assets, PassengerCatalog.Create(passengerIndex, sessionSeed), Drive.transform);
            Status = "Repartiendo";
        }

        // ------------------------------------------------------------------ pasajero

        void WaitingPassenger(float delta)
        {
            spawnTimer += delta;
            if (Passenger == null)
            {
                if (spawnTimer < PassengerLeadSeconds) return;
                SpawnPassenger();
                return;
            }
            Passenger.WalkTowards(Drive.transform);
            float kmh = Mathf.Abs(Drive.Speed) * 3.6f;
            if (kmh > FleeKmh && Passenger.DistanceTo(Drive.transform) > 6f)
            {
                if (Flow.PassengerFlees()) { fleeTimer = 0f; Status = "Te has ido sin el pasajero"; }
                return;
            }
            if (kmh < GroundedToBoardKmh && Passenger.AtDoor(Drive.transform) && Flow.PassengerBoards(false))
            {
                boardingTimer = 0f;
                Passenger.BeginBoarding(Drive.transform);
                Status = "Subiendo";
            }
        }

        void SpawnPassenger()
        {
            Passenger = PassengerAgent.Create(City, Assets, PassengerCatalog.Create(passengerIndex, sessionSeed), Drive.transform);
            Status = "Pasajero acercandose";
        }

        void Chasing(float delta)
        {
            if (Passenger == null) { Flow.Restart(); return; }
            fleeTimer += delta;
            Passenger.RunTowards(Drive.transform);
            float kmh = Mathf.Abs(Drive.Speed) * 3.6f;
            if (kmh < GroundedToBoardKmh && Passenger.AtDoor(Drive.transform))
            {
                ApplyPenalty(PenaltyKind.LatePickupAfterFleeing);
                if (Flow.PassengerBoards(true)) { boardingTimer = 0f; Passenger.BeginBoarding(Drive.transform); Status = "Sube con penalizacion"; }
                return;
            }
            if (Passenger.DistanceTo(Drive.transform) <= PoliceDistanceMetres && fleeTimer <= PoliceFleeSeconds) return;
            if (!Flow.PoliceTriggered()) return;
            Police?.Begin(Drive.Body.position);
            Status = "Persecucion policial";
        }

        void Boarding(float delta)
        {
            boardingTimer += delta;
            Passenger?.SitIn(boardingTimer / BoardingSeconds);
            if (boardingTimer < BoardingSeconds) return;
            if (!Flow.BeginTrip()) return;
            Passenger?.SitDown(Drive.transform);
            StartTrip();
        }

        // ------------------------------------------------------------------ viaje

        void StartTrip()
        {
            var profile = Passenger?.Profile ?? PassengerCatalog.Create(passengerIndex, sessionSeed);
            var node = NodeAt(City.AbsolutePosition);
            var routes = Graph.TryPickDestination(node, passengerIndex + 1 + sessionSeed, 225f, 255f, out var destination, out var found, 4, 32)
                ? found
                : new System.Collections.Generic.List<System.Collections.Generic.List<Vector2Int>>();
            if (routes.Count == 0)
            {
                destination = node + new Vector2Int(30, 24);
                var fallback = Graph.Shortest(node, destination);
                if (fallback.Count > 1) routes.Add(fallback);
            }
            var best = routes.Count > 0 ? routes[0] : new System.Collections.Generic.List<Vector2Int> { node };
            float eta = best.Count > 1 ? Graph.PathEta(best) : 240f;
            float metres = best.Count > 1 ? Graph.PathLength(best) : 3000f;

            Trip = new TripSession(profile, eta, metres);
            GPS.Graph = Graph;
            GPS.SetDestination(destination);
            GPS.Power(true);
            GPS.Replot();
            lastStation = Interior?.Station ?? -1;
            lastClimate = Interior?.Climate ?? 0;
            Status = "Viaje en curso";
        }

        void Driving(float delta)
        {
            if (Trip == null) { Boarding(delta); return; }
            float kmh = Mathf.Abs(Drive.Speed) * 3.6f;
            Trip.Tick(delta, kmh, Interior?.Climate ?? 0, Player != null && Player.Speaking);
            ResolveRequests();
            TrafficRules(delta);

            if (!GPS.Arrived || kmh >= .6f) { arrivalTimer = 0f; return; }
            arrivalTimer += delta;
            if (arrivalTimer < ArrivalHoldSeconds || !Flow.Arrive()) return;
            FinishTrip();
        }

        void FinishTrip()
        {
            Trip.Complete();
            Flow.Finish(Trip.Ending);
            EndingText = $"{Gameplay.Ending.NameOf(Trip.Ending)}\nPago: S/ {Trip.Payment:0.00} de S/ {Trip.MaximumFare:0.00}";
            Status = "Fin del viaje";
            GPS.Power(false);
            Passenger?.Celebrate(Trip.Ending);
        }

        void ResolveRequests()
        {
            if (Trip?.PendingRequest == null) return;
            switch (Trip.PendingRequest.Value)
            {
                case RequestKind.Radio:
                    if (Interior != null && Interior.Station != lastStation) { lastStation = Interior.Station; Trip.Answer(RequestKind.Radio, Interior.Station); }
                    break;
                case RequestKind.Temperature:
                    if (Interior != null && Interior.Climate != lastClimate)
                    {
                        lastClimate = Interior.Climate;
                        if (Interior.Climate != 0) Trip.Answer(RequestKind.Temperature, Interior.Climate);
                    }
                    break;
                case RequestKind.Food:
                    if (Interior != null && Interior.TryConsumeFoodOffer(out var item)) Trip.Answer(RequestKind.Food, (int)item);
                    break;
            }
        }

        void TrafficRules(float delta)
        {
            var node = NodeAt(City.AbsolutePosition);
            if (node != lastNode) { lastNode = node; CheckRedLight(node); }
            wrongWayTimer = WrongWay() ? wrongWayTimer + delta : 0f;
            if (wrongWayTimer < WrongWayInterval) return;
            wrongWayTimer = 0f;
            ApplyPenalty(PenaltyKind.WrongWay);
        }

        void CheckRedLight(Vector2Int node)
        {
            var forward = Drive.transform.forward;
            if (Mathf.Abs(forward.x) < .05f && Mathf.Abs(forward.z) < .05f) return;
            bool northSouth = Mathf.Abs(forward.z) >= Mathf.Abs(forward.x);
            if (CityMath.SignalGreen(northSouth, Time.time) || lastRedLightNode == node) return;
            lastRedLightNode = node;
            ApplyPenalty(PenaltyKind.RedLight);
        }

        bool WrongWay()
        {
            if (Graph == null) return false;
            var forward = Drive.transform.forward;
            var node = NodeAt(City.AbsolutePosition);
            var step = Mathf.Abs(forward.x) >= Mathf.Abs(forward.z)
                ? new Vector2Int(forward.x >= 0 ? 1 : -1, 0)
                : new Vector2Int(0, forward.z >= 0 ? 1 : -1);
            var next = node + step;
            if (!Graph.Exists(node, next)) return false;
            var edge = Graph.Describe(node, next);
            if (edge.Direction != RoadDirection.Forward && edge.Direction != RoadDirection.Backward) return false;
            return !edge.Allows(node, next);
        }

        public void ApplyPenalty(PenaltyKind kind) => Trip?.Score.Apply(kind);

        /// <summary>Solo para la verificacion automatica: entra en viaje sin esperar al pasajero ni a la
        /// impresora, para poder comprobar ruta, peticiones y entrega en un ejecutable de prueba.</summary>
        public void ForceTrip()
        {
            Flow.Restart();
            Flow.PassengerBoards(false);
            Flow.BeginTrip();
            StartupSeconds = StartupSequence.TotalSeconds;
            shutter?.SetVision(1f);
            StartTrip();
        }

        public void Report(VehicleEventKind kind)
        {
            switch (kind)
            {
                case VehicleEventKind.MinorBump: ApplyPenalty(PenaltyKind.TrashOrProp); break;
                case VehicleEventKind.SevereCrash: ApplyPenalty(PenaltyKind.SevereVehicleCollision); break;
                case VehicleEventKind.PedestrianHit: ApplyPenalty(PenaltyKind.PedestrianHit); break;
            }
            Passenger?.React(kind);
        }

        public void Report(PenaltyKind kind)
        {
            ApplyPenalty(kind);
            Passenger?.React(kind switch
            {
                PenaltyKind.PedestrianHit => VehicleEventKind.PedestrianHit,
                PenaltyKind.SevereVehicleCollision => VehicleEventKind.SevereCrash,
                _ => VehicleEventKind.MinorBump
            });
        }

        // ------------------------------------------------------------------ persecucion y finales

        void Pursued()
        {
            if (Police == null) return;
            if (Police.Verdict == PoliceVerdict.Caught && Flow.Finish(EndingKind.PoliceCaught))
                FinishPolice(Gameplay.Ending.NameOf(EndingKind.PoliceCaught));
            else if (Police.Verdict == PoliceVerdict.Escaped && Flow.Finish(EndingKind.PoliceEscaped))
                FinishPolice(Gameplay.Ending.NameOf(EndingKind.PoliceEscaped));
        }

        void FinishPolice(string text)
        {
            EndingText = text;
            Status = text;
            GPS.Power(false);
            Police.Halt();
        }

        void Ending(float delta)
        {
            endingTimer += delta;
            if (endingTimer >= EndingSeconds) Restart();
        }

        public void Restart()
        {
            endingTimer = boardingTimer = arrivalTimer = fleeTimer = spawnTimer = wrongWayTimer = 0f;
            lastNode = new Vector2Int(int.MinValue, int.MinValue);
            lastRedLightNode = null;
            Trip = null;
            EndingText = null;
            if (Passenger != null) Destroy(Passenger.gameObject);
            if (Sheet != null) Destroy(Sheet.gameObject);
            Passenger = null;
            Sheet = null;
            Police?.Halt();
            GPS.Power(false);
            Interior?.ResetForNewPassenger();
            passengerIndex = (passengerIndex + 1) % PassengerCatalog.Count;
            Flow.Restart();
            Status = "Esperando pasajero";
        }

        Vector2Int NodeAt(Vector3 world) =>
            new(Mathf.RoundToInt(world.x / CityGraph.BlockSize), Mathf.RoundToInt(world.z / CityGraph.BlockSize));
    }

    public enum VehicleEventKind { MinorBump, SevereCrash, PedestrianHit, HardBrake, NearMiss, Speeding, GoodDriving }
}
