using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using TaxiVR.Gameplay;
using TaxiVR.Playable;

namespace TaxiVR.Tests.EditMode
{
    /// <summary>Invariantes de la ciudad de rejilla. Son los que sostienen que la calle que se ve sea la que el
    /// grafo ofrece: si alguno se rompe, el GPS puede mandar al taxista contra las casas de una manzana larga.</summary>
    public sealed class CityGridTests
    {
        const int Samples = 8;

        static IEnumerable<Vector2Int> Superblocks()
        {
            for (int x = -Samples; x <= Samples; x++)
                for (int z = -Samples; z <= Samples; z++) yield return new Vector2Int(x, z);
        }

        [Test]
        public void EverySectorBelongsToExactlyOneBlockThatContainsIt()
        {
            for (int x = -30; x <= 30; x++)
                for (int z = -30; z <= 30; z++)
                {
                    var sector = new Vector2Int(x, z);
                    var block = CityGrid.BlockAt(sector);
                    Assert.That(sector.x, Is.InRange(block.Origin.x, block.Origin.x + block.SectorsX - 1), "Sector fuera de su manzana");
                    Assert.That(sector.y, Is.InRange(block.Origin.y, block.Origin.y + block.SectorsZ - 1), "Sector fuera de su manzana");
                }
        }

        [Test]
        public void BlocksAreThreeByThreeWithEightHousesOrThreeBySixWithFourteen()
        {
            foreach (var superblock in Superblocks())
            {
                var shape = CityGrid.ShapeOf(superblock);
                var block = CityGrid.BlockAt(new Vector2Int(superblock.x * CityGrid.SectorsPerSuperblock, superblock.y * CityGrid.SectorsPerSuperblock));
                Assert.AreEqual(shape, block.Shape);
                Assert.AreEqual(3, Mathf.Min(block.CellsX, block.CellsZ), "El lado corto siempre tiene tres parcelas");
                Assert.AreEqual(shape == BlockShape.Square ? 3 : 6, Mathf.Max(block.CellsX, block.CellsZ));
                Assert.AreEqual(shape == BlockShape.Square ? 8 : 14, block.Houses, "Casas por manzana");
                Assert.AreEqual(2 * (block.CellsX + block.CellsZ) - 4, CountPerimeter(block), "El centro de la manzana queda vacio");
            }
        }

        [Test]
        public void ARectangularBlockCoversTwoSectorsAndDropsTheStreetBetweenThem()
        {
            foreach (var superblock in Superblocks())
            {
                var shape = CityGrid.ShapeOf(superblock);
                if (shape == BlockShape.Square) continue;
                var first = new Vector2Int(superblock.x * 2, superblock.y * 2);
                var along = shape == BlockShape.Wide ? Vector2Int.right : Vector2Int.up;
                var across = shape == BlockShape.Wide ? Vector2Int.up : Vector2Int.right;

                Assert.AreEqual(CityGrid.BlockAt(first).Origin, CityGrid.BlockAt(first + along).Origin,
                    "Los dos sectores de una manzana rectangular son la misma manzana");
                Assert.AreNotEqual(CityGrid.BlockAt(first).Origin, CityGrid.BlockAt(first + across).Origin,
                    "El eje corto no se funde con la fila vecina");

                var block = CityGrid.BlockAt(first);
                float merged = shape == BlockShape.Wide ? block.SizeWorld.x : block.SizeWorld.z;
                float shortSide = shape == BlockShape.Wide ? block.SizeWorld.z : block.SizeWorld.x;
                Assert.AreEqual(2f * CityMath.Block - 2f * CityGrid.Inset, merged, .01f, "La calle intermedia desaparece");
                Assert.AreEqual(CityMath.Block - 2f * CityGrid.Inset, shortSide, .01f);
            }
        }

        [Test]
        public void TheStreetInsideARectangleIsTheOnlyOneBlocked()
        {
            foreach (var superblock in Superblocks())
            {
                bool wide = CityGrid.ShapeOf(superblock) == BlockShape.Wide;
                bool tall = CityGrid.ShapeOf(superblock) == BlockShape.Tall;
                int line = superblock.x * 2 + 1, row = superblock.y * 2 + 1;
                if (wide)
                {
                    Assert.IsTrue(CityGrid.ClosedVertical(line, superblock.y * 2), "La calle interior de la manzana ancha debe estar cerrada");
                    Assert.IsTrue(CityGrid.ClosedVertical(line, superblock.y * 2 + 1));
                    Assert.IsFalse(CityGrid.ClosedHorizontal(superblock.x * 2, row));
                }
                else
                {
                    Assert.IsFalse(CityGrid.ClosedVertical(line, superblock.y * 2));
                }
                if (tall)
                {
                    Assert.IsTrue(CityGrid.ClosedHorizontal(superblock.x * 2, row), "La calle interior de la manzana alta debe estar cerrada");
                    Assert.IsTrue(CityGrid.ClosedHorizontal(superblock.x * 2 + 1, row));
                }
                else
                {
                    Assert.IsFalse(CityGrid.ClosedHorizontal(superblock.x * 2, row));
                }
            }
        }

