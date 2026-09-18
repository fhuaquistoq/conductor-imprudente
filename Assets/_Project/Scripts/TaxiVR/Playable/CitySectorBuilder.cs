using System.Collections.Generic;
using UnityEngine;
using TaxiVR.Gameplay;

namespace TaxiVR.Playable
{
    /// <summary>Construye el contenido de un sector de 64 m a partir de la rejilla logica y del catalogo de
    /// arte. Nada se guarda entre sesiones: la misma clave de sector produce siempre la misma calle, la misma
    /// manzana y la misma decoracion, de modo que el mundo es infinito sin almacenar el mundo.
    ///
    /// El sector se reparte en tres grupos: suelo (calzada y acera, siempre presentes), silueta lejana
    /// (volumen barato para que la ciudad no desaparezca en la niebla) y detalle (casas y decoracion, solo
    /// cerca del taxi). Esa separacion es lo que sostiene el presupuesto de la maquina en el visor.</summary>
    public static class CitySectorBuilder
    {
        /// <summary>Cada cuantos metros se repite la textura. Se elige el tamano real de las piezas del kit
        /// para que el asfalto y la acera no se vean estirados.</summary>
        const float AsphaltTile = 6f;
        const float PavementTile = 3f;
        const float GrassTile = 4f;

        /// <summary>Retranqueo de la casa dentro de su parcela, para que dos vecinas no se toquen.</summary>
        const float HouseFill = .88f;

        /// <summary>Senalizacion: eje, ancho del paso de cebra, distancia al cruce y altura a la que se pinta
        /// para no pelear con el asfalto.</summary>
        const float CentreLine = .3f;
        const float CrossingWidth = 3f;
        const float CrossingInset = 6.4f;
        const float MarkHeight = .012f;
        const float MarkTile = 1f;

        // ------------------------------------------------------------------ suelo

        /// <summary>Calzada, acera y cesped del sector. Es lo unico que se dibuja siempre, y donde esta el
        /// colisionador que impide que el taxi caiga al vacio aunque el resto de la ciudad tarde en cargar.
        /// Devuelve el grupo creado para que el streaming pueda soltarlo entero al reciclar el sector.</summary>
        public static GameObject BuildGround(CityAssets assets, Vector2Int key, Transform parent)
        {
            var origin = new Vector3(key.x * CityMath.Block, 0, key.y * CityMath.Block);
            bool west = !CityGrid.ClosedVertical(key.x, key.y);
            bool south = !CityGrid.ClosedHorizontal(key.x, key.y);
            bool east = !CityGrid.ClosedVertical(key.x + 1, key.y);
            bool north = !CityGrid.ClosedHorizontal(key.x, key.y + 1);

            var road = new CityMesh { Origin = origin };
            var pavement = new CityMesh { Origin = origin };
            var grass = new CityMesh { Origin = origin };
            var lines = new CityMesh { Origin = origin };
            var crossings = new CityMesh { Origin = origin };

            // Reparto exacto del sector: cada region se pinta una sola vez, asi que no hay ni huecos por los
            // que asomarse al vacio ni superficies coplanares peleando por el mismo pixel.
            Surface(road, pavement, new Rect(0, 0, 6, 6), west || south);
            Surface(road, pavement, new Rect(0, 6, 6, 58), west);
            Surface(road, pavement, new Rect(6, 0, 58, 6), south);
            Surface(road, pavement, new Rect(58, 6, 6, 52), east);
            Surface(road, pavement, new Rect(6, 58, 52, 6), north);
            Surface(road, pavement, new Rect(58, 58, 6, 6), east || north);
            pavement.AddSlab(new Rect(6, 6, 52, 52), CityGrid.CurbHeight, .2f, PavementTile);

            // Senalizacion: eje amarillo y pasos de cebra, dibujados por mitades igual que el asfalto. Cada
            // sector pone la mitad que le toca y entre los cuatro vecinos sale el cruce completo, sin repetir ni
            // una sola banda.
            if (west) Markings(lines, crossings, true, true, 0f, 6f);
            if (east) Markings(lines, crossings, true, false, 58f, 64f);
            if (south) Markings(lines, crossings, false, true, 0f, 6f);
            if (north) Markings(lines, crossings, false, false, 58f, 64f);

            var block = CityGrid.BlockAt(key);
            var centre = CentreRect(block);
            var window = SectorRect(key);
            if (centre.Overlaps(window))
            {
                var overlap = Intersect(centre, window);
                grass.AddSlab(Local(overlap, origin), CityGrid.CurbHeight + .02f, .24f, GrassTile);
            }

            var root = new GameObject("Suelo");
            root.transform.SetParent(parent, false);
            road.Build(root.transform, "Calzada", assets.Asphalt);
            pavement.Build(root.transform, "Acera", assets.Pavement);
            if (!grass.IsEmpty) grass.Build(root.transform, "Cesped", assets.Grass);
            if (!lines.IsEmpty) lines.Build(root.transform, "Eje de calzada", assets.Yellow);
            if (!crossings.IsEmpty) crossings.Build(root.transform, "Pasos de cebra", assets.White);

            // Dos colisionadores: el del sector garantiza suelo en todo el cuadrado y la losa de acera da el
            // bordillo. El suelo es lo que hace imposible caerse incluso en el borde del mundo cargado.
            var floor = root.AddComponent<BoxCollider>();
            floor.center = new Vector3(CityMath.Block * .5f, -1f, CityMath.Block * .5f);
            floor.size = new Vector3(CityMath.Block, 2f, CityMath.Block);
            AddPavementColliders(root.transform, pavement);
            return root;
        }

