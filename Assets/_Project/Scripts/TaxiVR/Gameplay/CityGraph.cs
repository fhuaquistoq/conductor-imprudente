using System;
using System.Collections.Generic;
using UnityEngine;

namespace TaxiVR.Gameplay
{
    public enum RoadKind { Street, Alley }

    public enum RoadDirection { TwoWay, Forward, Backward, Closed }

    /// <summary>Una calle entre dos cruces. Se guarda siempre con extremos canonicos (A antes que B) para
    /// que comparar dos aristas no dependa del sentido de recorrido.</summary>
    public readonly struct RoadEdge : IEquatable<RoadEdge>
    {
        public readonly Vector2Int A;
        public readonly Vector2Int B;
        public readonly RoadKind Kind;
        public readonly RoadDirection Direction;

        public RoadEdge(Vector2Int a, Vector2Int b, RoadKind kind, RoadDirection direction)
        {
            A = a; B = b; Kind = kind; Direction = direction;
        }

        public bool Drivable => Direction != RoadDirection.Closed;

        public bool Allows(Vector2Int from, Vector2Int to) =>
            Direction == RoadDirection.TwoWay ||
            (Direction == RoadDirection.Forward && from == A && to == B) ||
            (Direction == RoadDirection.Backward && from == B && to == A);

        public Vector2Int Other(Vector2Int node) => node == A ? B : A;

        public bool Equals(RoadEdge other) => A == other.A && B == other.B;
        public override bool Equals(object obj) => obj is RoadEdge other && Equals(other);
        public override int GetHashCode() => (A.GetHashCode() * 397) ^ B.GetHashCode();
        public override string ToString() => $"{A}->{B} {Kind} {Direction}";
    }

    /// <summary>Grafo vial logico. Es infinito en la practica: cualquier cruce existe y las calles se derivan
    /// por hash, asi que no hay que guardar nada y el mundo no tiene borde alcanzable.
    ///
    /// La densidad pedida se consigue con una espina dorsal determinista que garantiza conectividad total mas
    /// un relleno por hash. Asi el grafo nunca se parte y la seleccion de destino siempre puede exigir cuatro
    /// rutas viables.</summary>
    public sealed class CityGraph
    {
        /// <summary>Separacion entre ejes de calle, en metros. Debe coincidir con la rejilla del mundo.</summary>
        public const int BlockSize = 64;

        /// <summary>Velocidad de referencia para estimar tiempos: 45 km/h.</summary>
        public const float CruiseSpeed = 12.5f;

        /// <summary>Cada cuantos cruces la espina dorsal une dos filas.</summary>
        const int BackboneSpacing = 8;

        public int Seed { get; }
        public float Connectivity { get; }
        public float OneWayShare { get; }
        public float AlleyShare { get; }
        public float ClosureShare { get; }
        public float MaxTrafficMultiplier { get; set; } = 2.5f;

        /// <summary>Calles que el trazado de la ciudad se come, aunque la rejilla las ofrezca. Lo usa la ciudad
        /// de manzanas rectangulares: cuando dos sectores se funden en una manzana larga, la calle intermedia
        /// desaparece del mundo y el grafo tiene que dejar de ofrecerla o el GPS mandaria al taxista contra las
        /// casas. Sin asignar, el grafo se comporta como siempre.</summary>
        public Func<Vector2Int, Vector2Int, bool> Blocked;

        readonly float extraProbability;
        [ThreadStatic] static List<Vector2Int> neighbourBuffer;

        public CityGraph(int seed = 0, float connectivity = .72f, float oneWay = .15f, float alleys = .12f, float closures = .02f)
        {
            Seed = seed;
            Connectivity = Mathf.Clamp01(connectivity);
            OneWayShare = Mathf.Clamp01(oneWay);
            AlleyShare = Mathf.Clamp01(alleys);
            ClosureShare = Mathf.Clamp01(closures);

            // La espina dorsal aporta 1 + 1/BackboneSpacing de las 2 ranuras que cada cruce puede ofrecer
            // hacia delante (derecha y arriba). El resto se rellena al azar hasta la densidad pedida.
            float backbone = (1f + 1f / BackboneSpacing) / 2f;
            float free = 1f - backbone;
            extraProbability = free <= 0f ? 0f : Mathf.Clamp01((Connectivity - backbone) / free);
        }

