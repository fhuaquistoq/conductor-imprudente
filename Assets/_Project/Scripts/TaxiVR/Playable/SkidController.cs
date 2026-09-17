using UnityEngine;

namespace TaxiVR.Playable
{
    public sealed class SkidController : MonoBehaviour
    {
        public TaxiDrive Drive;
        public Material SmokeMaterial;
        public Transform RearLeft;
        public Transform RearRight;
        public float MinimumSpeed = .6f;
        public bool Active { get; private set; }
        public float Intensity { get; private set; }

        ParticleSystem smoke;
        ParticleSystem.EmissionModule emission;
        AudioSource squeal;

        void Start()
        {
            var effect = new GameObject("Humo de derrape");
            effect.transform.SetParent(Drive.transform, false);
            smoke = effect.AddComponent<ParticleSystem>();
            var main = smoke.main;
            main.startLifetime = 1.3f; main.startSpeed = .7f; main.startSize = .62f;
            main.startColor = new Color(.82f, .83f, .85f, .34f);
            main.maxParticles = 140; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -.02f;
            var shape = smoke.shape;
            shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 24; shape.radius = .2f;
            emission = smoke.emission;
            emission.rateOverTime = 0;
            if (SmokeMaterial != null) effect.GetComponent<ParticleSystemRenderer>().sharedMaterial = SmokeMaterial;
            smoke.Stop();
            squeal = new GameObject("Chirrido de neumaticos").AddComponent<AudioSource>();
            squeal.transform.SetParent(Drive.transform, false);
            squeal.spatialBlend = 0; squeal.loop = true; squeal.clip = Squeal(); squeal.volume = 0;
            squeal.Play();
        }

        void Update()
        {
            bool wanted = FootPedals.Skid(Drive.Throttle > .5f, Drive.Brake > .5f) && Mathf.Abs(Drive.Speed) > MinimumSpeed && !Drive.Paused;
            Active = wanted;
            Intensity = Mathf.MoveTowards(Intensity, wanted ? 1 : 0, (wanted ? 4 : 6) * Time.deltaTime);
            emission.rateOverTime = Intensity * 26;
            var shape = smoke.shape;
            shape.position = RearLeft == null || RearRight == null ? Vector3.zero : (RearLeft.localPosition + RearRight.localPosition) * .5f;
            if (Intensity > .01f && !smoke.isPlaying) smoke.Play();
            else if (Intensity <= .01f && smoke.isPlaying) smoke.Stop();
            squeal.volume = Intensity * .22f;
            squeal.pitch = .92f + Intensity * .16f;
        }

        static AudioClip Squeal()
        {
            const int rate = 22050;
            int count = (int)(rate * 1.2f);
            var data = new float[count];
            var random = new System.Random(7);
            float low = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                low += ((float)(random.NextDouble() * 2 - 1) - low) * .35f;
                float tone = Mathf.Sin(t * 1180 * Mathf.PI * 2) * .35f + Mathf.Sin(t * 1770 * Mathf.PI * 2) * .18f;
                float envelope = .55f + .45f * Mathf.Sin(t * 7.5f * Mathf.PI * 2);
                data[i] = (tone + low * .55f) * envelope * .3f;
            }
            var clip = AudioClip.Create("Chirrido", count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