        /// <summary>Volumen de relleno de la manzana para cuando el detalle esta apagado. Una caja con la
        /// textura de fachadas lejanas cuesta un dibujo y mantiene la silueta de la ciudad en la niebla.</summary>
        public static GameObject BuildFar(CityAssets assets, Vector2Int key, Transform parent)
        {
            bool west = !CityGrid.ClosedVertical(key.x, key.y);
            bool south = !CityGrid.ClosedHorizontal(key.x, key.y);
            bool east = !CityGrid.ClosedVertical(key.x + 1, key.y);
            bool north = !CityGrid.ClosedHorizontal(key.x, key.y + 1);

            float x0 = west ? 6f : 0f, x1 = east ? 58f : 64f;
            float z0 = south ? 6f : 0f, z1 = north ? 58f : 64f;
            float width = x1 - x0, depth = z1 - z0;
            var root = new GameObject("Silueta lejana");
            root.transform.SetParent(parent, false);
            if (width <= 1f || depth <= 1f) return root;

            // La altura sale del hash del sector: dos sectores de la misma manzana nunca miden lo mismo, y asi
            // la silueta lejana tiene perfil de ciudad en lugar de un muro continuo.
            int seed = CityMath.Hash(key.x, key.y);
            float height = 9f + seed % 5 * 3.2f;
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Manzana lejana";
            box.transform.SetParent(root.transform, false);
            box.transform.localPosition = new Vector3(x0 + width * .5f, height * .5f, z0 + depth * .5f);
            box.transform.localScale = new Vector3(width, height, depth);
            var renderer = box.GetComponent<Renderer>();
            renderer.sharedMaterial = assets.DistantBuilding;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        // ------------------------------------------------------------------ detalle

        /// <summary>Casas, arboles y decoracion de la manzana. Solo se monta cerca del taxi: es la parte que
        /// cuesta dinero y la unica que el jugador puede llegar a mirar de cerca.</summary>
        public static GameObject BuildDetail(CityAssets assets, Vector2Int key, Transform parent)
        {
            var origin = new Vector3(key.x * CityMath.Block, 0, key.y * CityMath.Block);
            var block = CityGrid.BlockAt(key);
            var window = SectorRect(key);
            var cell = block.CellSize;
            var root = new GameObject("Detalle");
            root.transform.SetParent(parent, false);
            var houses = Child(root.transform, "Casas");
            var props = Child(root.transform, "Decoracion");

            for (int z = 0; z < block.CellsZ; z++)
                for (int x = 0; x < block.CellsX; x++)
                {
                    if (!block.IsPerimeter(x, z)) continue;
                    var centre = block.CellCentre(x, z);
                    if (!window.Contains(new Vector2(centre.x, centre.z))) continue;
                    var facing = Facing(block, x, z);
                    House(assets, houses, block, x, z, centre - origin, cell, facing);
                    StreetFurniture(assets, props, block, x, z, centre, origin, cell, facing);
                }

            // El patio interior no se edifica, pero se planta: da fondo verde sin costo de edificio.
            var centreRound = CentreRect(block);
            if (centreRound.Overlaps(window)) Yard(assets, props, block, Local(Intersect(centreRound, window), origin));

            Signal(assets, props, key);
            return root;
        }

        static void House(CityAssets assets, Transform parent, CityBlock block, int x, int z, Vector3 centre, Vector2 cell, Vector3 facing)
        {
            if (assets.Buildings == null || assets.Buildings.Length == 0)
            {
                ArtLog.WarnOnce("casas", "El catalogo no trae edificios: las manzanas se quedan sin casas. Ejecuta TaxiVR > City > Wire generated model slots.");
                return;
            }
            int seed = CityMath.Hash(block.Origin.x * 31 + x, block.Origin.y * 31 + z);
            var prefab = assets.Buildings[seed % assets.Buildings.Length];
            if (prefab == null) return;
            var position = new Vector3(centre.x, CityGrid.CurbHeight, centre.z);
            var house = Shape.Fit(prefab, parent, position, cell, Yaw(facing), HouseFill);
            CityProps.Mark(house, PenaltyKind.TreeOrBuilding);
        }

        /// <summary>Farolas, pivotes, bancos, bocas de riego, senales y contenedores, repartidos por el borde
        /// de la parcela que da a la calle. Todo determinista, para que la misma esquina se vea igual siempre.
        /// Las piezas de catalogo se colocan en coordenadas locales; el utillaje construido por codigo, en
        /// cambio, fija su posicion de mundo, que es lo que necesita para sobrevivir al origen flotante.</summary>
        static void StreetFurniture(CityAssets assets, Transform parent, CityBlock block, int x, int z, Vector3 centre, Vector3 origin, Vector2 cell, Vector3 facing)
        {
            if (assets.HardProps == null || assets.HardProps.Length == 0) ArtLog.WarnOnce("props-duros", "El catalogo no trae decoracion dura: faltan farolas, pivotes y jardineras del kit. Ejecuta TaxiVR > City > Wire generated model slots.");
            if (assets.SoftProps == null || assets.SoftProps.Length == 0) ArtLog.WarnOnce("props-blandos", "El catalogo no trae decoracion blanda: el taxi no tendra nada que tirar. Ejecuta TaxiVR > City > Wire generated model slots.");
            if (assets.MarketProps == null || assets.MarketProps.Length == 0) ArtLog.WarnOnce("puesto", "El catalogo no trae genero del puesto: los puestos callejeros salen vacios. Ejecuta TaxiVR > City > Wire generated model slots.");
            float offset = (Mathf.Abs(facing.x) > .5f ? cell.x : cell.y) * .5f + 1.6f;
            var curb = new Vector3(centre.x + facing.x * offset, CityGrid.CurbHeight, centre.z + facing.z * offset);
            int seed = CityMath.Hash(block.Origin.x * 17 + x * 3, block.Origin.y * 17 + z * 5);
            float yaw = Yaw(facing);
            int kind = seed % 12;

            if (kind == 0) CityProps.Lamp(parent, curb + facing * 1.1f, yaw, assets);
            else if (kind == 1) CityProps.Bench(parent, curb + facing * 1.5f, yaw + 90f, assets);
            else if (kind == 2) CityProps.Hydrant(parent, curb + facing * 1.2f, assets);
            else if (kind == 3) CityProps.Sign(parent, curb + facing * 1.3f, yaw + 90f, assets, assets.White);
            else if (kind == 4) CityProps.Barrier(parent, curb + facing * 1.6f, yaw + 90f, assets);
            else if (kind == 5 && assets.HardProps != null && assets.HardProps.Length > 0)
                CityProps.Fixed(assets.HardProps[seed % assets.HardProps.Length], parent, curb + facing * 1.4f - origin, seed % 4 * 90f);

            // Decoracion blanda: el coche la tira. Va pegada a la fachada para que se vea al pasar y no estorbe.
            var wall = new Vector3(centre.x + facing.x * (offset - 2.6f), CityGrid.CurbHeight, centre.z + facing.z * (offset - 2.6f));
            var side = new Vector3(facing.z, 0, -facing.x);
            switch (seed % 7)
            {
                case 0: CityProps.Bin(parent, wall + facing * .2f, yaw, assets); break;
                case 1:
                    CityProps.Crate(parent, wall, yaw + 20f, assets);
                    CityProps.Crate(parent, wall + side * .6f, yaw, assets);
                    break;
                case 2: CityProps.Barrel(parent, wall, assets); break;
                case 3:
                    for (int i = 0; i < 3; i++) CityProps.Cone(parent, wall + facing * (.4f + i * 1.1f), assets);
                    break;
                case 4 when assets.MarketProps != null && assets.MarketProps.Length > 0:
                    for (int i = 0; i < 4; i++)
                        CityProps.Produce(parent, wall + side * (i - 1.5f) * .5f, assets.MarketProps[(seed + i) % assets.MarketProps.Length], assets);
                    break;
                case 5 when assets.SoftProps != null && assets.SoftProps.Length > 0:
                    CityProps.Loose(assets.SoftProps[seed % assets.SoftProps.Length], parent, wall - origin, seed % 4 * 90f, 18f);
                    break;
            }
        }

        /// <summary>Arboles del patio interior. Son decoracion dura y dan la masa verde que separa una manzana
        /// de la siguiente cuando se mira la ciudad desde arriba.</summary>
        static void Yard(CityAssets assets, Transform parent, CityBlock block, Rect inside)
        {
            if (assets.Trees == null || assets.Trees.Length == 0 || inside.width < 2f || inside.height < 2f)
            {
                if (assets.Trees == null || assets.Trees.Length == 0)
                    ArtLog.WarnOnce("arboles", "El catalogo no trae arboles: los patios interiores quedan vacios. Ejecuta TaxiVR > City > Wire generated model slots.");
                return;
            }
            int seed = CityMath.Hash(block.Origin.x + 991, block.Origin.y - 331);
            int count = Mathf.Clamp(Mathf.RoundToInt(inside.width * inside.height / 90f), 1, 6);
            var root = Child(parent, "Patio");
            for (int i = 0; i < count; i++)
            {
                int h = CityMath.Hash(seed, i * 37);
                var point = new Vector3(inside.xMin + (h % 97) / 97f * inside.width, CityGrid.CurbHeight + .02f, inside.yMin + (h / 97 % 89) / 89f * inside.height);
                var tree = Shape.Fit(assets.Trees[h % assets.Trees.Length], root, point, new Vector2(3.4f, 3.4f), h % 4 * 90f, 1f);
                CityProps.Mark(tree, PenaltyKind.TreeOrBuilding);
            }
        }

        /// <summary>Semaforo del cruce, uno por sector. Cuatro sectores comparten cruce, asi que cada esquina
        /// aporta su cabeza, girada hacia el centro del cruce, y entre las cuatro sale el cruce completo.</summary>
        static void Signal(CityAssets assets, Transform parent, Vector2Int key)
        {
            if (CityGrid.ClosedVertical(key.x, key.y) || CityGrid.ClosedHorizontal(key.x, key.y)) return;
            bool northSouth = (Mathf.Abs(key.x + key.y) & 1) == 0;
            var position = new Vector3(key.x * CityMath.Block + 8.2f, 0, key.y * CityMath.Block + 8.2f);
            CityProps.TrafficLight(parent, position, 225f, northSouth, assets);
        }

        // ------------------------------------------------------------------ utilidades

        /// <summary>Anade una region como calzada o como acera segun si su calle existe. Las que no existen se
        /// pintan de acera, y asi la manzana rectangular queda continua en lugar de partida por una calzada que
        /// no lleva a ningun sitio.</summary>
        static void Surface(CityMesh road, CityMesh pavement, Rect rect, bool isRoad)
        {
            if (isRoad) road.AddFloor(rect, 0f, AsphaltTile);
            else pavement.AddSlab(rect, CityGrid.CurbHeight, .2f, PavementTile);
        }

        /// <summary>Eje de la calzada y pasos de cebra de una mitad de calle. La mitad es la que va del eje de
        /// la calle al borde del sector, porque la otra mitad la dibuja el vecino.</summary>
        static void Markings(CityMesh lines, CityMesh crossings, bool vertical, bool lineAtLow, float low, float high)
        {
            float band = high - low;
            float line = lineAtLow ? low + CentreLine * .5f : high - CentreLine * .5f;
            if (vertical)
            {
                lines.AddFloor(new Rect(line - CentreLine * .5f, CrossingInset + CrossingWidth + 1f, CentreLine, CityMath.Block - 2f * (CrossingInset + CrossingWidth + 1f)), MarkHeight, MarkTile);
                crossings.AddFloor(new Rect(low, CrossingInset, band, CrossingWidth), MarkHeight, MarkTile);
                crossings.AddFloor(new Rect(low, CityMath.Block - CrossingInset - CrossingWidth, band, CrossingWidth), MarkHeight, MarkTile);
            }
            else
            {
                lines.AddFloor(new Rect(CrossingInset + CrossingWidth + 1f, line - CentreLine * .5f, CityMath.Block - 2f * (CrossingInset + CrossingWidth + 1f), CentreLine), MarkHeight, MarkTile);
                crossings.AddFloor(new Rect(CrossingInset, low, CrossingWidth, band), MarkHeight, MarkTile);
                crossings.AddFloor(new Rect(CityMath.Block - CrossingInset - CrossingWidth, low, CrossingWidth, band), MarkHeight, MarkTile);
            }
        }

        /// <summary>Colisionadores de la acera, uno por region. Se solapan un poco con el suelo del sector para
        /// que el taxi no dude justo en el canto.</summary>
        static void AddPavementColliders(Transform parent, CityMesh pavement)
        {
            if (pavement.Rects.Count == 0) return;
            var go = new GameObject("Aceras");
            go.transform.SetParent(parent, false);
            foreach (var rect in pavement.Rects)
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(rect.center.x, CityGrid.CurbHeight * .5f - .02f, rect.center.y);
                box.size = new Vector3(rect.width, CityGrid.CurbHeight + .04f, rect.height);
            }
        }