        public static bool IsUnitStep(Vector2Int a, Vector2Int b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;

        static void Canonical(Vector2Int a, Vector2Int b, out Vector2Int lo, out Vector2Int hi)
        {
            if (a.x < b.x || (a.x == b.x && a.y < b.y)) { lo = a; hi = b; }
            else { lo = b; hi = a; }
        }

        static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value >> 16; value *= 0x7feb352d;
                value ^= value >> 15; value *= 0x846ca68b;
                value ^= value >> 16;
                return value;
            }
        }

        /// <summary>Numero determinista en [0,1). No depende de la plataforma.</summary>
        public static float Unit(Vector2Int node, int salt)
        {
            unchecked
            {
                uint h = (uint)(node.x * 73856093) ^ (uint)(node.y * 19349663) ^ (uint)(salt * 83492791);
                return Mix(h) / 4294967296f;
            }
        }

        /// <summary>Calles que forman la rejilla conectada garantizada.</summary>
        static bool IsBackbone(Vector2Int a, Vector2Int b)
        {
            if (a.y == b.y) return true;                    // toda fila es una cadena horizontal
            return a.x % BackboneSpacing == 0;              // y cada BackboneSpacing columnas unen filas
        }

        public bool Exists(Vector2Int a, Vector2Int b)
        {
            if (!IsUnitStep(a, b)) return false;
            if (Blocked != null && Blocked(a, b)) return false;
            if (IsBackbone(a, b)) return true;
            Canonical(a, b, out var lo, out _);
            return Unit(lo, 11 + Seed) < extraProbability;
        }

        /// <summary>Arista entre dos cruces contiguos. Solo tiene sentido si <see cref="Exists"/>.</summary>
        public RoadEdge Describe(Vector2Int a, Vector2Int b)
        {
            Canonical(a, b, out var lo, out var hi);
            RoadKind kind = RoadKind.Street;
            RoadDirection direction;
            if (Unit(lo, 23 + Seed) < ClosureShare) direction = RoadDirection.Closed;
            else if (Unit(lo, 31 + Seed) < OneWayShare) direction = Unit(lo, 37 + Seed) < .5f ? RoadDirection.Forward : RoadDirection.Backward;
            else
            {
                direction = RoadDirection.TwoWay;
                if (Unit(lo, 41 + Seed) < AlleyShare) kind = RoadKind.Alley;
            }
            return new RoadEdge(lo, hi, kind, direction);
        }

        /// <summary>Intenta recorrer un tramo respetando sentido unico y cierres.</summary>
        public bool TryStep(Vector2Int from, Vector2Int to, out RoadEdge edge)
        {
            edge = default;
            if (!Exists(from, to)) return false;
            edge = Describe(from, to);
            return edge.Drivable && edge.Allows(from, to);
        }

        /// <summary>Vecinos transitables de un cruce, respetando sentido unico y cierres. El buffer se reutiliza
        /// para no generar basura durante la busqueda.</summary>
        public List<Vector2Int> Neighbours(Vector2Int node, List<Vector2Int> buffer = null)
        {
            buffer ??= new List<Vector2Int>(4);
            buffer.Clear();
            if (TryStep(node, node + Vector2Int.right, out _)) buffer.Add(node + Vector2Int.right);
            if (TryStep(node, node + Vector2Int.left, out _)) buffer.Add(node + Vector2Int.left);
            if (TryStep(node, node + Vector2Int.up, out _)) buffer.Add(node + Vector2Int.up);
            if (TryStep(node, node + Vector2Int.down, out _)) buffer.Add(node + Vector2Int.down);
            return buffer;
        }

        /// <summary>Presion de trafico normalizada [0,1] del tramo. Alimenta coste y color del GPS.</summary>
        public float Traffic(Vector2Int a, Vector2Int b)
        {
            Canonical(a, b, out var lo, out _);
            return Unit(lo, 53 + Seed);
        }

        /// <summary>Coste en segundos del tramo, ya penalizado por el trafico.</summary>
        public float Cost(Vector2Int from, Vector2Int to)
        {
            if (!TryStep(from, to, out _)) return float.PositiveInfinity;
            float multiplier = 1f + Traffic(from, to) * (MaxTrafficMultiplier - 1f);
            return BlockSize / Mathf.Max(1f, CruiseSpeed / multiplier);
        }

