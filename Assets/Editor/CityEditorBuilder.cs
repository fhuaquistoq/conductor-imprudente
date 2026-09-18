using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaxiVR.City;
using UnityEditor;
using UnityEngine;

namespace TaxiVR.City.Editor
{
    public static class CityEditorBuilder
    {
        const string SourcePath = "Assets/_Project/Art/Buildings/Models";
        const string RootPath = "Assets/_Project/City";
        const string ModulePath = RootPath + "/Prefabs/Modules";
        const string BuildingPath = RootPath + "/Prefabs/Buildings";
        const string SectorPath = RootPath + "/Prefabs/Sectors";
        const string DataPath = RootPath + "/Data";

        [MenuItem("TaxiVR/City/Create editor preview")]
        public static void CreateEditorPreview()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Open the main scene first.");
            var old = GameObject.Find("Editor City Preview");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var assets = AssetDatabase.LoadAssetAtPath<TaxiVR.Playable.CityAssets>("Assets/_Project/PlayableData/CityAssets.asset");
            if (assets == null || assets.Catalog == null || assets.Catalog.SectorTemplates == null || assets.Taxi == null) throw new InvalidOperationException("City catalog or taxi is missing.");
            var root = new GameObject("Editor City Preview") { tag = "EditorOnly" };
            var templates = assets.Catalog.SectorTemplates;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            {
                var prefab = templates[TaxiVR.Playable.CityMath.TemplateIndex(new Vector2Int(x, z), templates.Length)];
                var instance = PrefabUtility.InstantiatePrefab(prefab, root.transform) as GameObject;
                instance.name = $"Editor Sector {x},{z}";
                instance.transform.localPosition = new Vector3(x * TaxiVR.Playable.CityMath.Block, 0, z * TaxiVR.Playable.CityMath.Block);
                MarkEditorOnly(instance);
            }
            var taxi = PrefabUtility.InstantiatePrefab(assets.Taxi, root.transform) as GameObject;
            taxi.name = "Editor Taxi Preview";
            taxi.transform.localPosition = new Vector3(3, .1f, 22);
            MarkEditorOnly(taxi);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }

        static void MarkEditorOnly(GameObject root)
        {
            root.tag = "EditorOnly";
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.tag = "EditorOnly";
        }

