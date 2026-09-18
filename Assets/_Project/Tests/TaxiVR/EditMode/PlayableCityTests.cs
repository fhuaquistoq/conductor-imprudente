using NUnit.Framework;
using UnityEngine;
using TaxiVR.City;
using TaxiVR.Playable;

namespace TaxiVR.Tests.EditMode
{
    public class PlayableCityTests
    {
        [TestCase(-0.1f, -1)] [TestCase(0f, 0)] [TestCase(63.9f, 0)] [TestCase(64f, 1)]
        public void SectorsCoverNegativeAndPositiveCoordinates(float x, int expected)
            => Assert.AreEqual(expected, CityMath.Sector(x));
        [Test] public void SignalsNeverGiveBothAxesGreen()
        {
            for (float t = 0; t < 120; t += .1f)
                Assert.IsFalse(CityMath.SignalGreen(true, t) && CityMath.SignalGreen(false, t));
        }
        [Test] public void TrafficSignalsCycleThroughGreenOnBothAxesAndAnAllRedGap()
        {
            // El invariante negativo de arriba pasaria aunque SignalGreen devolviera siempre false: aqui se
            // comprueba que el ciclo de verdad reparte verde entre los dos ejes y deja un todo rojo.
            bool northGreen = false, eastGreen = false, allRed = false;
            for (float t = 0; t < 96f; t += .25f)
            {
                bool north = CityMath.SignalGreen(true, t), east = CityMath.SignalGreen(false, t);
                northGreen |= north;
                eastGreen |= east;
                allRed |= !north && !east;
            }
            Assert.IsTrue(northGreen, "El eje norte-sur debe ponerse verde en algun momento del ciclo");
            Assert.IsTrue(eastGreen, "El eje este-oeste debe ponerse verde en algun momento del ciclo");
            Assert.IsTrue(allRed, "Debe existir un intervalo de todo rojo entre ejes");
        }
        [Test] public void SteeringUnwrapsAcrossTheAngleSeam()
            => Assert.AreEqual(2, CityMath.WheelDelta(179, -179), .001);
        [Test] public void SectorHashIsStableAndVaried()
        {
            Assert.AreEqual(CityMath.Hash(-10, 700), CityMath.Hash(-10, 700));
            Assert.AreNotEqual(CityMath.Hash(-10, 700), CityMath.Hash(700, -10));
        }
        [Test] public void RouteRemainsOnConnectedGridAndReachesTarget()
        {
            var route = CityMath.Route(new Vector2Int(-2, 4), new Vector2Int(3, -1));
            Assert.AreEqual(new Vector2Int(3, -1), route[route.Count - 1]);
            for (int i = 1; i < route.Count; i++)
                Assert.AreEqual(1, Mathf.Abs(route[i].x - route[i-1].x) + Mathf.Abs(route[i].y - route[i-1].y));
        }
        [Test] public void TemplateIndexIsStableAndBounded()
        {
            for (int x = -20; x <= 20; x++) for (int z = -20; z <= 20; z++)
            {
                var key = new Vector2Int(x, z);
                Assert.AreEqual(CityMath.TemplateIndex(key, 6), CityMath.TemplateIndex(key, 6));
                Assert.That(CityMath.TemplateIndex(key, 6), Is.InRange(0, 5));
            }
        }
        [Test] public void TemplateRotationHonorsAllowedOrientations()
        {
            var go = new GameObject("Template test");
            var template = go.AddComponent<CitySectorTemplate>();
            template.AllowedRotations = new[] { false, true, false, false };
            Assert.AreEqual(90, CityMath.TemplateRotation(new Vector2Int(4, -8), template));
            Object.DestroyImmediate(go);
        }
    }
}
