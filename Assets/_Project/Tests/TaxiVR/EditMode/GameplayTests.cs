using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using TaxiVR.Gameplay;

namespace TaxiVR.Tests.EditMode
{
    public class CityGraphTests
    {
        static readonly Vector2Int Origin = new(0, 0);

        [Test]
        public void EveryReachableCornerOfTheLogicalCityIsConnected()
        {
            var graph = new CityGraph(7);
            foreach (var target in new[] { new Vector2Int(30, 30), new Vector2Int(-25, 17), new Vector2Int(40, -40), new Vector2Int(-33, -12) })
            {
                var route = graph.Shortest(Origin, target);
                Assert.IsNotEmpty(route, "Sin ruta hasta " + target);
                Assert.AreEqual(target, route[route.Count - 1]);
            }
        }

        [Test]
        public void PresenceIsDeterministicForTheSameSeed()
        {
            var a = new CityGraph(11);
            var b = new CityGraph(11);
            for (int x = -6; x <= 6; x++)
                for (int z = -6; z <= 6; z++)
                    Assert.AreEqual(a.Exists(new Vector2Int(x, z), new Vector2Int(x + 1, z)),
                                    b.Exists(new Vector2Int(x, z), new Vector2Int(x + 1, z)));
        }

        [Test]
        public void DifferentSeedsProduceDifferentLayouts()
        {
            var a = new CityGraph(1);
            var b = new CityGraph(2);
            int differences = 0;
            for (int x = -20; x <= 20; x++)
                for (int z = -20; z <= 20; z++)
                    if (a.Exists(new Vector2Int(x, z), new Vector2Int(x, z + 1)) != b.Exists(new Vector2Int(x, z), new Vector2Int(x, z + 1))) differences++;
            Assert.Greater(differences, 0);
        }

        [Test]
        public void DensityStaysNearTheConfiguredConnectivity()
        {
            var graph = new CityGraph(3, .72f);
            int present = 0, total = 0;
            for (int x = -40; x <= 40; x++)
                for (int z = -40; z <= 40; z++)
                {
                    var node = new Vector2Int(x, z);
                    total += 2;
                    if (graph.Exists(node, node + Vector2Int.right)) present++;
                    if (graph.Exists(node, node + Vector2Int.up)) present++;
                }
            float density = present / (float)total;
            Assert.That(density, Is.InRange(.60f, .84f), "Densidad medida " + density);
        }

        [Test]
        public void OneWayEdgesOnlyAllowTheirOwnDirection()
        {
            var graph = new CityGraph(5);
            bool found = false;
            for (int x = -30; x <= 30 && !found; x++)
                for (int z = -30; z <= 30 && !found; z++)
                {
                    var a = new Vector2Int(x, z);
                    var b = a + Vector2Int.right;
                    if (!graph.Exists(a, b)) continue;
                    var edge = graph.Describe(a, b);
                    if (edge.Direction == RoadDirection.TwoWay || edge.Direction == RoadDirection.Closed) continue;
                    found = true;
                    bool forward = graph.TryStep(edge.A, edge.B, out _);
                    bool backward = graph.TryStep(edge.B, edge.A, out _);
                    Assert.AreNotEqual(forward, backward, "Un sentido unico debe permitir exactamente un recorrido");
                }
            Assert.IsTrue(found, "No se encontro ninguna calle de sentido unico");
        }

        [Test]
        public void ClosedEdgesAreNeverDrivable()
        {
            var graph = new CityGraph(9);
            int closed = 0;
            for (int x = -40; x <= 40; x++)
                for (int z = -40; z <= 40; z++)
                {
                    var a = new Vector2Int(x, z);
                    var b = a + Vector2Int.right;
                    if (!graph.Exists(a, b) || graph.Describe(a, b).Direction != RoadDirection.Closed) continue;
                    closed++;
                    Assert.IsFalse(graph.TryStep(a, b, out _));
                    Assert.IsFalse(graph.TryStep(b, a, out _));
                }
            Assert.Greater(closed, 0, "El generador debe producir cierres iniciales");
        }