        [Test]
        public void ClosingAStreetIsSymmetricAndLeavesMostOfTheGridOpen()
        {
            int blocked = 0, total = 0;
            for (int x = -20; x <= 20; x++)
                for (int z = -20; z <= 20; z++)
                {
                    var node = new Vector2Int(x, z);
                    foreach (var step in new[] { Vector2Int.right, Vector2Int.up })
                    {
                        bool closed = CityGrid.IsBlocked(node, node + step);
                        Assert.AreEqual(closed, CityGrid.IsBlocked(node + step, node), "El cierre debe leerse igual en los dos sentidos");
                        total++;
                        if (closed) blocked++;
                    }
                }
            Assert.Greater(blocked, 0, "Sin manzanas rectangulares no se estaria ejerciendo el caso");
            Assert.Less(blocked, total / 4, "Una manzana rectangular solo se come una calle, no un barrio");
        }

        [Test]
        public void HousePlotsFillTheBlockWithoutLeavingIt()
        {
            foreach (var superblock in Superblocks())
            {
                var block = CityGrid.BlockAt(new Vector2Int(superblock.x * 2, superblock.y * 2));
                var origin = block.OriginWorld;
                var size = block.SizeWorld;
                var external = new List<Rect>();
                for (int x = 0; x < block.CellsX; x++)
                    for (int z = 0; z < block.CellsZ; z++)
                    {
                        var rect = block.CellRect(x, z);
                        Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(origin.x - .01f));
                        Assert.That(rect.xMax, Is.LessThanOrEqualTo(origin.x + size.x + .01f));
                        Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(origin.z - .01f));
                        Assert.That(rect.yMax, Is.LessThanOrEqualTo(origin.z + size.z + .01f));
                        if (!block.IsPerimeter(x, z)) continue;
                        // Se encoge una micra antes de comparar: dos parcelas contiguas comparten borde, y en
                        // coma flotante ese borde puede caer una diezmillonesima a un lado u otro.
                        var inner = new Rect(rect.x + 1e-3f, rect.y + 1e-3f, rect.width - 2e-3f, rect.height - 2e-3f);
                        foreach (var other in external) Assert.IsFalse(inner.Overlaps(other), "Dos parcelas de la misma manzana no se solapan");
                        external.Add(inner);
                    }
                Assert.AreEqual(block.Houses, external.Count);
            }
        }

        [Test]
        public void TheGridStaysConnectedWithTheRectangleStreetsRemoved()
        {
            var graph = new CityGraph(1234, 1f, 0f, 0f, 0f) { Blocked = CityGrid.IsBlocked };
            var reached = graph.Reachable(Vector2Int.zero, null, 4000).ToList();
            Assert.Greater(reached.Count, 1000, "La rejilla debe seguir siendo un mundo abierto");
            foreach (var target in new[] { new Vector2Int(20, 20), new Vector2Int(-20, 18), new Vector2Int(15, -15) })
                Assert.IsTrue(reached.Contains(target), "Sin ruta hasta " + target);
        }

        [Test]
        public void TheGridStillOffersAlternativeRoutesToADestination()
        {
            var graph = new CityGraph(77, 1f, .05f, 0f, 0f) { Blocked = CityGrid.IsBlocked };
            Assert.IsTrue(graph.TryPickDestination(Vector2Int.zero, 3, 225f, 255f, out var destination, out var routes, 4, 32),
                "La ciudad de rejilla debe seguir dando destinos dentro de la ventana de tiempo");
            Assert.GreaterOrEqual(routes.Count, 2, "El destino debe ofrecer alternativas, no una unica ruta");
            Assert.AreNotEqual(Vector2Int.zero, destination);
            float optimal = graph.PathEta(routes[0]);
            Assert.That(optimal, Is.InRange(225f, 255f));
            for (int i = 1; i < routes.Count; i++)
                Assert.LessOrEqual(graph.PathEta(routes[i]), optimal * 1.35f, "Alternativa demasiado lenta");
        }

        [Test]
        public void HousePlotsTileTheBlockWithoutInvadingTheRoadCorridor()
        {
            var block = CityGrid.BlockAt(Vector2Int.zero);
            var first = block.CellRect(0, 0);
            var last = block.CellRect(block.CellsX - 1, block.CellsZ - 1);
            // Las parcelas cubren la manzana de borde a borde: si la cuenta fallara, sobraria hueco o la
            // parcela se comeria la acera. No es una constante contra si misma: mide parcelas reales.
            Assert.AreEqual(block.OriginWorld.x, first.xMin, .001f, "La primera parcela arranca en el borde de la manzana");
            Assert.AreEqual(block.OriginWorld.z, first.yMin, .001f);
            Assert.AreEqual(block.OriginWorld.x + block.SizeWorld.x, last.xMax, .001f, "La ultima parcela cierra la manzana");
            Assert.AreEqual(block.OriginWorld.z + block.SizeWorld.z, last.yMax, .001f);
        }

        static int CountPerimeter(CityBlock block)
        {
            int count = 0;
            for (int x = 0; x < block.CellsX; x++)
                for (int z = 0; z < block.CellsZ; z++)
                    if (block.IsPerimeter(x, z)) count++;
            return count;
        }
    }
}
