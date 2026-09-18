using UnityEngine;

namespace TaxiVR.City
{
    /// <summary>Metadatos de un prefab de sector de ciudad. Permite a <see cref="TaxiVR.Playable.EndlessCity"/>
    /// aplicar variante, rotación y contenido variable (calles, semáforos, peatón) sin reconstruir jerarquías
    /// ni instanciar geometría por código.</summary>
    [DisallowMultipleComponent]
    public sealed class CitySectorTemplate : MonoBehaviour
    {
        [Tooltip("Identificador legible. No afecta a la selección determinista; sirve para inspeccionar el sector.")]
        public string TemplateId;
        [Tooltip("Rotaciones permitidas para esta plantilla (en pasos de 90 grados). Reduce la repetición visual.")]
        public bool[] AllowedRotations = { true, true, true, true };
        [Tooltip("Renderers que solo deben existir cerca del taxi (peatones, animaciones, detalles).")]
        public GameObject[] DetailGroup;
        [Tooltip("Renderers principales de carretera y acera; deben estar siempre activos.")]
        public Renderer[] PersistentRenderers;
        [Tooltip("Anclajes donde EndlessCity escribirá el nombre de calle visible.")]
        public Transform[] StreetLabels;
    }
}
