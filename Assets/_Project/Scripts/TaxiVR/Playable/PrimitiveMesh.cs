using System.Collections.Generic;
using UnityEngine;

namespace TaxiVR.Playable
{
    /// <summary>Mallas de las primitivas basicas, generadas en codigo y compartidas por todos los objetos que
    /// las usan. GameObject.CreatePrimitive pide la suya al fichero de recursos integrados de Unity (Cube.fbx,
    /// New-Cylinder.fbx, New-Sphere.fbx), que en un build de jugador puede no venir: el objeto se crea igual
    /// pero sin malla, de modo que la ciudad, el trafico, los peatones y las manos quedaban invisibles sin que
    /// ninguna comprobacion logica fallara. Generarlas aqui quita esa dependencia y comparte una sola malla por
    /// forma, en vez de una por objeto.</summary>
    public static class PrimitiveMesh
    {
        const int Segments = 24;
        const int SphereRings = 12;
        const int CapRings = 6;

        static Mesh cube, sphere, cylinder, capsule, quad;

        public static Mesh Cube => cube != null ? cube : cube = Box();
        public static Mesh Sphere => sphere != null ? sphere : sphere = Revolve("Sphere", SphereProfile(), Segments);
        public static Mesh Cylinder => cylinder != null ? cylinder : cylinder = Revolve("Cylinder", new[] { new Vector2(.5f, -1f), new Vector2(.5f, 1f) }, Segments);
        public static Mesh Capsule => capsule != null ? capsule : capsule = Revolve("Capsule", CapsuleProfile(), Segments);
        public static Mesh Quad => quad != null ? quad : quad = Panel();

        public static Mesh Of(PrimitiveType type)
        {
            switch (type)
            {
                case PrimitiveType.Sphere: return Sphere;
                case PrimitiveType.Cylinder: return Cylinder;
                case PrimitiveType.Capsule: return Capsule;
                case PrimitiveType.Quad: return Quad;
                default: return Cube;
            }
        }

