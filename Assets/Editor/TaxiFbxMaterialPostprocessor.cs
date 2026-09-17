using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Keep the playable build small and fast on PCVR: imported city/vehicle materials use a
// single built-in shader. Lighting is supplied by the scene and custom emissive lamps.
public sealed class TaxiFbxMaterialPostprocessor : AssetPostprocessor
{
    static readonly string[] Roots = { "/ThirdParty/DowntownCity/", "/ThirdParty/Vehicles/", "/ThirdParty/UniversalCharacters/" };
    void OnPostprocessMaterial(Material material)
    {
        if (!assetPath.EndsWith(".fbx") || !Roots.Any(root => assetPath.Replace('\\','/').Contains(root))) return;
        var shader = Shader.Find("Standard");
        if (shader == null) return;
        Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
        material.shader = shader;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.black);
    }
}
