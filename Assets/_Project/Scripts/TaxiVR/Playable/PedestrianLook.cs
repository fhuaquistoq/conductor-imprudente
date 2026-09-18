using System.Collections.Generic;
using UnityEngine;

namespace TaxiVR.Playable
{
    /// <summary>Personalizacion de peatones: injerta un peinado sobre el esqueleto de un cuerpo ya montado y le
    /// engancha el ciclo de caminata.
    ///
    /// Ambas cosas se apoyan en que los cuerpos, los peinados y la libreria de animaciones del proyecto
    /// comparten esqueleto: la malla del pelo se reengancha hueso a hueso por nombre, y el ciclo de andar se
    /// retargeta por ser humanoide. Cuando algo de eso no esta, el peaton se queda con la marcha procedural en
    /// lugar de quedarse en pose de fabrica.</summary>
    public static class PedestrianLook
    {
        /// <summary>Velocidad a la que el ciclo de andar va a velocidad natural. Por encima, el animador acelera
        /// la zancada para que los pies no patinen.</summary>
        public const float WalkReferenceSpeed = 1.35f;

        /// <summary>Engancha la malla de un peinado a los huesos del cuerpo. Devuelve null si los esqueletos no
        /// coinciden: es preferible un peaton sin pelo a uno con la melena flotando en el sitio equivocado.</summary>
        public static GameObject Attach(GameObject body, GameObject hairPrefab)
        {
            if (body == null || hairPrefab == null) return null;
            var probe = Object.Instantiate(hairPrefab);
            var source = probe.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (source == null) { Object.Destroy(probe); return null; }

            var index = new Dictionary<string, Transform>();
            foreach (var node in body.GetComponentsInChildren<Transform>(true))
                if (!index.ContainsKey(node.name)) index[node.name] = node;

            var bones = source.bones;
            var mapped = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                if (!index.TryGetValue(bones[i].name, out mapped[i])) { Object.Destroy(probe); return null; }
            }
            Transform root = null;
            if (source.rootBone != null) index.TryGetValue(source.rootBone.name, out root);
            if (root == null && mapped.Length > 0) root = mapped[0];

            // Solo viaja la malla: el esqueleto propio del peinado se descarta y sus huesos pasan a ser los del
            // cuerpo, que es lo que hace que el pelo siga a la cabeza al andar.
            var renderer = source.transform;
            renderer.SetParent(body.transform, false);
            renderer.localPosition = Vector3.zero;
            renderer.localRotation = Quaternion.identity;
            renderer.localScale = Vector3.one;
            source.bones = mapped;
            source.rootBone = root;
            source.updateWhenOffscreen = false;
            Object.Destroy(probe);
            return renderer.gameObject;
        }

        /// <summary>Animador del ciclo de caminata. Devuelve null si el cuerpo no trae avatar humanoide valido o
        /// si el proyecto no tiene el controlador, y entonces el peaton conserva la marcha procedural.</summary>
        public static PedestrianWalker Walk(GameObject body, RuntimeAnimatorController controller, float phase)
        {
            if (body == null || controller == null) return null;
            var animator = body.GetComponent<Animator>();
            bool added = animator == null;
            if (added) animator = body.AddComponent<Animator>();
            if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            {
                if (added) Object.Destroy(animator);
                return null;
            }
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            // El desfase inicial se fija una sola vez: repetirlo cada fotograma congelaria la zancada.
            animator.Play(0, 0, Mathf.Repeat(phase * .31f, 1f));
            return new PedestrianWalker(animator);
        }
    }

    /// <summary>Ciclo de caminata de un peaton. Solo ajusta la velocidad del animador a la velocidad real, para
    /// que la zancada acompane al paso en lugar de patinar.</summary>
    public sealed class PedestrianWalker
    {
        readonly Animator animator;
        public bool Ready => animator != null && animator.isActiveAndEnabled;

        public PedestrianWalker(Animator animator) { this.animator = animator; }

        public void Pose(float metresPerSecond)
        {
            if (animator == null) return;
            animator.speed = Mathf.Clamp(metresPerSecond / PedestrianLook.WalkReferenceSpeed, .35f, 2.2f);
        }
    }
}
