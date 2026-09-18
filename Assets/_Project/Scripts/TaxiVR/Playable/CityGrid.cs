using UnityEngine;

namespace TaxiVR.Playable
{
    /// <summary>Forma de una manzana. Cuadrada son 3x3 parcelas (8 casas) y rectangular 3x6 (14 casas).
    /// En ambos casos el centro queda vacio, que es lo que ahorra geometria sin que se note desde la calle.</summary>
    public enum BlockShape { Square, Wide, Tall }

    /// <summary>Manzana de la rejilla: un rectangulo de parcelas de casa con el centro vacio. Ocupa uno o dos
    /// sectores, y cuando ocupa dos la calle intermedia desaparece y la manzana se convierte en un rectangulo
    /// largo, que es exactamente lo que pide el diseno (3x6 o 6x3).</summary>
    public readonly struct CityBlock
    {
        public readonly Vector2Int Origin;
        public readonly BlockShape Shape;
        public readonly int CellsX;
        public readonly int CellsZ;

        public CityBlock(Vector2Int origin, BlockShape shape, int cellsX, int cellsZ)
        {
            Origin = origin;
            Shape = shape;
            CellsX = cellsX;
            CellsZ = cellsZ;
        }

        /// <summary>Sectores que ocupa la manzana en cada eje: 1, o 2 si la calle intermedia se elimino.</summary>
        public int SectorsX => Shape == BlockShape.Wide ? 2 : 1;
        public int SectorsZ => Shape == BlockShape.Tall ? 2 : 1;

        /// <summary>Casas de la manzana: el perimetro de la rejilla de parcelas. 8 en el cuadrado y 14 en el
        /// rectangulo, porque el centro no se construye.</summary>
        public int Houses => 2 * (CellsX + CellsZ) - 4;

        /// <summary>Parcela de casa, en metros. Es rectangular a proposito: la manzana que ocupa dos sectores
        /// reparte el largo extra entre sus parcelas en lugar de estirar la ciudad.</summary>
        public Vector2 CellSize => new(
            (SectorsX * CityMath.Block - 2f * CityGrid.Inset) / CellsX,
            (SectorsZ * CityMath.Block - 2f * CityGrid.Inset) / CellsZ);

        /// <summary>Esquina suroeste de la manzana, ya descontada la acera.</summary>
        public Vector3 OriginWorld => new(Origin.x * CityMath.Block + CityGrid.Inset, 0, Origin.y * CityMath.Block + CityGrid.Inset);

        public Vector3 SizeWorld => new(SectorsX * CityMath.Block - 2f * CityGrid.Inset, 0, SectorsZ * CityMath.Block - 2f * CityGrid.Inset);

        /// <summary>La parcela toca la calle: es donde van las casas. El resto es el patio interior.</summary>
        public bool IsPerimeter(int x, int z) => x == 0 || z == 0 || x == CellsX - 1 || z == CellsZ - 1;

        /// <summary>Centro de una parcela, en metros de mundo.</summary>
        public Vector3 CellCentre(int x, int z)
        {
            var size = CellSize;
            var origin = OriginWorld;
            return new Vector3(origin.x + (x + .5f) * size.x, 0, origin.z + (z + .5f) * size.y);
        }

        /// <summary>Rectangulo de una parcela en el plano XZ.</summary>
        public Rect CellRect(int x, int z)
        {
            var size = CellSize;
            var origin = OriginWorld;
            return new Rect(origin.x + x * size.x, origin.z + z * size.y, size.x, size.y);
        }

        /// <summary>Rectangulo del patio interior (las parcelas que no se construyen).</summary>
        public Rect CentreRect()
        {
            var size = CellSize;
            var origin = OriginWorld;
            return new Rect(origin.x + size.x, origin.z + size.y, (CellsX - 2) * size.x, (CellsZ - 2) * size.y);
        }
    }