        [Test]
        public void RoutesOnlyUseLegalSingleSteps()
        {
            var graph = new CityGraph(13);
            var route = graph.Shortest(Origin, new Vector2Int(26, 19));
            Assert.IsNotEmpty(route);
            for (int i = 1; i < route.Count; i++)
            {
                Assert.AreEqual(1, Mathf.Abs(route[i].x - route[i - 1].x) + Mathf.Abs(route[i].y - route[i - 1].y));
                Assert.IsTrue(graph.TryStep(route[i - 1], route[i], out _), "Paso ilegal en la ruta");
            }
        }

        [Test]
        public void ShortestPathIsNoSlowerThanAnyAlternative()
        {
            var graph = new CityGraph(17);
            var goal = new Vector2Int(18, -14);
            var optimal = graph.PathEta(graph.Shortest(Origin, goal));
            var routes = graph.KShortest(Origin, goal, 4);
            Assert.GreaterOrEqual(routes.Count, 2);
            foreach (var route in routes) Assert.GreaterOrEqual(graph.PathEta(route), optimal - .001f);
        }

        [Test]
        public void KShortestReturnsDistinctRoutesOrderedByTime()
        {
            var graph = new CityGraph(19);
            var routes = graph.KShortest(Origin, new Vector2Int(20, 16), 5);
            Assert.GreaterOrEqual(routes.Count, 3);
            var signatures = routes.Select(r => string.Join(",", r.Select(n => n.x + ":" + n.y))).ToList();
            CollectionAssert.AllItemsAreUnique(signatures);
            for (int i = 1; i < routes.Count; i++)
                Assert.GreaterOrEqual(graph.PathEta(routes[i]), graph.PathEta(routes[i - 1]) - .001f);
        }

        [Test]
        public void EdgeOverlapIsOneForIdenticalRoutesAndZeroForDisjointOnes()
        {
            var graph = new CityGraph(23);
            var route = graph.Shortest(Origin, new Vector2Int(10, 10));
            Assert.AreEqual(1f, CityGraph.EdgeOverlap(route, route), .001f);
            var elsewhere = graph.Shortest(new Vector2Int(200, 200), new Vector2Int(210, 210));
            Assert.AreEqual(0f, CityGraph.EdgeOverlap(route, elsewhere), .001f);
        }

        [Test]
        public void ChosenDestinationAlwaysOffersFourViableRoutesInsideTheEtaWindow()
        {
            var graph = new CityGraph(29);
            Assert.IsTrue(graph.TryPickDestination(Origin, 4, 225f, 255f, out var destination, out var routes, 4, 32),
                "No se encontro destino con cuatro rutas viables");
            Assert.GreaterOrEqual(routes.Count, 4);
            float optimal = graph.PathEta(routes[0]);
            Assert.That(optimal, Is.InRange(225f, 255f));
            for (int i = 1; i < routes.Count; i++)
            {
                Assert.LessOrEqual(graph.PathEta(routes[i]), optimal * 1.35f, "Alternativa demasiado lenta");
                Assert.LessOrEqual(CityGraph.EdgeOverlap(routes[i], routes[0]), .75f, "Alternativa demasiado parecida");
            }
        }

        [Test]
        public void ReroutingAroundAClosureStillReachesTheGoal()
        {
            var graph = new CityGraph(31);
            var goal = new Vector2Int(14, 11);
            var route = graph.Shortest(Origin, goal);
            Assert.IsNotEmpty(route);
            var banned = new HashSet<RoadEdge> { RoadEdgeOf(route[0], route[1]) };
            var detour = graph.Shortest(Origin, goal, banned, null);
            Assert.IsNotEmpty(detour);
            Assert.AreEqual(goal, detour[detour.Count - 1]);
            for (int i = 1; i < detour.Count; i++) Assert.IsFalse(banned.Contains(RoadEdgeOf(detour[i - 1], detour[i])), "La ruta alternativa reutiliza la calle cerrada");
        }