        /// <summary>Cubo unidad centrado en el origen. Cada cara lleva sus propios vertices para que la normal
        /// sea plana y no se redondeen las aristas.</summary>
        static Mesh Box()
        {
            var builder = new Builder();
            builder.Face(new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.5f, -.5f), new Vector3(.5f, .5f, -.5f), new Vector3(-.5f, .5f, -.5f), Vector3.back);
            builder.Face(new Vector3(-.5f, -.5f, .5f), new Vector3(.5f, -.5f, .5f), new Vector3(.5f, .5f, .5f), new Vector3(-.5f, .5f, .5f), Vector3.forward);
            builder.Face(new Vector3(-.5f, -.5f, -.5f), new Vector3(-.5f, -.5f, .5f), new Vector3(-.5f, .5f, .5f), new Vector3(-.5f, .5f, -.5f), Vector3.left);
            builder.Face(new Vector3(.5f, -.5f, -.5f), new Vector3(.5f, -.5f, .5f), new Vector3(.5f, .5f, .5f), new Vector3(.5f, .5f, -.5f), Vector3.right);
            builder.Face(new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.5f, -.5f), new Vector3(.5f, -.5f, .5f), new Vector3(-.5f, -.5f, .5f), Vector3.down);
            builder.Face(new Vector3(-.5f, .5f, -.5f), new Vector3(.5f, .5f, -.5f), new Vector3(.5f, .5f, .5f), new Vector3(-.5f, .5f, .5f), Vector3.up);
            return builder.Build("Cube");
        }

        /// <summary>Cuadrado unidad en el plano XY mirando a -Z, igual que la primitiva Quad de Unity.</summary>
        static Mesh Panel()
        {
            var builder = new Builder();
            builder.Face(new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0), Vector3.back);
            return builder.Build("Quad");
        }

        /// <summary>Perfil (radio, altura) de abajo arriba. La esfera y la capsula cierran en radio cero en los
        /// polos; el cilindro acaba en pared vertical y por eso lleva tapas.</summary>
        static Vector2[] SphereProfile()
        {
            var profile = new Vector2[SphereRings + 1];
            for (int i = 0; i <= SphereRings; i++)
            {
                float angle = Mathf.Lerp(-Mathf.PI * .5f, Mathf.PI * .5f, (float)i / SphereRings);
                profile[i] = new Vector2(Mathf.Cos(angle) * .5f, Mathf.Sin(angle) * .5f);
            }
            return profile;
        }

        static Vector2[] CapsuleProfile()
        {
            var profile = new List<Vector2>();
            for (int i = 0; i <= CapRings; i++)
            {
                float angle = Mathf.Lerp(-Mathf.PI * .5f, 0f, (float)i / CapRings);
                profile.Add(new Vector2(Mathf.Cos(angle) * .5f, -.5f + Mathf.Sin(angle) * .5f));
            }
            profile.Add(new Vector2(.5f, .5f));
            for (int i = 1; i <= CapRings; i++)
            {
                float angle = Mathf.Lerp(0f, Mathf.PI * .5f, (float)i / CapRings);
                profile.Add(new Vector2(Mathf.Cos(angle) * .5f, .5f + Mathf.Sin(angle) * .5f));
            }
            return profile.ToArray();
        }

        /// <summary>Revoluciona un perfil alrededor del eje Y. La normal de cada punto sale de la perpendicular a
        /// la cuerda del perfil, asi que la pared del cilindro mira hacia fuera y el polo de la esfera hacia
        /// abajo sin tener que decirlo forma por forma.</summary>
        static Mesh Revolve(string name, Vector2[] profile, int segments)
        {
            var flat = new Vector2[profile.Length];
            for (int i = 0; i < profile.Length; i++)
            {
                Vector2 before = i > 0 ? profile[i] - profile[i - 1] : profile[1] - profile[0];
                Vector2 after = i < profile.Length - 1 ? profile[i + 1] - profile[i] : profile[i] - profile[i - 1];
                Vector2 tangent = before.normalized + after.normalized;
                if (tangent.sqrMagnitude < .0001f) tangent = after;
                flat[i] = new Vector2(tangent.y, -tangent.x).normalized;
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i < profile.Length; i++)
                for (int j = 0; j <= segments; j++)
                {
                    float angle = Mathf.PI * 2f * j / segments;
                    float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                    vertices.Add(new Vector3(profile[i].x * cos, profile[i].y, profile[i].x * sin));
                    normals.Add(new Vector3(flat[i].x * cos, flat[i].y, flat[i].x * sin).normalized);
                    uvs.Add(new Vector2((float)j / segments, (profile[i].y + 1f) * .5f));
                }

            for (int i = 0; i < profile.Length - 1; i++)
                for (int j = 0; j < segments; j++)
                {
                    int a = i * (segments + 1) + j;
                    int start = triangles.Count;
                    triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + segments + 2);
                    triangles.Add(a); triangles.Add(a + segments + 2); triangles.Add(a + segments + 1);
                    Winding(vertices, normals, triangles, start);
                }

            if (profile[0].x > .001f) Cap(vertices, normals, uvs, triangles, profile[0], -1f, segments);
            if (profile[profile.Length - 1].x > .001f) Cap(vertices, normals, uvs, triangles, profile[profile.Length - 1], 1f, segments);

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Tapa plana de un tubo: abanico desde el centro del disco hasta el anillo del perfil.</summary>
        static void Cap(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles, Vector2 edge, float up, int segments)
        {
            int centre = vertices.Count;
            vertices.Add(new Vector3(0, edge.y, 0));
            normals.Add(new Vector3(0, up, 0));
            uvs.Add(new Vector2(.5f, .5f));
            for (int j = 0; j <= segments; j++)
            {
                float angle = Mathf.PI * 2f * j / segments;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                vertices.Add(new Vector3(edge.x * cos, edge.y, edge.x * sin));
                normals.Add(new Vector3(0, up, 0));
                uvs.Add(new Vector2(.5f + cos * .5f, .5f + sin * .5f));
            }
            for (int j = 0; j < segments; j++)
            {
                int start = triangles.Count;
                triangles.Add(centre); triangles.Add(centre + 1 + j); triangles.Add(centre + 2 + j);
                Winding(vertices, normals, triangles, start);
            }
        }

        /// <summary>Si un triangulo recien anadido mira al lado contrario que su normal, se le da la vuelta. Se
        /// revisan uno a uno y no por parejas: junto a los polos de la esfera y la capsula uno de los dos
        /// triangulos del cuadrilatero es degenerado, y darlo por bueno dejaba el otro del reves.</summary>
        static void Winding(List<Vector3> vertices, List<Vector3> normals, List<int> triangles, int start)
        {
            for (int i = start; i < triangles.Count; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                var winding = Vector3.Cross(b - a, c - b);
                if (winding.sqrMagnitude < 1e-10f) continue;
                if (Vector3.Dot(winding, normals[triangles[i]]) >= 0f) continue;
                int first = triangles[i];
                triangles[i] = triangles[i + 2];
                triangles[i + 2] = first;
            }
        }

        sealed class Builder
        {
            readonly List<Vector3> vertices = new();
            readonly List<Vector3> normals = new();
            readonly List<Vector2> uvs = new();
            readonly List<int> triangles = new();

            /// <summary>Cara de cuatro esquinas en orden ciclico. El winding se corrige solo contra el normal.</summary>
            public Builder Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                for (int i = 0; i < 4; i++) normals.Add(normal);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                int begin = triangles.Count;
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
                Winding(vertices, normals, triangles, begin);
                return this;
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
