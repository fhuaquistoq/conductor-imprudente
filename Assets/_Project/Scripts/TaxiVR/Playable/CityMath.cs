using System.Collections.Generic;
using UnityEngine;

namespace TaxiVR.Playable
{
    public static class CityMath
    {
        public const float Block = 64f;
        public static int Sector(float value) => Mathf.FloorToInt(value / Block);
        public static int Hash(int x, int z) { unchecked { uint h = (uint)(x * 374761393 + z * 668265263 + 1013); h = (h ^ (h >> 13)) * 1274126177; return (int)((h ^ (h >> 16)) & 0x7fffffff); } }
        public static float WheelDelta(float previous, float current) => Mathf.DeltaAngle(previous, current);
        public static bool SignalGreen(bool northSouth, float time)
        {
            float phase = Mathf.Repeat(time, 28);
            return northSouth ? phase < 11 : phase >= 14 && phase < 25;
        }
        public static bool SignalAmber(bool northSouth, float time)
        {
            float phase = Mathf.Repeat(time, 28);
            return northSouth ? phase >= 11 && phase < 13 : phase >= 25 && phase < 27;
        }
        // A* on an unbounded, uniform road graph; bounded search around this short local route.
        public static List<Vector2Int> Route(Vector2Int start, Vector2Int goal)
        {
            var open = new List<Vector2Int> { start };
            var costs = new Dictionary<Vector2Int, int> { [start] = 0 };
            var previous = new Dictionary<Vector2Int, Vector2Int>();
            Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            int Distance(Vector2Int p) => Mathf.Abs(p.x - goal.x) + Mathf.Abs(p.y - goal.y);
            for (int iteration = 0; open.Count > 0 && iteration < 4096; iteration++)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++) if (costs[open[i]] + Distance(open[i]) < costs[open[best]] + Distance(open[best])) best = i;
                var current = open[best]; open.RemoveAt(best);
                if (current == goal)
                {
                    var path = new List<Vector2Int> { goal };
                    while (previous.TryGetValue(current, out var parent)) { path.Add(parent); current = parent; }
                    path.Reverse(); return path;
                }
                foreach (var d in directions)
                {
                    var next = current + d; int cost = costs[current] + 1;
                    if (costs.TryGetValue(next, out int old) && cost >= old) continue;
                    costs[next] = cost; previous[next] = current;
                    if (!open.Contains(next)) open.Add(next);
                }
            }
            return new List<Vector2Int> { start };
        }
    }
}
