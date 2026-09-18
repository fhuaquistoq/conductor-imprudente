using System.Collections.Generic;
using UnityEngine;
using TaxiVR.City;

namespace TaxiVR.Playable
{
    public static class CityMath
    {
        public const float Block = 64f;
        public static int Sector(float value) => Mathf.FloorToInt(value / Block);
        public static int Hash(int x, int z) { unchecked { uint h = (uint)(x * 374761393 + z * 668265263 + 1013); h = (h ^ (h >> 13)) * 1274126177; return (int)((h ^ (h >> 16)) & 0x7fffffff); } }
        public static int TemplateIndex(Vector2Int key, int count)
        {
            if (count <= 0) return 0;
            return Hash(key.x, key.y) % count;
        }
        public static float TemplateRotation(Vector2Int key, CitySectorTemplate template)
        {
            if (template == null || template.AllowedRotations == null || template.AllowedRotations.Length == 0) return 0;
            int available = 0;
            for (int i = 0; i < template.AllowedRotations.Length && i < 4; i++) if (template.AllowedRotations[i]) available++;
            if (available == 0) return 0;
            int selected = Hash(key.x + 7919, key.y - 104729) % available;
            for (int i = 0; i < template.AllowedRotations.Length && i < 4; i++)
                if (template.AllowedRotations[i] && selected-- == 0) return i * 90;
            return 0;
        }
        public static float WheelDelta(float previous, float current) => Mathf.DeltaAngle(previous, current);
        // El ciclo vive en TrafficLights (20 s verde, 3 s ambar, 1 s todo rojo por eje) para que los coches
        // civiles, las lamparas de la ciudad y la penalizacion por rojo del director no puedan discrepar.
        public static bool SignalGreen(bool northSouth, float time) => TrafficLights.IsGreen(northSouth, time);
        public static bool SignalAmber(bool northSouth, float time) => TrafficLights.IsAmber(northSouth, time);
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