        static RoadEdge RoadEdgeOf(Vector2Int a, Vector2Int b)
        {
            if (a.x > b.x || (a.x == b.x && a.y > b.y)) (a, b) = (b, a);
            return new RoadEdge(a, b, RoadKind.Street, RoadDirection.TwoWay);
        }

        [Test]
        public void TrafficNeverExceedsTheConfiguredMultiplier()
        {
            var graph = new CityGraph(37) { MaxTrafficMultiplier = 2.5f };
            for (int x = -20; x <= 20; x++)
                for (int z = -20; z <= 20; z++)
                {
                    var node = new Vector2Int(x, z);
                    if (!graph.TryStep(node, node + Vector2Int.right, out _)) continue;
                    float cost = graph.Cost(node, node + Vector2Int.right);
                    Assert.That(cost, Is.InRange(CityGraph.BlockSize / CityGraph.CruiseSpeed - .001f, CityGraph.BlockSize / CityGraph.CruiseSpeed * 2.5f + .001f));
                    Assert.That(graph.Traffic(node, node + Vector2Int.right), Is.InRange(0f, 1f));
                }
        }
    }

    public class ScoreSystemTests
    {
        [Test]
        public void PenaltyTableMatchesTheSpecification()
        {
            Assert.AreEqual(-1, Penalties.Of(PenaltyKind.TrashOrProp));
            Assert.AreEqual(-2, Penalties.Of(PenaltyKind.Barrier));
            Assert.AreEqual(-3, Penalties.Of(PenaltyKind.TreeOrBuilding));
            Assert.AreEqual(-3, Penalties.Of(PenaltyKind.CivilianVehicle));
            Assert.AreEqual(-6, Penalties.Of(PenaltyKind.SevereVehicleCollision));
            Assert.AreEqual(-15, Penalties.Of(PenaltyKind.PedestrianHit));
            Assert.AreEqual(-3, Penalties.Of(PenaltyKind.RedLight));
            Assert.AreEqual(-1, Penalties.Of(PenaltyKind.WrongWay));
            Assert.AreEqual(-10, Penalties.Of(PenaltyKind.TaxiRecovery));
            Assert.AreEqual(-10, Penalties.Of(PenaltyKind.LatePickupAfterFleeing));
        }

        [Test]
        public void ScoreStartsAtOneHundredAndStaysInsideItsRange()
        {
            var score = new ScoreSystem();
            Assert.AreEqual(100, score.Score);
            score.Apply(-500);
            Assert.AreEqual(0, score.Score);
            score.Apply(500);
            Assert.AreEqual(100, score.Score);
            score.Reset();
            Assert.AreEqual(100, score.Score);
        }

        [TestCase(.75f, 0)] [TestCase(.74f, -3)] [TestCase(.60f, -3)] [TestCase(.59f, -7)]
        [TestCase(.40f, -7)] [TestCase(.39f, -12)] [TestCase(0f, -12)]
        public void SpeedComplianceUsesTheSpecificationBands(float fraction, int expected) =>
            Assert.AreEqual(expected, Compliance.SpeedPenalty(fraction));

        [TestCase(.75f, 0)] [TestCase(.74f, -3)] [TestCase(.50f, -3)] [TestCase(.49f, -6)]
        [TestCase(.25f, -6)] [TestCase(.24f, -10)]
        public void TemperatureComplianceUsesTheSpecificationBands(float fraction, int expected) =>
            Assert.AreEqual(expected, Compliance.TemperaturePenalty(fraction));

        [TestCase(12f, 0)] [TestCase(11f, -3)] [TestCase(6f, -3)] [TestCase(5.9f, -8)]
        public void TalkativeConversationBands(float secondsPerMinute, int expected) =>
            Assert.AreEqual(expected, Compliance.ConversationPenalty(ConversationPreference.Talkative, secondsPerMinute));

