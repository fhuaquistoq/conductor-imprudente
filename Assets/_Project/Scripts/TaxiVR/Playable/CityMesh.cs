using System.Collections.Generic;
using UnityEngine;

namespace TaxiVR.Playable
{
    /// <summary>Acumulador de geometria plana de ciudad. Une todas las piezas de un mismo material en una sola
    /// malla, de modo que una manzana entera cuesta dos o tres dibujos en lugar de una docena, y las
    /// coordenadas de textura se calculan en metros de mundo para que el asfalto siga cuadrando al cruzar de un
    /// sector al siguiente.</summary>
    public sealed class CityMesh
    {
        /// <summary>Esquina del sector en el mundo. Se suma solo a las coordenadas de textura: los vertices van
        /// en coordenadas locales para que el sector se pueda mover con el origen flotante sin rehacer nada.</summary>
        public Vector3 Origin;

        /// <summary>Regiones de losa anadidas. El constructor las usa para dar colisionador a la acera.</summary>
        public readonly List<Rect> Rects = new();

        readonly List<Vector3> vertices = new();
        readonly List<Vector2> uvs = new();
        readonly List<int> triangles = new();

        public bool IsEmpty => vertices.Count == 0;

        /// <summary>Superficie horizontal. <paramref name="tile"/> es cada cuantos metros se repite la textura.</summary>
        public void AddFloor(Rect rect, float y, float tile)
        {
            AddQuad(
                new Vector3(rect.xMin, y, rect.yMin),
                new Vector3(rect.xMax, y, rect.yMin),
                new Vector3(rect.xMax, y, rect.yMax),
                new Vector3(rect.xMin, y, rect.yMax),
                tile);
        }

        /// <summary>Losa con canto: tapa superior y cuatro paredes. Es lo que convierte la acera en un bordillo
        /// visible en lugar de una pegatina; las caras interiores quedan ocultas por el descarte de caras
        /// traseras, asi que no hace falta decidir de antemano donde hay calle.</summary>
        public void AddSlab(Rect rect, float top, float thickness, float tile)
        {
            Rects.Add(rect);
            float bottom = top - thickness;
            AddFloor(rect, top, tile);
            AddWall(rect.xMin, rect.yMin, rect.xMin, rect.yMax, bottom, top, Vector3.left, tile);
            AddWall(rect.xMax, rect.yMin, rect.xMax, rect.yMax, bottom, top, Vector3.right, tile);
            AddWall(rect.xMin, rect.yMin, rect.xMax, rect.yMin, bottom, top, Vector3.back, tile);
            AddWall(rect.xMin, rect.yMax, rect.xMax, rect.yMax, bottom, top, Vector3.forward, tile);
        }

        /// <summary>Pared vertical del canto, del suelo a la tapa. La orientacion se elige para que la normal
        /// mire al lado que se le pide.</summary>
        public void AddWall(float x0, float z0, float x1, float z1, float bottom, float top, Vector3 outward, float tile)
        {
            float width = Mathf.Abs(x1 - x0), depth = Mathf.Abs(z1 - z0);
            float length = Mathf.Max(width, depth);
            if (length <= .001f || top - bottom <= .001f) return;
            float v = (top - bottom) / Mathf.Max(.001f, tile), u = length / Mathf.Max(.001f, tile);
            int start = vertices.Count;
            vertices.Add(new Vector3(x0, bottom, z0)); uvs.Add(new Vector2(0, 0));
            vertices.Add(new Vector3(x1, bottom, z1)); uvs.Add(new Vector2(u, 0));
            vertices.Add(new Vector3(x1, top, z1)); uvs.Add(new Vector2(u, v));
            vertices.Add(new Vector3(x0, top, z0)); uvs.Add(new Vector2(0, v));
            // El sentido del triangulado depende de si el muro corre en X o en Z, asi que se elige el winding
            // que deja la normal mirando al lado exterior pedido.
            bool alongX = width > depth;
            bool keep = alongX ? outward.z > 0f : outward.x < 0f;
            if (keep)
            {
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
            else
            {
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
            }
        }

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float tile)
        {
            int start = vertices.Count;
            vertices.Add(a); uvs.Add(Uv(a, tile));
            vertices.Add(b); uvs.Add(Uv(b, tile));
            vertices.Add(c); uvs.Add(Uv(c, tile));
            vertices.Add(d); uvs.Add(Uv(d, tile));
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
            triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
        }

        Vector2 Uv(Vector3 local, float tile) => new((local.x + Origin.x) / tile, (local.z + Origin.z) / tile);

        public GameObject Build(Transform parent, string name, Material material)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
