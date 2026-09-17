using UnityEngine;

namespace TaxiVR.Playable
{
    public sealed class CityAssets : ScriptableObject
    {
        public GameObject[] Buildings;
        public GameObject[] Cars;
        public GameObject Taxi;
        public Material Asphalt, Pavement, Dark, Yellow, White, Glass, Grass, Foliage, Skin, Red, Blue;
        public Material[] Facades;
        public Material DistantBuilding;
        public Shader UnlitShader;
        public Font Font;
    }
    public static class Shape
    {
        public static GameObject Part(string name, Transform parent, Vector3 position, Vector3 size, Material material, PrimitiveType type = PrimitiveType.Cube, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) { var c = go.GetComponent<Collider>(); c.enabled = false; Object.Destroy(c); }
            return go;
        }
        public static TextMesh Label(string name, Transform parent, Vector3 position, string text, float size, Color color, Font font)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
            var label = go.AddComponent<TextMesh>(); label.text = text; label.font = font; label.fontSize = 64;
            label.characterSize = size; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = color;
            if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return label;
        }
        public static GameObject Model(GameObject prefab, Transform parent, Vector3 position, float height, float yaw = 0)
        {
            var pivot = new GameObject(prefab.name); pivot.transform.SetParent(parent, false); pivot.transform.localPosition = position;
            var obj = Object.Instantiate(prefab, pivot.transform);
            obj.transform.localPosition = Vector3.zero; obj.transform.localRotation = Quaternion.identity;
            var renderers = obj.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(); bool first = true;
            foreach (var r in renderers) { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
            float scale = height / Mathf.Max(.01f, bounds.size.y);
            obj.transform.localScale *= scale;
            obj.transform.localPosition = new Vector3(-bounds.center.x + pivot.transform.position.x, -bounds.min.y + pivot.transform.position.y, -bounds.center.z + pivot.transform.position.z) * scale;
            pivot.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            return pivot;
        }
    }
}