        [TestCase(5f, 0)] [TestCase(6f, -3)] [TestCase(12f, -3)] [TestCase(12.1f, -8)]
        public void SilentConversationBands(float secondsPerMinute, int expected) =>
            Assert.AreEqual(expected, Compliance.ConversationPenalty(ConversationPreference.Silent, secondsPerMinute));

        [TestCase(RequestOutcome.Correct, 0)] [TestCase(RequestOutcome.Ignored, -5)] [TestCase(RequestOutcome.Incorrect, -8)]
        public void RequestPenaltiesMatchTheSpecification(RequestOutcome outcome, int expected) =>
            Assert.AreEqual(expected, Compliance.RequestPenalty(outcome));

        [Test]
        public void ServiceQualitySplitsAtSixty()
        {
            Assert.AreEqual(ServiceQuality.Good, Compliance.Quality(60));
            Assert.AreEqual(ServiceQuality.Bad, Compliance.Quality(59));
        }

        [Test]
        public void MaximumFareStaysInsideTheSpecifiedRange()
        {
            Assert.AreEqual(18f, Fare.Maximum(0f, false));
            Assert.AreEqual(35f, Fare.Maximum(10000f, true));
            Assert.AreEqual(26f, Fare.Maximum(3000f, false), .001f);
            Assert.AreEqual(31f, Fare.Maximum(3000f, true), .001f);
        }

        [TestCase(ServiceQuality.Good, TimeOutcome.OnTime, 1f)]
        [TestCase(ServiceQuality.Good, TimeOutcome.Late, .80f)]
        [TestCase(ServiceQuality.Bad, TimeOutcome.OnTime, .60f)]
        [TestCase(ServiceQuality.Bad, TimeOutcome.Late, .35f)]
        public void PaymentMultipliersMatchTheSpecification(ServiceQuality quality, TimeOutcome time, float expected)
        {
            Assert.AreEqual(expected, Fare.Multiplier(quality, time), .001f);
            Assert.AreEqual(100f * expected, Fare.Payment(100f, quality, time), .001f);
        }

        [Test]
        public void UrgentPassengersHaveAlmostNoMargin()
        {
            Assert.AreEqual(75f, Deadlines.Allowance(false));
            Assert.AreEqual(20f, Deadlines.Allowance(true));
            Assert.AreEqual(315f, Deadlines.Deadline(240f, false));
            Assert.AreEqual(260f, Deadlines.Deadline(240f, true));
        }

        [Test]
        public void EndingsCoverTheFourCombinations()
        {
            Assert.AreEqual(EndingKind.GoodOnTime, Ending.Of(ServiceQuality.Good, TimeOutcome.OnTime));
            Assert.AreEqual(EndingKind.GoodLate, Ending.Of(ServiceQuality.Good, TimeOutcome.Late));
            Assert.AreEqual(EndingKind.BadOnTime, Ending.Of(ServiceQuality.Bad, TimeOutcome.OnTime));
            Assert.AreEqual(EndingKind.BadLate, Ending.Of(ServiceQuality.Bad, TimeOutcome.Late));
        }
    }

    public class PassengerCatalogTests
    {
        [Test]
        public void CatalogContainsTwelveNamedPassengers()
        {
            var all = PassengerCatalog.CreateAll(0);
            Assert.AreEqual(12, all.Length);
            CollectionAssert.AllItemsAreUnique(all.Select(p => p.FullName).ToArray());
        }