    /// <summary>Rejilla urbana determinista. Todas las calles son rectas y todos los cruces existen, salvo la
    /// calle que se come una manzana rectangular; el mundo es una cuadricula de 64 m con manzanas de 8 o 14
    /// casas. La misma funcion decide la geometria y el grafo, de modo que el GPS nunca puede mandar al taxista
    /// por una calle que no esta dibujada.</summary>
    public static class CityGrid
    {
        /// <summary>Sectores que agrupa cada sorteo de manzanas.</summary>
        public const int SectorsPerSuperblock = 2;

        /// <summary>Mitad del ancho de calzada. Deja los carriles a +-3 m, que es donde circula el trafico.</summary>
        public const float RoadHalfWidth = 6f;

        /// <summary>Acera entre la calzada y las casas.</summary>
        public const float SidewalkWidth = 4f;

        /// <summary>Distancia del eje de calle al borde de la manzana (calzada + acera).</summary>
        public const float Inset = RoadHalfWidth + SidewalkWidth;

        /// <summary>Altura del bordillo. Lo justo para que se lea la acera sin que el taxi note un escalon.</summary>
        public const float CurbHeight = .15f;

        /// <summary>Parcelas por lado corto de manzana. El largo es el doble en la manzana rectangular.</summary>
        public const int ShortSideCells = 3;
        public const int LongSideCells = 6;

        public static int FloorDiv(int value, int divisor) => Mathf.FloorToInt(value / (float)divisor);

        /// <summary>Reparto determinista del superbloque: cuatro manzanas cuadradas, dos anchas o dos altas.</summary>
        public static BlockShape ShapeOf(Vector2Int superblock) => (BlockShape)(CityMath.Hash(superblock.x, superblock.y) % 3);

        /// <summary>Manzana a la que pertenece un sector. Todo sector tiene exactamente una.</summary>
        public static CityBlock BlockAt(Vector2Int sector)
        {
            var superblock = new Vector2Int(FloorDiv(sector.x, SectorsPerSuperblock), FloorDiv(sector.y, SectorsPerSuperblock));
            var shape = ShapeOf(superblock);
            if (shape == BlockShape.Wide)
                return new CityBlock(new Vector2Int(superblock.x * SectorsPerSuperblock, sector.y), shape, LongSideCells, ShortSideCells);
            if (shape == BlockShape.Tall)
                return new CityBlock(new Vector2Int(sector.x, superblock.y * SectorsPerSuperblock), shape, ShortSideCells, LongSideCells);
            return new CityBlock(sector, shape, ShortSideCells, ShortSideCells);
        }

        /// <summary>La calle que atraviesa una manzana rectangular no existe. Linea vertical = eje x constante;
        /// la celda es el tramo entre dos cruces.</summary>
        public static bool ClosedVertical(int line, int cell)
        {
            if (line % 2 == 0) return false;
            return ShapeOf(new Vector2Int(FloorDiv(line, SectorsPerSuperblock), FloorDiv(cell, SectorsPerSuperblock))) == BlockShape.Wide;
        }

        public static bool ClosedHorizontal(int cell, int line)
        {
            if (line % 2 == 0) return false;
            return ShapeOf(new Vector2Int(FloorDiv(cell, SectorsPerSuperblock), FloorDiv(line, SectorsPerSuperblock))) == BlockShape.Tall;
        }

        /// <summary>True si el tramo entre dos cruces contiguos esta ocupado por una manzana. El grafo vial
        /// consulta esto para no ofrecer una calle que la ciudad no dibuja.</summary>
        public static bool IsBlocked(Vector2Int from, Vector2Int to)
        {
            if (to == from + Vector2Int.up) return ClosedVertical(from.x, from.y);
            if (to == from + Vector2Int.down) return ClosedVertical(to.x, to.y);
            if (to == from + Vector2Int.right) return ClosedHorizontal(from.x, from.y);
            if (to == from + Vector2Int.left) return ClosedHorizontal(to.x, to.y);
            return false;
        }
    }
}
