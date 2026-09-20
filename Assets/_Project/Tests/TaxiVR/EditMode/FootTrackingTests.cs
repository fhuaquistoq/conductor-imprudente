using System;
using System.IO;
using NUnit.Framework;
using TaxiVR.Playable;
using UnityEngine;

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

        const string WellFormed =
            "{\"version\":2,\"sequence\":1821,\"timestamp\":1789800123.22,\"calibrated\":true," +
            "\"brake\":{\"pressed\":true,\"confidence\":0.94,\"value\":0.08}," +
            "\"accelerator\":{\"pressed\":false,\"confidence\":0.9,\"value\":0.87}}";

        [Test]
        public void ParseAcceptsWellFormedPacket()
        {
            Assert.IsTrue(FootProtocol.TryParse(WellFormed, out var packet));
            Assert.AreEqual(1821, packet.Sequence);
            Assert.AreEqual(1789800123.22, packet.Timestamp, .001);
            Assert.IsTrue(packet.Calibrated);
            Assert.AreEqual(FootState.Down, packet.Brake);
            Assert.AreEqual(FootState.Up, packet.Accelerator);
            Assert.AreEqual(0.94f, packet.BrakeConfidence, .001f);
            Assert.AreEqual(0.08f, packet.BrakeValue, .001f);
            Assert.AreEqual(2, packet.Version);
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("[1,2,3]")]
        [TestCase("{\"version\":2}")]
        [TestCase("{\"version\":2,\"sequence\":1,\"brake\":\"down\",\"accelerator\":{\"pressed\":false}}")]
        public void ParseRejectsMalformedInput(string json) => Assert.IsFalse(FootProtocol.TryParse(json, out _));

        [Test]
        public void ParseRejectsTheOldContract() =>
            Assert.IsFalse(FootProtocol.TryParse("{\"version\":1,\"seq\":1,\"red\":\"up\",\"green\":\"down\",\"redValid\":true,\"greenValid\":true}", out _));

        [Test]
        public void ParseRejectsUnsupportedVersion() =>
            Assert.IsFalse(FootProtocol.TryParse("{\"version\":99,\"sequence\":1,\"brake\":{\"pressed\":true},\"accelerator\":{\"pressed\":false}}", out _));

        [Test]
        public void ParseTreatsMissingVersionAsCurrent()
        {
            Assert.IsTrue(FootProtocol.TryParse("{\"sequence\":4,\"brake\":{\"pressed\":true},\"accelerator\":{\"pressed\":false}}", out var packet));
            Assert.AreEqual(FootProtocol.Version, packet.Version);
        }

        [Test]
        public void AFootThatIsNotSeenIsUnknownAndNeverPressed()
        {
            Assert.IsTrue(FootProtocol.TryParse(
                "{\"version\":2,\"sequence\":4,\"brake\":{\"pressed\":true,\"confidence\":0.0,\"value\":1.0},\"accelerator\":{\"pressed\":false,\"confidence\":0.0,\"value\":1.0}}",
                out var packet));
            Assert.AreEqual(FootState.Unknown, packet.Brake);
            Assert.IsFalse(packet.BrakeValid);
        }

        [Test]
        public void SerializeRoundTripsThroughParse()
        {
            var original = FootProtocol.FromStates(7, FootState.Up, FootState.Unknown, 1789800123.5, true);
            Assert.IsTrue(FootProtocol.TryParse(FootProtocol.Serialize(original), out var parsed));
            Assert.AreEqual(original.Sequence, parsed.Sequence);
            Assert.AreEqual(original.Timestamp, parsed.Timestamp, .001);
            Assert.AreEqual(original.Calibrated, parsed.Calibrated);
            Assert.AreEqual(original.Brake, parsed.Brake);
            Assert.AreEqual(original.Accelerator, parsed.Accelerator);
            Assert.AreEqual(original.BrakeConfidence, parsed.BrakeConfidence, .001f);
            Assert.AreEqual(original.BrakeValue, parsed.BrakeValue, .001f);
        }

        [Test]
        public void ConnectionThresholdsAreOrdered()
        {
            Assert.That(FootProtocol.UnstableSeconds, Is.LessThan(FootProtocol.LostSeconds));
            Assert.That(FootProtocol.LostSeconds, Is.LessThan(FootProtocol.StaleSeconds),
                "el estado del enlace es diagnostico; el mando vuelve al teclado mas tarde, como antes");
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
        public void NoDataEverIsSilentAndNeutral()
        {
            var machine = new FootStateMachine();
            Tick(machine, FootState.Unknown, FootState.Unknown, 120);
            Assert.IsFalse(machine.Armed);
            Assert.IsFalse(machine.Warning, "Sin haber seguido nunca no hay nada que avisar: el juego no dice nada.");
            Assert.IsFalse(machine.Throttle);
            Assert.IsFalse(machine.Brake);
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
        public void FeetTakeOverWhileTrackingAndReturnControlWhenTheyStop()
        {
            FootPedals.Source(true, true, false, 0f, 1f, out float throttle, out float brake);
            Assert.IsTrue(throttle > .99f, "El pie verde acelera.");
            Assert.IsFalse(brake > .01f, "Con el tracker en linea el freno del teclado no manda.");

            FootPedals.Source(false, false, false, 1f, 0f, out throttle, out brake);
            Assert.IsTrue(throttle > .99f, "Sin tracker vuelve el teclado.");
            Assert.IsFalse(brake > .01f);
        }

        [Test]
        public void FeetNeutralWhileTrackingIgnoresTheKeyboard()
        {
            FootPedals.Source(true, false, false, 1f, 0f, out float throttle, out float brake);
            Assert.IsFalse(throttle > .01f, "Con los pies en linea, W no acelera por su cuenta.");
            Assert.IsFalse(brake > .01f);
        }

        [Test]
        public void FootBrakeWinsOverKeyboardThrottle()
        {
            FootPedals.Source(true, false, true, 1f, 0f, out float throttle, out float brake);
            Assert.IsFalse(throttle > .01f);
            Assert.IsTrue(brake > .99f);
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

        [Serializable] sealed class FixturePedal { public bool pressed; public float confidence; public float value; }
        [Serializable] sealed class FixtureSample { public string json; public int sequence; public double timestamp; public bool calibrated; public FixturePedal brake; public FixturePedal accelerator; }
        [Serializable] sealed class FixtureOrder { public int previous; public int next; public bool expected; }
        [Serializable] sealed class ProtocolFixture { public int version; public FixtureSample[] accepted; public string[] rejected; public FixtureOrder[] newer; }

        static string FixturePath => Path.Combine(Application.dataPath, "..", "FootTracker", "tests", "fixtures", "protocol_samples.json");

        /// <summary>El protocolo de pies esta duplicado en C# y en Python: ambos lados leen este mismo fixture,
        /// de modo que una divergencia rompe el test de uno de los dos.</summary>
        [Test]
        public void SharedCrossLanguageFixtureMatchesProtocol()
        {
            Assert.IsTrue(File.Exists(FixturePath), "Falta el fixture compartido: " + FixturePath);
            var fixture = JsonUtility.FromJson<ProtocolFixture>(File.ReadAllText(FixturePath));
            Assert.AreEqual(FootProtocol.Version, fixture.version, "La version del fixture y la del protocolo deben coincidir");
            foreach (var sample in fixture.accepted)
            {
                Assert.IsTrue(FootProtocol.TryParse(sample.json, out var packet), "Deberia aceptar: " + sample.json);
                Assert.AreEqual(sample.sequence, packet.Sequence);
                Assert.AreEqual(sample.timestamp, packet.Timestamp, .001, "timestamp de " + sample.json);
                Assert.AreEqual(sample.calibrated, packet.Calibrated);
                Assert.AreEqual(sample.brake.pressed, packet.BrakePressed, "freno de " + sample.json);
                Assert.AreEqual(sample.brake.confidence, packet.BrakeConfidence, .001f);
                Assert.AreEqual(sample.brake.value, packet.BrakeValue, .001f);
                Assert.AreEqual(sample.accelerator.pressed, packet.AcceleratorPressed, "acelerador de " + sample.json);
                Assert.AreEqual(sample.accelerator.confidence, packet.AcceleratorConfidence, .001f);
                Assert.AreEqual(sample.accelerator.value, packet.AcceleratorValue, .001f);
            }
            foreach (var raw in fixture.rejected)
                Assert.IsFalse(FootProtocol.TryParse(raw, out _), "Deberia rechazar: " + raw);
            foreach (var item in fixture.newer)
                Assert.AreEqual(item.expected, FootProtocol.IsNewer(item.previous, item.next), $"IsNewer({item.previous}, {item.next})");
        }
    }
}