        /// <summary>Longitud del tramo en metros.</summary>
        public float Length(Vector2Int from, Vector2Int to) =>
            new Vector2(to.x - from.x, to.y - from.y).magnitude * BlockSize;

        /// <summary>Coste minimo posible entre dos cruces, sin mirar el grafo. Cota inferior para A*.</summary>
        public const float FreeFlowSecondsPerBlock = BlockSize / CruiseSpeed;

        // ---------------------------------------------------------------- busqueda

        public List<Vector2Int> Shortest(Vector2Int start, Vector2Int goal) =>
            Shortest(start, goal, null, null);

        /// <summary>A* sobre la rejilla. Devuelve la ruta con inicio y destino incluidos, o una lista vacia.</summary>
        public List<Vector2Int> Shortest(Vector2Int start, Vector2Int goal, ISet<RoadEdge> bannedEdges, ISet<Vector2Int> bannedNodes, int budget = 20000)
        {
            var path = new List<Vector2Int>();
            if (start == goal) { path.Add(start); return path; }

            var cost = new Dictionary<Vector2Int, float> { [start] = 0f };
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var closed = new HashSet<Vector2Int>();
            var frontier = new NodeHeap();
            frontier.Push(start, Heuristic(start, goal));
            var buffer = neighbourBuffer ??= new List<Vector2Int>(4);

            int expansions = 0;
            while (frontier.Count > 0 && expansions++ < budget)
            {
                var current = frontier.Pop();
                if (!closed.Add(current)) continue;
                if (current == goal) return Reconstruct(cameFrom, start, goal);

                Neighbours(current, buffer);
                for (int i = 0; i < buffer.Count; i++)
                {
                    var next = buffer[i];
                    if (closed.Contains(next)) continue;
                    if (bannedNodes != null && bannedNodes.Contains(next)) continue;
                    if (!TryStep(current, next, out var edge)) continue;
                    if (bannedEdges != null && bannedEdges.Contains(edge)) continue;
                    float tentative = cost[current] + Cost(current, next);
                    if (cost.TryGetValue(next, out float known) && tentative >= known) continue;
                    cost[next] = tentative;
                    cameFrom[next] = current;
                    frontier.Push(next, tentative + Heuristic(next, goal));
                }
            }
            return path;
        }

        /// <summary>Cruces alcanzables desde un origen. Se usa en los tests de conectividad.</summary>
        public IEnumerable<Vector2Int> Reachable(Vector2Int start, List<Vector2Int> into = null, int limit = 4096)
        {
            into ??= new List<Vector2Int>();
            into.Clear();
            var seen = new HashSet<Vector2Int> { start };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            var buffer = new List<Vector2Int>(4);
            while (queue.Count > 0 && into.Count < limit)
            {
                var node = queue.Dequeue();
                into.Add(node);
                Neighbours(node, buffer);
                foreach (var next in buffer) if (seen.Add(next)) queue.Enqueue(next);
            }
            return into;
        }

        static float Heuristic(Vector2Int node, Vector2Int goal) =>
            (Mathf.Abs(node.x - goal.x) + Mathf.Abs(node.y - goal.y)) * FreeFlowSecondsPerBlock;

        static List<Vector2Int> Reconstruct(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int start, Vector2Int goal)
        {
            var path = new List<Vector2Int> { goal };
            var current = goal;
            while (current != start && cameFrom.TryGetValue(current, out var parent)) { current = parent; path.Add(current); }
            path.Reverse();
            if (path[0] != start) path.Insert(0, start);
            return path;
        }

        public float PathEta(IList<Vector2Int> path)
        {
            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += Cost(path[i - 1], path[i]);
            return total;
        }

        public float PathLength(IList<Vector2Int> path)
        {
            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += Length(path[i - 1], path[i]);
            return total;
        }

        static HashSet<RoadEdge> PathEdges(IList<Vector2Int> path)
        {
            var edges = new HashSet<RoadEdge>();
            for (int i = 1; i < path.Count; i++) edges.Add(Key(path[i - 1], path[i]));
            return edges;
        }

        static RoadEdge Key(Vector2Int a, Vector2Int b)
        {
            Canonical(a, b, out var lo, out var hi);
            return new RoadEdge(lo, hi, RoadKind.Street, RoadDirection.TwoWay);
        }

