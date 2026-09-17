using NUnit.Framework;
using TaxiVR.Playable;

namespace TaxiVR.Tests.EditMode
{
    public class FootTrackingTests
    {
        const float Frame = 1f / 30f;

        static FootStateMachine Armed()
        {
            var machine = new FootStateMachine();
            machine.Tick(FootState.Down, FootState.Up, true, true, Frame);
            machine.Tick(FootState.Down, FootState.Up, true, true, Frame);
            machine.Tick(FootState.Down, FootState.Up, true, true, Frame);
            return machine;
        }

        static void Tick(FootStateMachine machine, FootState red, FootState green, int times = 3, float delta = Frame)
        {
            for (int i = 0; i < times; i++) machine.Tick(red, green, true, true, delta);
        }

        [Test]
        public void ParseAcceptsWellFormedPacket()
        {
            Assert.IsTrue(FootProtocol.TryParse("{\"version\":1,\"seq\":1821,\"red\":\"up\",\"green\":\"down\",\"redValid\":true,\"greenValid\":true}", out var packet));
            Assert.AreEqual(1821, packet.Sequence);
            Assert.AreEqual(FootState.Up, packet.Red);
            Assert.AreEqual(FootState.Down, packet.Green);
            Assert.IsTrue(packet.RedValid && packet.GreenValid);
            Assert.AreEqual(1, packet.Version);
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("[1,2,3]")]
        [TestCase("{\"seq\":1}")]
        [TestCase("{\"red\":\"down\",\"green\":5}")]
        public void ParseRejectsMalformedInput(string json) => Assert.IsFalse(FootProtocol.TryParse(json, out _));

        [Test]
        public void ParseRejectsUnknownState() =>
            Assert.IsFalse(FootProtocol.TryParse("{\"red\":\"sideways\",\"green\":\"down\"}", out _));

        [Test]
        public void ParseRejectsUnsupportedVersion() =>
            Assert.IsFalse(FootProtocol.TryParse("{\"version\":99,\"red\":\"up\",\"green\":\"down\"}", out _));

        [Test]
        public void ParseTreatsMissingVersionAsCurrent()
        {
            Assert.IsTrue(FootProtocol.TryParse("{\"red\":\"up\",\"green\":\"down\"}", out var packet));
            Assert.AreEqual(FootProtocol.Version, packet.Version);
        }

        [Test]
        public void SerializeRoundTripsThroughParse()
        {
            var original = new FootPacket(7, FootState.Up, FootState.Unknown, true, false, FootProtocol.Version);
            Assert.IsTrue(FootProtocol.TryParse(FootProtocol.Serialize(original), out var parsed));
            Assert.AreEqual(original.Sequence, parsed.Sequence);
            Assert.AreEqual(original.Red, parsed.Red);
            Assert.AreEqual(original.Green, parsed.Green);
            Assert.AreEqual(original.RedValid, parsed.RedValid);
            Assert.AreEqual(original.GreenValid, parsed.GreenValid);
        }

        [Test]
        public void SequenceOrderingSurvivesWrapAround()
        {
            Assert.IsTrue(FootProtocol.IsNewer(0, 1));
            Assert.IsFalse(FootProtocol.IsNewer(1, 0));
            Assert.IsFalse(FootProtocol.IsNewer(5, 5));
            Assert.IsTrue(FootProtocol.IsNewer(int.MaxValue, int.MinValue));
            Assert.IsTrue(FootProtocol.IsNewer(2147483645, -2147483645));
            Assert.IsFalse(FootProtocol.IsNewer(-2147483645, 2147483645));
        }

        [Test]
        public void BothFeetDownStartUnarmedAndNeutral()
        {
            var machine = new FootStateMachine();
            Tick(machine, FootState.Down, FootState.Down, 30);
            Assert.IsFalse(machine.Armed);
            Assert.IsFalse(machine.Throttle);
            Assert.IsFalse(machine.Brake);
            Assert.IsTrue(machine.TrackingValid);
        }

        [Test]
        public void FirstRaisedFootArmsAndBrakes()
        {
            var machine = Armed();
            Assert.IsTrue(machine.Armed);
            Assert.IsTrue(machine.Brake);
            Assert.IsFalse(machine.Throttle);
        }

        [Test]
        public void BothFeetUpAfterArmingIsNeutral()
        {
            var machine = Armed();
            Tick(machine, FootState.Up, FootState.Up);
            Assert.IsTrue(machine.Armed);
            Assert.IsFalse(machine.Throttle);
            Assert.IsFalse(machine.Brake);
        }

        [Test]
        public void BothFeetDownAfterArmingSkids()
        {
            var machine = Armed();
            Tick(machine, FootState.Down, FootState.Down);
            Assert.IsTrue(machine.Throttle);
            Assert.IsTrue(machine.Brake);
            Assert.IsTrue(FootPedals.Skid(machine.Throttle, machine.Brake));
        }