        [Test]
        public void CatalogIsDeterministicForASeed()
        {
            var a = PassengerCatalog.CreateAll(5);
            var b = PassengerCatalog.CreateAll(5);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].FullName, b[i].FullName);
                Assert.AreEqual(a[i].Speed, b[i].Speed);
                Assert.AreEqual(a[i].Food, b[i].Food);
                Assert.AreEqual(a[i].Urgency, b[i].Urgency);
            }
        }

        [Test]
        public void SpeedPreferencesUseTheSpecifiedBands()
        {
            var slow = new PassengerProfile { Speed = SpeedPreference.Slow };
            var moderate = new PassengerProfile { Speed = SpeedPreference.Moderate };
            var fast = new PassengerProfile { Speed = SpeedPreference.Fast };
            Assert.AreEqual(20f, slow.MinimumSpeedKmh); Assert.AreEqual(35f, slow.MaximumSpeedKmh);
            Assert.AreEqual(35f, moderate.MinimumSpeedKmh); Assert.AreEqual(50f, moderate.MaximumSpeedKmh);
            Assert.AreEqual(50f, fast.MinimumSpeedKmh); Assert.AreEqual(65f, fast.MaximumSpeedKmh);
        }

        [Test]
        public void FoodPreferencesMapToTheSixProps()
        {
            CollectionAssert.AreEquivalent(new[] { FoodItem.MeatSandwich, FoodItem.Jerky }, PassengerProfile.Accepted(FoodPreference.Carnivore));
            CollectionAssert.AreEquivalent(new[] { FoodItem.EggSandwich, FoodItem.CheeseSandwich }, PassengerProfile.Accepted(FoodPreference.Vegetarian));
            CollectionAssert.AreEquivalent(new[] { FoodItem.VegetableSalad, FoodItem.Apple }, PassengerProfile.Accepted(FoodPreference.Vegan));
            Assert.AreEqual(6, PassengerProfile.Menu.Length);
        }

        [Test]
        public void VeganPassengerRejectsMeat()
        {
            var vegan = new PassengerProfile { Food = FoodPreference.Vegan };
            Assert.IsTrue(vegan.Accepts(FoodItem.Apple));
            Assert.IsFalse(vegan.Accepts(FoodItem.Jerky));
        }

        [Test]
        public void SheetShowsEveryPreferenceTheSpecRequires()
        {
            var sheet = PassengerCatalog.Create(2, 0).SheetText();
            foreach (var label in new[] { "DESTINO", "VELOCIDAD", "COMIDA", "CONVERSACION", "TEMPERATURA", "URGENCIA" })
                StringAssert.Contains(label, sheet);
        }
    }

    public class TripSessionTests
    {
        static PassengerProfile Talkative() => new()
        {
            FullName = "Prueba",
            Speed = SpeedPreference.Moderate,
            Food = FoodPreference.Vegan,
            Conversation = ConversationPreference.Talkative,
            Temperature = TemperaturePreference.Cold,
            Urgency = PassengerUrgency.Normal,
            Voice = 1
        };

        static TripSession Session(float eta = 240f, float metres = 3000f) => new(Talkative(), eta, metres);

        static void Advance(TripSession trip, float seconds, float step = .5f, float speedKmh = 45f, int climate = -1, bool speaking = false)
        {
            for (float t = 0; t < seconds; t += step) trip.Tick(step, speedKmh, climate, speaking);
        }

        /// <summary>Igual que <see cref="Advance"/> pero atendiendo cada peticion en cuanto aparece, para
        /// aislar el efecto que se quiere medir del de las tres peticiones ignoradas.</summary>
        static void AdvanceAnswering(TripSession trip, float seconds, float step = .5f, float speedKmh = 45f, int climate = -1, bool speaking = true)
        {
            for (float t = 0; t < seconds; t += step)
            {
                trip.Tick(step, speedKmh, climate, speaking);
                if (trip.PendingRequest == null) continue;
                switch (trip.PendingRequest.Value)
                {
                    case RequestKind.Food: trip.Answer(RequestKind.Food, (int)FoodItem.Apple); break;
                    case RequestKind.Radio: trip.Answer(RequestKind.Radio, trip.PreferredStation); break;
                    default: trip.Answer(RequestKind.Temperature, -1); break;
                }
            }
        }

        [Test]
        public void IgnoringEveryRequestCostsFifteen()
        {
            var trip = Session();
            Advance(trip, 200f);
            trip.Complete();
            Assert.AreEqual(100 - 5 - 5 - 5 - 8, trip.Score.Score);
        }

        [Test]
        public void RequestsOpenAtTheScheduledTimes()
        {
            var trip = Session();
            Advance(trip, 54f);
            Assert.IsFalse(trip.PendingRequest.HasValue);
            Advance(trip, 2f);
            Assert.AreEqual(RequestKind.Food, trip.PendingRequest);
        }

        [Test]
        public void RequestWindowLastsTwentySecondsThenCountsAsIgnored()
        {
            var trip = Session();
            Advance(trip, 56f);
            Assert.AreEqual(RequestKind.Food, trip.PendingRequest);
            Advance(trip, 21f);
            Assert.IsFalse(trip.PendingRequest.HasValue);
            Assert.AreEqual(RequestOutcome.Ignored, trip.RequestResults[0]);
            Assert.AreEqual(95, trip.Score.Score);
        }

        [Test]
        public void CorrectFoodIsAcceptedAndWrongFoodIsNot()
        {
            var trip = Session();
            Advance(trip, 56f);
            Assert.AreEqual(RequestOutcome.Incorrect, trip.Answer(RequestKind.Food, (int)FoodItem.Jerky));
            Assert.AreEqual(92, trip.Score.Score);

            var second = Session();
            Advance(second, 56f);
            Assert.AreEqual(RequestOutcome.Correct, second.Answer(RequestKind.Food, (int)FoodItem.Apple));
            Assert.AreEqual(100, second.Score.Score);
        }

        [Test]
        public void RadioRequestWantsThePassengerStation()
        {
            var trip = Session();
            Advance(trip, 116f);
            Assert.AreEqual(RequestKind.Radio, trip.PendingRequest);
            int wanted = trip.PreferredStation;
            Assert.AreEqual(RequestOutcome.Correct, trip.Answer(RequestKind.Radio, wanted));
        }

        [Test]
        public void TemperatureRequestMatchesThePreference()
        {
            var trip = Session();
            Advance(trip, 176f);
            Assert.AreEqual(RequestKind.Temperature, trip.PendingRequest);
            Assert.AreEqual(RequestOutcome.Incorrect, trip.Answer(RequestKind.Temperature, 1));
        }

        [Test]
        public void AnsweringWithNoRequestOutstandingDoesNothing()
        {
            var trip = Session();
            Assert.AreEqual(RequestOutcome.Ignored, trip.Answer(RequestKind.Food, (int)FoodItem.Apple));
            Assert.AreEqual(100, trip.Score.Score);
        }

        [Test]
        public void DrivingInsideThePreferredBandCostsNothing()
        {
            var trip = Session();
            AdvanceAnswering(trip, 200f, speedKmh: 45f);
            trip.Complete();
            Assert.AreEqual(100, trip.Score.Score);
        }

        [Test]
        public void SpeedingTheWholeTripCostsTwelve()
        {
            var trip = Session();
            AdvanceAnswering(trip, 200f, speedKmh: 90f);
            trip.Complete();
            Assert.AreEqual(88, trip.Score.Score);
        }

        [Test]
        public void WrongClimateAndSilenceAreBothPenalised()
        {
            var trip = Session();
            AdvanceAnswering(trip, 200f, speedKmh: 45f, climate: 1, speaking: false);
            trip.Complete();
            // Clima contrario (-10) y conversador en silencio (-8).
            Assert.AreEqual(82, trip.Score.Score);
        }

        [Test]
        public void PerfectOnTimeTripEndsGoodOnTimeAndPaysInFull()
        {
            var trip = Session(eta: 240f, metres: 3000f);
            AdvanceAnswering(trip, 200f, speedKmh: 45f, climate: -1, speaking: true);
            var ending = trip.Complete();
            Assert.AreEqual(EndingKind.GoodOnTime, ending);
            Assert.AreEqual(ServiceQuality.Good, trip.Quality);
            Assert.AreEqual(TimeOutcome.OnTime, trip.Time);
            Assert.AreEqual(26f, trip.Payment, .001f);
        }

        [Test]
        public void OverrunningTheDeadlineEndsLateAndPaysLess()
        {
            var trip = Session(eta: 240f, metres: 3000f);
            Advance(trip, 330f, speedKmh: 45f, climate: -1, speaking: true);
            var ending = trip.Complete();
            Assert.AreEqual(EndingKind.GoodLate, ending);
            Assert.AreEqual(26f * .8f, trip.Payment, .001f);
        }

        [Test]
        public void UrgentPassengerDeadlineIsTighter()
        {
            var urgent = Talkative();
            urgent.Urgency = PassengerUrgency.Urgent;
            var trip = new TripSession(urgent, 240f, 3000f);
            Assert.AreEqual(260f, trip.Deadline, .001f);
            Advance(trip, 265f);
            Assert.IsTrue(trip.Overdue);
        }

        [Test]
        public void PennyPinchingDriverDropsToBadQuality()
        {
            var trip = Session();
            Advance(trip, 200f, speedKmh: 10f, climate: 1, speaking: false);
            Advance(trip, 21f);
            trip.Complete();
            // Velocidad fuera de banda (-12), clima contrario (-10), conversador en silencio (-8) y las tres
            // peticiones sin atender (-15).
            Assert.AreEqual(ServiceQuality.Bad, trip.Quality);
            Assert.AreEqual(100 - 12 - 10 - 8 - 15, trip.Score.Score);
        }

        [Test]
        public void CompletedTripIgnoresFurtherTicks()
        {
            var trip = Session();
            Advance(trip, 10f);
            trip.Complete();
            int score = trip.Score.Score;
            Advance(trip, 120f, speedKmh: 200f);
            Assert.AreEqual(score, trip.Score.Score);
        }
    }

    public class GameStateMachineTests
    {
        [Test]
        public void HappyPathWalksEveryPhaseInOrder()
        {
            var flow = new GameStateMachine();
            Assert.AreEqual(GamePhase.Boot, flow.Phase);
            flow.Tick(.1f);
            // El arranque no espera al FootTracker: es una fuente de entrada mas, no un requisito.
            Assert.AreEqual(GamePhase.WakeUp, flow.Phase);
            Assert.IsTrue(flow.WakeUpFinished());
            Assert.IsTrue(flow.BriefReady());
            Assert.AreEqual(GamePhase.WaitingPassenger, flow.Phase);
            Assert.IsTrue(flow.PassengerBoards(false));
            Assert.IsTrue(flow.BeginTrip());
            Assert.AreEqual(GamePhase.TripActive, flow.Phase);
            Assert.IsTrue(flow.Arrive());
            Assert.IsTrue(flow.Finish(EndingKind.GoodOnTime));
            Assert.AreEqual(GamePhase.NormalEnding, flow.Phase);
            Assert.IsTrue(flow.Terminal);
        }

        [Test]
        public void TripCannotStartBeforeThePassengerIsInside()
        {
            var flow = new GameStateMachine();
            flow.Tick(.1f);
            flow.WakeUpFinished();
            flow.BriefReady();
            Assert.IsFalse(flow.BeginTrip());
            Assert.IsFalse(flow.Arrive());
        }

        [Test]
        public void FleeingOnlyStartsOnceThePassengerIsExpected()
        {
            var flow = new GameStateMachine();
            Assert.IsFalse(flow.PassengerFlees());
            flow.Restart();
            Assert.IsTrue(flow.PassengerFlees());
            Assert.AreEqual(GamePhase.PassengerChasing, flow.Phase);
        }

        [Test]
        public void PassengerCanStillBeCollectedWhileBeingChasedWithAPenalty()
        {
            var flow = new GameStateMachine();
            flow.Restart();
            flow.PassengerFlees();
            Assert.IsTrue(flow.PassengerBoards(true));
            Assert.AreEqual(GamePhase.PassengerBoardsWithPenalty, flow.Phase);
            Assert.IsTrue(flow.BeginTrip());
        }

        [Test]
        public void PoliceOnlyAppearAfterFleeing()
        {
            var flow = new GameStateMachine();
            flow.Restart();
            Assert.IsFalse(flow.PoliceTriggered());
            flow.PassengerFlees();
            Assert.IsTrue(flow.PoliceTriggered());
            Assert.AreEqual(GamePhase.PoliceChase, flow.Phase);
        }

        [Test]
        public void PoliceChaseResolvesIntoCaughtOrEscaped()
        {
            var caught = new GameStateMachine();
            caught.Restart(); caught.PassengerFlees(); caught.PoliceTriggered();
            Assert.IsTrue(caught.Finish(EndingKind.PoliceCaught));
            Assert.AreEqual(GamePhase.PoliceCaught, caught.Phase);
            Assert.AreEqual(1, caught.TimesCaught);

            var escaped = new GameStateMachine();
            escaped.Restart(); escaped.PassengerFlees(); escaped.PoliceTriggered();
            Assert.IsTrue(escaped.Finish(EndingKind.PoliceEscaped));
            Assert.AreEqual(GamePhase.PoliceEscaped, escaped.Phase);
            Assert.AreEqual(1, escaped.PoliceEscapes);
        }

        [Test]
        public void ServedPassengersAreCountedAndRestartKeepsHistory()
        {
            var flow = new GameStateMachine();
            flow.Restart(); flow.PassengerBoards(false); flow.BeginTrip(); flow.Arrive(); flow.Finish(EndingKind.BadLate);
            flow.Restart();
            Assert.AreEqual(GamePhase.WaitingPassenger, flow.Phase);
            Assert.AreEqual(1, flow.PassengersServed);
            Assert.IsFalse(flow.Terminal);
        }

        [Test]
        public void PhaseTimerResetsOnEveryTransition()
        {
            var flow = new GameStateMachine();
            flow.Tick(.1f);
            Assert.AreEqual(0f, flow.PhaseSeconds, .0001f);
            flow.Tick(.4f);
            Assert.AreEqual(.4f, flow.PhaseSeconds, .0001f);
        }
    }

    public class StartupSequenceTests
    {
        [Test]
        public void BlackScreenLastsFiveSeconds()
        {
            Assert.AreEqual(0f, StartupSequence.Vision(0f));
            Assert.AreEqual(0f, StartupSequence.Vision(4.9f));
            Assert.AreEqual(0f, StartupSequence.Vision(5f));
        }

        [Test]
        public void EyesAreFullyOpenAtSixAndAHalfSeconds()
        {
            Assert.AreEqual(1f, StartupSequence.Vision(6.5f));
            Assert.That(StartupSequence.Vision(5.75f), Is.InRange(.01f, .99f));
        }

        [Test]
        public void PrinterRunsBetweenSevenAndNineSeconds()
        {
            Assert.IsFalse(StartupSequence.PrinterRunning(6.9f));
            Assert.IsTrue(StartupSequence.PrinterRunning(7f));
            Assert.IsTrue(StartupSequence.PrinterRunning(8.9f));
            Assert.IsFalse(StartupSequence.PrinterRunning(9f));
        }

        [Test]
        public void PrintProgressIsMonotonicAndClamped()
        {
            Assert.AreEqual(0f, StartupSequence.PrintProgress(7f));
            Assert.AreEqual(.5f, StartupSequence.PrintProgress(8f), .0001f);
            Assert.AreEqual(1f, StartupSequence.PrintProgress(9f));
            Assert.IsTrue(StartupSequence.Finished(9f));
        }
    }
}