        /// <summary>Fraccion de aristas que la ruta comparte con la de referencia.</summary>
        public static float EdgeOverlap(IList<Vector2Int> path, IList<Vector2Int> reference)
        {
            var mine = PathEdges(path);
            if (mine.Count == 0) return 0f;
            var theirs = PathEdges(reference);
            int shared = 0;
            foreach (var edge in mine) if (theirs.Contains(edge)) shared++;
            return shared / (float)mine.Count;
        }

        static bool SameRoot(IList<Vector2Int> path, IList<Vector2Int> root)
        {
            for (int i = 0; i < root.Count; i++) if (path[i] != root[i]) return false;
            return true;
        }

        static string Signature(IList<Vector2Int> path)
        {
            var text = new System.Text.StringBuilder(path.Count * 8);
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0) text.Append('|');
                text.Append(path[i].x).Append(',').Append(path[i].y);
            }
            return text.ToString();
        }

        /// <summary>K rutas mas cortas por tiempo, sin repeticion.</summary>
        public List<List<Vector2Int>> KShortest(Vector2Int start, Vector2Int goal, int k) =>
            Search(start, goal, k, int.MaxValue, float.PositiveInfinity, float.PositiveInfinity);

        /// <summary>Rutas alternativas aceptables: tiempo dentro del margen y sin repetir en exceso el trazado
        /// de la mejor. Se detiene en cuanto junta <paramref name="stopAfter"/> rutas validas.</summary>
        public List<List<Vector2Int>> ViableRoutes(Vector2Int start, Vector2Int goal, int k = 5, float maxEtaFactor = 1.35f, float maxOverlap = .75f, int stopAfter = int.MaxValue) =>
            Search(start, goal, k, stopAfter, maxEtaFactor, maxOverlap);

        /// <summary>Yen sobre A*. Genera rutas por tiempo creciente y devuelve solo las que superan el filtro.</summary>
        List<List<Vector2Int>> Search(Vector2Int start, Vector2Int goal, int k, int stopAfter, float maxEtaFactor, float maxOverlap)
        {
            int wanted = Mathf.Max(1, k);
            var viable = new List<List<Vector2Int>>();
            var generated = new List<List<Vector2Int>>();
            var first = Shortest(start, goal);
            if (first.Count == 0) return viable;

            float optimal = PathEta(first);
            generated.Add(first);
            if (Accepts(first, null, optimal, maxEtaFactor, maxOverlap)) viable.Add(first);
            if (viable.Count >= stopAfter || generated.Count >= wanted) return viable;

            var candidates = new List<List<Vector2Int>>();
            var seen = new HashSet<string> { Signature(first) };

            for (int round = 1; round < wanted; round++)
            {
                var previous = generated[generated.Count - 1];
                for (int spur = 0; spur < previous.Count - 1; spur++)
                {
                    var root = previous.GetRange(0, spur + 1);
                    var bannedEdges = new HashSet<RoadEdge>();
                    foreach (var path in generated)
                    {
                        if (path.Count <= spur + 1 || !SameRoot(path, root)) continue;
                        bannedEdges.Add(Key(path[spur], path[spur + 1]));
                    }
                    var bannedNodes = new HashSet<Vector2Int>(previous.GetRange(0, spur));
                    var spurPath = Shortest(previous[spur], goal, bannedEdges, bannedNodes);
                    if (spurPath.Count == 0) continue;
                    var total = new List<Vector2Int>(root);
                    total.AddRange(spurPath.GetRange(1, spurPath.Count - 1));
                    if (seen.Add(Signature(total))) candidates.Add(total);
                }
                if (candidates.Count == 0) break;

                int best = 0;
                for (int i = 1; i < candidates.Count; i++) if (PathEta(candidates[i]) < PathEta(candidates[best])) best = i;
                var next = candidates[best];
                candidates.RemoveAt(best);
                generated.Add(next);

                if (Accepts(next, first, optimal, maxEtaFactor, maxOverlap)) viable.Add(next);
                if (viable.Count >= stopAfter) break;
            }
            return viable;
        }

        bool Accepts(IList<Vector2Int> path, IList<Vector2Int> reference, float optimal, float maxEtaFactor, float maxOverlap) =>
            PathEta(path) <= optimal * maxEtaFactor &&
            (reference == null || EdgeOverlap(path, reference) <= maxOverlap);

        /// <summary>Busca un destino cuya ruta cumpla el tiempo objetivo y ofrezca varias alternativas reales.
        ///
        /// La distancia se corrige iterativamente: se parte de una estimacion, se mide el tiempo real de la
        /// ruta que da A* y se reajusta el numero de cruces hacia el objetivo. Sortear destinos al azar casi
        /// nunca cae dentro de un margen de treinta segundos, y el trafico hace que A* elija rutas bastante mas
        /// rapidas que la media, asi que sin este ajuste la ventana se incumple casi siempre.</summary>
        public bool TryPickDestination(Vector2Int start, int salt, float minimumEta, float maximumEta, out Vector2Int destination, out List<List<Vector2Int>> routes, int requiredRoutes = 4, int attempts = 24)
        {
            destination = start;
            routes = new List<List<Vector2Int>>();

            float target = (minimumEta + maximumEta) * .5f;
            const float AverageTraffic = .5f;
            float secondsPerBlock = FreeFlowSecondsPerBlock * (1f + AverageTraffic * (MaxTrafficMultiplier - 1f));
            int total = Mathf.Clamp(Mathf.RoundToInt(target / secondsPerBlock), 2, 400);

            Vector2Int fallbackDestination = start;
            List<List<Vector2Int>> fallbackRoutes = null;
            float fallbackError = float.MaxValue;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int a = salt * 31 + attempt;
                int split = 1 + (int)(Unit(new Vector2Int(salt, a), 67) * Mathf.Max(1, total - 1));
                int dx = Mathf.Clamp(split, 1, Mathf.Max(1, total - 1));
                int dz = Mathf.Max(1, total - dx);
                var candidate = new Vector2Int(
                    start.x + (Unit(new Vector2Int(a, 0), 71) < .5f ? -dx : dx),
                    start.y + (Unit(new Vector2Int(0, a), 73) < .5f ? -dz : dz));

                var best = Shortest(start, candidate);
                if (best.Count <= 1) { total = Mathf.Clamp(total + 2, 2, 400); continue; }
                float eta = PathEta(best);

                if (eta >= minimumEta && eta <= maximumEta)
                {
                    var viable = ViableRoutes(start, candidate, 5, 1.35f, .75f, requiredRoutes);
                    if (viable.Count >= requiredRoutes)
                    {
                        destination = candidate;
                        routes = viable;
                        return true;
                    }
                    // El tiempo encaja pero no hay cuatro alternativas: se recuerda por si nada mejor aparece.
                    if (viable.Count > (fallbackRoutes?.Count ?? 0) && Mathf.Abs(eta - target) < fallbackError)
                    {
                        fallbackDestination = candidate;
                        fallbackRoutes = viable;
                        fallbackError = Mathf.Abs(eta - target);
                    }
                }

                int corrected = Mathf.Clamp(Mathf.RoundToInt(total * target / Mathf.Max(1f, eta)), 2, 400);
                if (corrected == total) corrected = total + (eta < minimumEta ? 1 : -1);
                total = Mathf.Clamp(corrected, 2, 400);
            }

            if (fallbackRoutes == null || fallbackRoutes.Count == 0) return false;
            destination = fallbackDestination;
            routes = fallbackRoutes;
            return true;
        }

        /// <summary>Heap binario minimo. Unity no expone PriorityQueue en su perfil de API.</summary>
        sealed class NodeHeap
        {
            readonly List<Vector2Int> nodes = new();
            readonly List<float> priorities = new();

            public int Count => nodes.Count;

            public void Push(Vector2Int node, float priority)
            {
                nodes.Add(node); priorities.Add(priority);
                int i = nodes.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (priorities[parent] <= priorities[i]) break;
                    Swap(parent, i); i = parent;
                }
            }

            public Vector2Int Pop()
            {
                var top = nodes[0];
                int last = nodes.Count - 1;
                nodes[0] = nodes[last]; priorities[0] = priorities[last];
                nodes.RemoveAt(last); priorities.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1, right = left + 1, smallest = i;
                    if (left < nodes.Count && priorities[left] < priorities[smallest]) smallest = left;
                    if (right < nodes.Count && priorities[right] < priorities[smallest]) smallest = right;
                    if (smallest == i) break;
                    Swap(smallest, i); i = smallest;
                }
                return top;
            }

            void Swap(int a, int b)
            {
                (nodes[a], nodes[b]) = (nodes[b], nodes[a]);
                (priorities[a], priorities[b]) = (priorities[b], priorities[a]);
            }
        }
    }
}