        [Test]
        public void GreenDownAloneAccelerates()
        {
            var machine = Armed();
            Tick(machine, FootState.Up, FootState.Down);
            Assert.IsTrue(machine.Throttle);
            Assert.IsFalse(machine.Brake);
        }

        [Test]
        public void UnknownNeverArms()
        {
            var machine = new FootStateMachine();
            Tick(machine, FootState.Unknown, FootState.Unknown, 60);
            Assert.IsFalse(machine.Armed);
            Assert.IsFalse(machine.Throttle);
            Assert.IsFalse(machine.Brake);
        }

        [Test]
        public void ChangeNeedsThreeStableSamples()
        {
            var machine = new FootStateMachine();
            machine.Tick(FootState.Down, FootState.Up, true, true, Frame);
            Assert.IsFalse(machine.Armed);
            machine.Tick(FootState.Down, FootState.Up, true, true, Frame);
            Assert.IsFalse(machine.Armed);
            machine.Tick(FootState.Down, FootState.Up, true, true, Frame);
            Assert.IsTrue(machine.Armed);
        }

        [Test]
        public void BounceBelowDebounceIsIgnored()
        {
            var machine = Armed();
            for (int i = 0; i < 4; i++)
            {
                machine.Tick(FootState.Up, FootState.Up, true, true, Frame);
                machine.Tick(FootState.Up, FootState.Up, true, true, Frame);
                machine.Tick(FootState.Down, FootState.Up, true, true, Frame);
                Assert.IsTrue(machine.Brake);
                Assert.IsFalse(machine.Throttle);
            }
        }

        [Test]
        public void InvalidMarkerIsNotReadAsPedal()
        {
            var machine = new FootStateMachine();
            for (int i = 0; i < 10; i++) machine.Tick(FootState.Down, FootState.Up, false, true, Frame);
            Assert.IsTrue(machine.Armed);
            Assert.IsFalse(machine.Brake);
        }

        [Test]
        public void ShortLossHoldsLastPedalWithinGrace()
        {
            var machine = Armed();
            Tick(machine, FootState.Up, FootState.Down);
            Assert.IsTrue(machine.Throttle);
            machine.Tick(FootState.Unknown, FootState.Unknown, true, true, .03f);
            Assert.IsTrue(machine.Throttle);
            Assert.IsTrue(machine.TrackingValid);
        }

        [Test]
        public void ProlongedLossGoesNeutralThenWarns()
        {
            var machine = Armed();
            Tick(machine, FootState.Up, FootState.Down);
            for (int i = 0; i < 4; i++) machine.Tick(FootState.Unknown, FootState.Unknown, true, true, Frame);
            Assert.IsFalse(machine.TrackingValid);
            Assert.IsFalse(machine.Throttle);
            Assert.IsFalse(machine.Brake);
            Assert.IsFalse(machine.Warning);
            Tick(machine, FootState.Unknown, FootState.Unknown, 30);
            Assert.IsTrue(machine.Warning);
        }

        [Test]
        public void LostTrackingRecoversWhenMarkersReturn()
        {
            var machine = Armed();
            Tick(machine, FootState.Unknown, FootState.Unknown, 30);
            Assert.IsFalse(machine.TrackingValid);
            Tick(machine, FootState.Down, FootState.Up);
            Assert.IsTrue(machine.TrackingValid);
            Assert.IsFalse(machine.Warning);
            Assert.IsTrue(machine.Brake);
        }

        [Test]
        public void UnarmedTableIsAlwaysNeutral()
        {
            FootPedals.Table(false, FootState.Down, FootState.Down, out bool throttle, out bool brake);
            Assert.IsFalse(throttle);
            Assert.IsFalse(brake);
            FootPedals.Table(true, FootState.Down, FootState.Down, out throttle, out brake);
            Assert.IsTrue(throttle);
            Assert.IsTrue(brake);
        }

        [Test]
        public void PedalRampIsMonotonicAndClamped()
        {
            Assert.AreEqual(.5f, FootPedals.Approach(0, 1, .2f, .1f), .0001f);
            Assert.AreEqual(1f, FootPedals.Approach(.9f, 1, .2f, .1f), .0001f);
            Assert.AreEqual(1f, FootPedals.Approach(0, 1, 0, .1f), .0001f);
            Assert.AreEqual(.5f, FootPedals.Approach(1, 0, .2f, .1f), .0001f);
        }

        [Test]
        public void ResetClearsArming()
        {
            var machine = Armed();
            machine.Reset();
            Assert.IsFalse(machine.Armed);
            Tick(machine, FootState.Down, FootState.Down);
            Assert.IsFalse(machine.Throttle);
            Assert.IsFalse(machine.Brake);
        }
    }
}
