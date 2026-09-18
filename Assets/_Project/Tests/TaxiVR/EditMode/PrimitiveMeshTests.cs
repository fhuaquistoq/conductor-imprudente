using NUnit.Framework;
using TaxiVR.Playable;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TaxiVR.Tests.EditMode
{
    /// <summary>Las primitivas se generan en codigo porque las de Unity (Cube.fbx, New-Cylinder.fbx,
    /// New-Sphere.fbx) no llegan al build del jugador y dejaban la ciudad, el trafico y las manos sin malla.
    /// Una cara con el winding al reves tampoco se ve, asi que aqui no basta con que la malla exista: se
    /// comprueba que cada triangulo mira al lado que dice su normal.</summary>
    public sealed class PrimitiveMeshTests
    {
        static readonly PrimitiveType[] Shapes =
        {
            PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Cylinder, PrimitiveType.Capsule, PrimitiveType.Quad
        };

        [Test]
        public void EveryShapeHasGeometry()
        {
            foreach (var type in Shapes)
            {
                var mesh = PrimitiveMesh.Of(type);
                Assert.That(mesh, Is.Not.Null, type.ToString());
                Assert.That(mesh.vertexCount, Is.GreaterThan(3), type.ToString());
                Assert.That(mesh.triangles.Length, Is.GreaterThan(0), type.ToString());
                Assert.That(mesh.triangles.Length % 3, Is.Zero, type.ToString());
            }
        }

        [Test]
        public void EveryTriangleFacesTheWayItsNormalSays()
        {
            foreach (var type in Shapes)
            {
                var mesh = PrimitiveMesh.Of(type);
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                var triangles = mesh.triangles;
                Assert.That(normals.Length, Is.EqualTo(vertices.Length), type + ": faltan normales");
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = vertices[triangles[i]];
                    var b = vertices[triangles[i + 1]];
                    var c = vertices[triangles[i + 2]];
                    var corner = normals[triangles[i]];
                    Assert.That(corner.magnitude, Is.EqualTo(1f).Within(.001f), type + ": normal no unitaria");
                    // Los polos de la esfera y la capsula son triangulos degenerados: no dicen nada del winding.
                    if (Vector3.Cross(b - a, c - b).sqrMagnitude < 1e-10f) continue;
                    Assert.That(Vector3.Dot(Vector3.Cross(b - a, c - b), corner), Is.GreaterThanOrEqualTo(-.0001f),
                        type + ": triangulo " + i / 3 + " del reves");
                }
            }
        }

        [TestCase(PrimitiveType.Cube, 1f, 1f, 1f)]
        [TestCase(PrimitiveType.Sphere, 1f, 1f, 1f)]
        [TestCase(PrimitiveType.Cylinder, 1f, 2f, 1f)]
        [TestCase(PrimitiveType.Capsule, 1f, 2f, 1f)]
        [TestCase(PrimitiveType.Quad, 1f, 1f, 0f)]
        public void EveryShapeKeepsTheSizeUnityGaveIt(PrimitiveType type, float x, float y, float z)
        {
            var size = PrimitiveMesh.Of(type).bounds.size;
            Assert.That(size.x, Is.EqualTo(x).Within(.02f), type.ToString());
            Assert.That(size.y, Is.EqualTo(y).Within(.02f), type.ToString());
            Assert.That(size.z, Is.EqualTo(z).Within(.02f), type.ToString());
        }

        [Test]
        public void ShapesAreSharedInsteadOfRebuiltPerObject()
        {
            Assert.That(PrimitiveMesh.Of(PrimitiveType.Cube), Is.SameAs(PrimitiveMesh.Cube));
            Assert.That(PrimitiveMesh.Of(PrimitiveType.Cylinder), Is.SameAs(PrimitiveMesh.Cylinder));
            Assert.That(PrimitiveMesh.Of(PrimitiveType.Cube), Is.SameAs(PrimitiveMesh.Of(PrimitiveType.Cube)));
        }

        /// <summary>La parte que rompia el juego: una pieza creada con Shape.Part tiene que salir con malla, y
        /// con el mismo collider (desactivado salvo que se pida) que ponia CreatePrimitive.</summary>
        [Test]
        public void PartGetsAMeshAndTheColliderUnityUsedToAdd()
        {
            var root = new GameObject("prueba de piezas");
            try
            {
                var plain = Shape.Part("Sin collider", root.transform, Vector3.zero, Vector3.one, null);
                Assert.That(plain.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(PrimitiveMesh.Cube));
                Assert.That(plain.GetComponent<MeshRenderer>(), Is.Not.Null);
                Assert.That(plain.GetComponent<Collider>(), Is.Not.Null);
                Assert.That(plain.GetComponent<Collider>().enabled, Is.False);

                var post = Shape.Part("Poste", root.transform, Vector3.zero, Vector3.one, null, PrimitiveType.Cylinder, true);
                Assert.That(post.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(PrimitiveMesh.Cylinder));
                Assert.That(post.GetComponent<CapsuleCollider>(), Is.Not.Null);
                Assert.That(post.GetComponent<Collider>().enabled, Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