        /// <summary>Rectangulo del patio interior de la manzana, en coordenadas de mundo.</summary>
        static Rect CentreRect(CityBlock block)
        {
            var size = block.CellSize;
            return new Rect(block.OriginWorld.x + size.x, block.OriginWorld.z + size.y, (block.CellsX - 2) * size.x, (block.CellsZ - 2) * size.y);
        }

        static Rect SectorRect(Vector2Int key) => new(key.x * CityMath.Block, key.y * CityMath.Block, CityMath.Block, CityMath.Block);

        static Rect Intersect(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));

        static Rect Local(Rect world, Vector3 origin) => new(world.x - origin.x, world.y - origin.z, world.width, world.height);

        /// <summary>Direccion hacia la calle de una parcela del perimetro. En las esquinas se elige por hash para
        /// que la manzana no tenga todas las esquinas mirando al mismo lado.</summary>
        static Vector3 Facing(CityBlock block, int x, int z)
        {
            bool west = x == 0, east = x == block.CellsX - 1, south = z == 0, north = z == block.CellsZ - 1;
            bool horizontal = CityMath.Hash(block.Origin.x + x, block.Origin.y + z) % 2 == 0;
            if (west && south) return horizontal ? Vector3.left : Vector3.back;
            if (west && north) return horizontal ? Vector3.left : Vector3.forward;
            if (east && south) return horizontal ? Vector3.right : Vector3.back;
            if (east && north) return horizontal ? Vector3.right : Vector3.forward;
            if (west) return Vector3.left;
            if (east) return Vector3.right;
            if (south) return Vector3.back;
            return Vector3.forward;
        }

        static float Yaw(Vector3 facing)
        {
            if (facing.x > .5f) return 90f;
            if (facing.x < -.5f) return 270f;
            return facing.z > .5f ? 0f : 180f;
        }

        static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }
    }
}