        [MenuItem("TaxiVR/City/Build DowntownCity prefab catalog")]
        public static void Build()
        {
            EnsureFolders();
            var modules = BuildModules();
            var buildings = BuildBuildings(modules);
            var sectors = BuildSectors(modules, buildings);
            var catalog = LoadOrCreate<CityCatalog>(DataPath + "/DowntownCityCatalog.asset");
            catalog.LoadRadius = 3;
            catalog.DetailRadius = 1;
            catalog.SectorTemplates = sectors;
            catalog.BuildingPrefabs = buildings;
            catalog.Asphalt = FindMaterial("Asphalt");
            catalog.Pavement = FindMaterial("Pavement");
            catalog.Grass = FindMaterial("Grass");
            catalog.FacadeDefault = FindMaterial("FacadeDefault");
            catalog.Lamp = FindMaterial("Lamp");
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"DowntownCity catalog built: {modules.Count} modules, {buildings.Length} buildings, {sectors.Length} sectors.");
        }

        static void EnsureFolders()
        {
            EnsureFolder("Assets/_Project", "City");
            EnsureFolder(RootPath, "Prefabs");
            EnsureFolder(RootPath + "/Prefabs", "Modules");
            EnsureFolder(RootPath + "/Prefabs", "Buildings");
            EnsureFolder(RootPath + "/Prefabs", "Sectors");
            EnsureFolder(RootPath, "Data");
        }

        static void EnsureFolder(string parent, string child)
        {
            var path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }

        static Dictionary<string, GameObject> BuildModules()
        {
            var result = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { SourcePath }))
            {
                var sourcePath = AssetDatabase.GUIDToAssetPath(guid);
                if (!sourcePath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if (source == null) continue;
                var name = Path.GetFileNameWithoutExtension(sourcePath);
                var prefab = SavePrefab(source, ModulePath + "/" + name + ".prefab", IsSolid(name));
                MarkObstacle(prefab, name);
                result[name] = prefab;
            }
            return result;
        }

        /// <summary>Solo los obstaculos sueltos llevan collider: ponerlo en cada adorno multiplicaria los
        /// cuerpos fisicos de la manzana sin que el jugador note la diferencia.</summary>
        static bool IsSolid(string name) =>
            name.StartsWith("Prop_", StringComparison.Ordinal) ||
            name.StartsWith("Stairs_", StringComparison.Ordinal) ||
            name.IndexOf("Bollard", StringComparison.Ordinal) >= 0 ||
            name.IndexOf("Planter", StringComparison.Ordinal) >= 0;

        /// <summary>Marca el obstaculo con la penalizacion que corresponde al chocar con el, para que tirar una
        /// papelera cueste menos que arrancar un arbol.</summary>
        static void MarkObstacle(GameObject prefab, string name)
        {
            if (prefab == null || prefab.GetComponent<TaxiVR.Playable.PenaltyMarker>() != null) return;
            TaxiVR.Gameplay.PenaltyKind? kind =
                name.StartsWith("Prop_", StringComparison.Ordinal) ? TaxiVR.Gameplay.PenaltyKind.TrashOrProp :
                name.IndexOf("Bollard", StringComparison.Ordinal) >= 0 || name.IndexOf("Planter", StringComparison.Ordinal) >= 0 ? TaxiVR.Gameplay.PenaltyKind.Barrier :
                (TaxiVR.Gameplay.PenaltyKind?)null;
            if (kind == null) return;
            var marker = prefab.AddComponent<TaxiVR.Playable.PenaltyMarker>();
            marker.Kind = kind.Value;
            EditorUtility.SetDirty(prefab);
        }

        static GameObject[] BuildBuildings(Dictionary<string, GameObject> modules)
        {
            return new[] { "Building_Small_1", "Building_Medium_2_001", "Building_Large_2" }
                .Where(modules.ContainsKey)
                .Select(name => SavePrefab(modules[name], BuildingPath + "/" + name + ".prefab", true))
                .ToArray();
        }

        static GameObject[] BuildSectors(Dictionary<string, GameObject> modules, GameObject[] buildings)
        {
            var roadNames = new[] { "Street_4WayIntersection", "Street_TIntersection", "Street_4Lane", "Street_2Lane", "Street_Curve_2Lane", "Street_Curve_4LaneShort" };
            var sidewalkNames = new[] { "Sidewalk_Straight_3m", "Sidewalk_Corner_Flat_3m", "Sidewalk_Corner_Round_3m", "Sidewalk_Planter" };
            var propNames = new[] { "Prop_Bollard", "Prop_Planter_Single", "Prop_ManholeCover", "Prop_Drain", "Prop_ACUnit", "Stairs_Entrance_Concrete" };
            var sectors = new List<GameObject>();
            for (int i = 0; i < 6; i++)
            {
                var root = new GameObject("Downtown Sector " + (i + 1));
                var metadata = root.AddComponent<CitySectorTemplate>();
                metadata.TemplateId = "Downtown_" + (i + 1).ToString("00");
                metadata.AllowedRotations = new[] { true, true, true, true };
                // El cruce va en el origen del sector, que es exactamente donde el CityGraph pone un nudo y
                // donde el trafico y el taxi suponen que esta la calzada. Antes se colocaba en el centro de la
                // manzana y el mundo jugable quedaba sin calles.
                AddModel(root.transform, modules, roadNames[i], Vector3.zero, Quaternion.identity, false);
                AddGround(root.transform);
                AddModel(root.transform, modules, sidewalkNames[i % sidewalkNames.Length], new Vector3(32, .05f, 12), Quaternion.identity, false);
                AddModel(root.transform, modules, sidewalkNames[(i + 1) % sidewalkNames.Length], new Vector3(32, .05f, 52), Quaternion.Euler(0, 180, 0), false);
                AddModel(root.transform, modules, sidewalkNames[(i + 2) % sidewalkNames.Length], new Vector3(12, .05f, 32), Quaternion.Euler(0, 90, 0), false);
                AddModel(root.transform, modules, sidewalkNames[(i + 3) % sidewalkNames.Length], new Vector3(52, .05f, 32), Quaternion.Euler(0, -90, 0), false);
                for (int plot = 0; plot < 4; plot++)
                {
                    if (buildings.Length == 0) break;
                    var building = buildings[(i + plot) % buildings.Length];
                    var position = new Vector3(plot % 2 == 0 ? 18 : 46, 0, plot < 2 ? 18 : 46);
                    AddPrefab(root.transform, building, position, Quaternion.Euler(0, plot % 2 == 0 ? 180 : 0, 0), true);
                }
                for (int prop = 0; prop < 3; prop++)
                {
                    var name = propNames[(i * 3 + prop) % propNames.Length];
                    AddModel(root.transform, modules, name, new Vector3(20 + prop * 7, .2f, 20 + i % 3 * 7), Quaternion.identity, false);
                }
                sectors.Add(SaveScenePrefab(root, SectorPath + "/" + metadata.TemplateId + ".prefab"));
                UnityEngine.Object.DestroyImmediate(root);
            }
            return sectors.ToArray();
        }

        static GameObject AddModel(Transform parent, IReadOnlyDictionary<string, GameObject> modules, string name, Vector3 position, Quaternion rotation, bool collider)
        {
            if (!modules.TryGetValue(name, out var prefab)) return null;
            return AddPrefab(parent, prefab, position, rotation, collider);
        }

        /// <summary>Suelo fisico de la manzana. El kit de arte no trae una losa continua, asi que sin esto el
        /// taxi cae al vacio en cuanto se aleja del cruce. Es invisible: solo existe para poder rodar.</summary>
        static void AddGround(Transform parent)
        {
            var ground = new GameObject("Suelo fisico");
            ground.transform.SetParent(parent, false);
            ground.transform.localPosition = new Vector3(0, -.2f, 0);
            var box = ground.AddComponent<BoxCollider>();
            box.size = new Vector3(64f, .4f, 64f);
        }

        static GameObject AddPrefab(Transform parent, GameObject prefab, Vector3 position, Quaternion rotation, bool collider)
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null) return null;
            instance.transform.localPosition = position;
            instance.transform.localRotation = rotation;
            if (collider && instance.GetComponent<Collider>() == null)
            {
                var bounds = RenderBounds(instance);
                var box = instance.AddComponent<BoxCollider>();
                box.center = instance.transform.InverseTransformPoint(bounds.center);
                box.size = bounds.size;
            }
            return instance;
        }

        static Bounds RenderBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static GameObject SaveScenePrefab(GameObject source, string path)
        {
            return PrefabUtility.SaveAsPrefabAsset(source, path);
        }

        static GameObject SavePrefab(GameObject source, string path, bool addCollider)
        {
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null) throw new InvalidOperationException("Could not instantiate " + source.name);
            if (addCollider && instance.GetComponent<Collider>() == null)
            {
                var bounds = RenderBounds(instance);
                var box = instance.AddComponent<BoxCollider>();
                box.center = instance.transform.InverseTransformPoint(bounds.center);
                box.size = bounds.size;
            }
            var saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
            UnityEngine.Object.DestroyImmediate(instance);
            return saved;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Material FindMaterial(string name)
        {
            var path = DataPath + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = name, enableInstancing = true };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
