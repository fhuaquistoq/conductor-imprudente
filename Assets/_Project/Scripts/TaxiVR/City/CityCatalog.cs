using UnityEngine;

namespace TaxiVR.City
{
    /// <summary>Catálogo editable de la ciudad. Guarda las plantillas de sector, materiales compartidos y
    /// presupuestos de streaming consumidos por <see cref="TaxiVR.Playable.EndlessCity"/>. Vive como asset de
    /// proyecto para que un diseñador modifique la composición sin tocar código.</summary>
    [CreateAssetMenu(menuName = "TaxiVR/City/Catalog", fileName = "CityCatalog")]
    public sealed class CityCatalog : ScriptableObject
    {
        [Header("Streaming")]
        [Tooltip("Radio de sectores cargados alrededor del taxi (sectores de 64 m).")]
        [Min(1)] public int LoadRadius = 3;
        [Tooltip("Radio de sectores con detalle cercano (peatones y señalización activos).")]
        [Min(0)] public int DetailRadius = 1;

        [Header("Plantillas de sector")]
        [Tooltip("Prefabs que representan un sector de 64 m. EndlessCity los elige por hash determinista.")]
        public GameObject[] SectorTemplates;

        [Header("Edificios cerrados")]
        [Tooltip("Prefabs de edificio cerrado usados por las plantillas. Solo referencia; no se instancian sueltos.")]
        public GameObject[] BuildingPrefabs;

        [Header("Materiales compartidos")]
        public Material Asphalt;
        public Material Pavement;
        public Material Grass;
        public Material FacadeDefault;
        public Material Lamp;
    }
}
