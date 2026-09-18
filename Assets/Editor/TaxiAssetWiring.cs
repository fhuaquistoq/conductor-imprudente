using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaxiVR.Playable;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TaxiVR.Bootstrap.Editor
{
    /// <summary>Rellena el catalogo de arte con los modelos y materiales de la ciudad. Vive aparte del
    /// constructor de escena para poder reenlazar el arte sin regenerar la escena de produccion.</summary>
    public static class TaxiAssetWiring
    {
        const string CatalogPath = "Assets/_Project/PlayableData/CityAssets.asset";
        const string CharacterModels = "Assets/_Project/Art/Characters/Models/";
        const string VehicleModels = "Assets/_Project/Art/Vehicles/Models/";
        const string BuildingModels = "Assets/_Project/Art/Buildings/Models/";
        const string TreeModels = "Assets/_Project/Art/Trees/Models/";
        const string ProduceModels = "Assets/_Project/Art/Props/Vegetables/";
        const string TexturePath = "Assets/_Project/Art/Buildings/Textures/";
        const string ModulePath = "Assets/_Project/City/Prefabs/Modules/";
        const string WalkControllerPath = "Assets/_Project/PlayableData/PeatonAndar.controller";
        const string WalkClipName = "Armature|Walk_Carry_Loop";
        const string AnimationLibrary = "Assets/_Project/Art/Animations/Animations/UAL2_Standard.fbx";

        [MenuItem("TaxiVR/City/Wire generated model slots")]
        public static void Wire()
        {
            var assets = AssetDatabase.LoadAssetAtPath<CityAssets>(CatalogPath);
            if (assets == null) throw new InvalidOperationException("Falta el catalogo en " + CatalogPath);

            assets.PoliceCar = Import(VehicleModels + "Cop.fbx");
            assets.PassengerModel = Import(CharacterModels + "Superhero_Male_FullBody.fbx");
            // Ultimo recurso del trafico civil si la lista de cuerpos no llegara a cargarse.
            assets.PedestrianModel = assets.PassengerModel;

            // Los peatones salen del mismo esqueleto con cuerpos, pelos y alturas distintas: seis cuerpos por
            // siete peinados da cuarenta y dos siluetas reconocibles con la geometria que ya traia el proyecto.
            assets.PedestrianBodies = new[]
            {
                "Superhero_Male_FullBody.fbx", "Superhero_Female_FullBody.fbx",
                "Outfits/Male_Peasant.fbx", "Outfits/Female_Peasant.fbx",
                "Outfits/Male_Ranger.fbx", "Outfits/Female_Ranger.fbx"
            }.Select(name => Import(CharacterModels + name)).ToArray();

            assets.PedestrianHair = Directory.GetFiles(CharacterModels + "Hairstyles", "*.fbx")
                .Select(path => Import(path.Replace('\\', '/'))).ToArray();

            Humanoid(AnimationLibrary);
            foreach (var body in assets.PedestrianBodies) Humanoid(AssetDatabase.GetAssetPath(body));
            assets.PedestrianWalk = WalkController();

            // Edificios y arboles: el kit solo trae tres edificios cerrados y una docena larga de arboles, asi
            // que la variedad de la calle sale de los arboles y del utillaje, no de repetir casas.
            assets.Buildings = new[] { "Building_Small_1", "Building_Medium_2_001", "Building_Large_2" }
                .Select(name => Import(BuildingModels + name + ".fbx")).ToArray();
            assets.Trees = Directory.GetFiles(TreeModels, "*.fbx")
                .Where(path => !Path.GetFileName(path).StartsWith("Dead", StringComparison.Ordinal))
                .OrderBy(path => path).Select(path => Import(path.Replace('\\', '/'))).ToArray();

            // Decoracion dura: piezas del kit a su tamano real, que es como encajan con la acera de 4 m.
            assets.HardProps = new[] { "Prop_Bollard", "Prop_Planter_Single", "Sidewalk_Planter", "Prop_ACUnit" }
                .Select(name => Load(ModulePath + name + ".prefab")).ToArray();
            // Decoracion blanda: piezas suelta que el taxi puede tirar.
            assets.SoftProps = new[] { "Prop_ManholeCover", "Prop_Drain", "Sidewalk_Planter" }
                .Select(name => Load(ModulePath + name + ".prefab")).ToArray();
            assets.MarketProps = new[] { "Apple", "Banana", "Tomato", "Bread", "Orange", "Pumpkin" }
                .Select(name => Import(ProduceModels + name + ".fbx")).ToArray();

            Texture(assets.Asphalt, TexturePath + "T_Concrete_Asphalt_BaseColor.png");
            Texture(assets.Pavement, TexturePath + "T_Concrete_BaseColor.png");

            if (assets.Catalog != null)
            {
                assets.Catalog.LoadRadius = 3;
                assets.Catalog.DetailRadius = 1;
                EditorUtility.SetDirty(assets.Catalog);
            }

            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
            Debug.Log($"Catalogo enlazado: {assets.Buildings.Length} edificios, {assets.Trees.Length} arboles, " +
                      $"{assets.HardProps.Length} piezas duras, {assets.SoftProps.Length} blandas, " +
                      $"{assets.PedestrianBodies.Length} cuerpos de peaton, {assets.PedestrianHair.Length} peinados, " +
                      $"andar {(assets.PedestrianWalk == null ? "ausente" : "listo")}.");
        }

        /// <summary>Controlador del ciclo de caminata. Se genera desde el clip de la libreria universal, que es
        /// la que trae el proyecto; si el clip no esta, los peatones vuelven a la marcha procedural.</summary>
        static RuntimeAnimatorController WalkController()
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(AnimationLibrary).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == WalkClipName);
            if (clip == null) return null;
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(WalkControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(WalkControllerPath);
                var machine = controller.layers[0].stateMachine;
                var state = machine.AddState("Andar");
                state.motion = clip;
                machine.defaultState = state;
            }
            else if (controller.layers.Length > 0 && controller.layers[0].stateMachine.states.Length > 0)
                controller.layers[0].stateMachine.states[0].state.motion = clip;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>Marca un modelo como humanoide. Sin esto la libreria de animaciones no puede retargetar su
        /// ciclo de caminata sobre los cuerpos del proyecto.</summary>
        static void Humanoid(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || importer.animationType == ModelImporterAnimationType.Human) return;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
        }

        static void Texture(Material material, string path)
        {
            if (material == null) return;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) return;
            material.mainTexture = texture;
            material.mainTextureScale = Vector2.one;
            // El color base pasa a blanco porque el matiz lo pone la textura; dejarlo oscuro la apagaria.
            material.color = Color.white;
            EditorUtility.SetDirty(material);
        }

        static Material Material(string name, Color color, Texture2D texture)
        {
            var path = "Assets/_Project/PlayableData/" + name + ".mat";
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(lit); AssetDatabase.CreateAsset(material, path); }
            material.color = color;
            if (texture != null) material.mainTexture = texture;
            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject Load(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new InvalidOperationException("Falta el recurso " + path);
            return asset;
        }

        static GameObject Import(string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new InvalidOperationException("Falta el modelo " + path);
            return model;
        }
    }
}
