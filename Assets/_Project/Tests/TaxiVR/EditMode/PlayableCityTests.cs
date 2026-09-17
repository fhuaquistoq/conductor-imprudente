using NUnit.Framework;
using UnityEngine;
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
    }
}
