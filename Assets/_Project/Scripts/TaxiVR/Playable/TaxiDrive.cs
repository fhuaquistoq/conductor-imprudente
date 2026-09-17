using UnityEngine;
using UnityEngine.InputSystem;

namespace TaxiVR.Playable
{
    public sealed class TaxiDrive : MonoBehaviour
    {
        public CockpitInteractable Wheel;
        public PlayerHands Player;
        public FootReceiver Feet;
        public Rigidbody Body { get; private set; }
        public float Speed => Vector3.Dot(Body.linearVelocity, transform.forward);
        public int Direction = 1;
        public bool Cruise;
        public bool Paused;
        public float Throttle, Brake;
        public float ThrottleAnalog { get; private set; }
        public float BrakeAnalog { get; private set; }
        public bool Skidding { get; private set; }
        public int Collisions { get; private set; }
        public float TestThrottle = -1;
        public bool FootTracking => Feet != null && Feet.SocketBound && Feet.TrackingReceived;
        float lastCollision;
        void Awake() { Body = GetComponent<Rigidbody>(); }
        void Update()
        {
            var keys = Keyboard.current;
            if (keys?.qKey.wasPressedThisFrame == true) ChangeDirection(-1);
            if (keys?.eKey.wasPressedThisFrame == true) ChangeDirection(1);
            if (keys?.cKey.wasPressedThisFrame == true) Cruise = !Cruise;
            if (keys?.escapeKey.wasPressedThisFrame == true) { Paused = !Paused; Cruise = false; }
            if (keys?.rKey.wasPressedThisFrame == true) ResetToRoad();
            var manual = keys?.wKey.isPressed == true ? 1f : 0f;
            var manualBrake = keys?.sKey.isPressed == true || keys?.spaceKey.isPressed == true ? 1f : 0f;
            if (Player != null)
            {
                manual = Mathf.Max(manual, Player.RightTrigger);
                manualBrake = Mathf.Max(manualBrake, Player.LeftTrigger);
            }
            float feetThrottle = FootTracking && Feet.Machine.Throttle ? 1f : 0f;
            float feetBrake = FootTracking && Feet.Machine.Brake ? 1f : 0f;
            Throttle = TestThrottle >= 0 ? TestThrottle : Mathf.Max(manual, feetThrottle);
            Brake = Mathf.Max(manualBrake, feetBrake);
            Skidding = FootPedals.Skid(Throttle > .5f, Brake > .5f);
            if (Cruise && Mathf.Abs(Speed) < 6) Throttle = Mathf.Max(Throttle, .5f);
            if (Brake > .1f) Cruise = false;
            if (Paused || Player != null && Player.HadHeadTracking && !Player.HeadTracked) { Throttle = 0; Brake = 1; Cruise = false; Skidding = false; }
            ThrottleAnalog = FootPedals.Approach(ThrottleAnalog, Throttle, FootPedals.RampSeconds, Time.deltaTime);
            BrakeAnalog = FootPedals.Approach(BrakeAnalog, Brake, FootPedals.RampSeconds, Time.deltaTime);
            if (Wheel != null && !Wheel.IsHeld)
            {
                float steer = (keys?.dKey.isPressed == true ? 1 : 0) - (keys?.aKey.isPressed == true ? 1 : 0);
                Wheel.Value = Mathf.MoveTowards(Wheel.Value, steer * 180, 220 * Time.deltaTime);
            }
        }
        void FixedUpdate()
        {
            float speed = Speed;
            speed = Mathf.MoveTowards(speed, 0, (BrakeAnalog * 12 + .22f) * Time.fixedDeltaTime);
            if (BrakeAnalog < .1f) speed += ThrottleAnalog * Direction * 3.5f * Time.fixedDeltaTime;
            speed = Mathf.Clamp(speed, -5, 16);
            if (Skidding) speed = Mathf.MoveTowards(speed, 0, .9f * Time.fixedDeltaTime);
            float steering = Wheel == null ? 0 : Mathf.Clamp(Wheel.Value / 180, -1, 1);
            float authority = Skidding ? .62f : 1f;
            float yaw = Mathf.Tan(steering * 30 * Mathf.Deg2Rad) * speed / 2.6f * Mathf.Rad2Deg * Time.fixedDeltaTime * authority;
            Body.MoveRotation(Body.rotation * Quaternion.Euler(0, yaw, 0));
            float drift = Skidding ? Mathf.Sin(Time.time * 9f) * .85f * Mathf.Clamp01(Mathf.Abs(speed) / 4f) : 0;
            Body.linearVelocity = Body.rotation * new Vector3(drift, 0, speed);
            Body.angularVelocity = Vector3.zero;
        }
        public void ChangeDirection(int direction) { if (Mathf.Abs(Speed) < .8f) { Direction = direction; Cruise = false; } }
        public void ResetToRoad()
        {
            Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; Cruise = false;
            Body.position = new Vector3(Mathf.Round(Body.position.x / 64) * 64 + 3, 0, Mathf.Round(Body.position.z / 64) * 64 + 22);
            Body.rotation = Quaternion.identity;
        }
        void OnCollisionEnter(Collision collision)
        {
            if (collision.relativeVelocity.magnitude < 1.5f || Time.time - lastCollision < 1) return;
            lastCollision = Time.time; Collisions++; Cruise = false;
            Body.linearVelocity *= .15f;
            Player?.Pulse(true); Player?.Pulse(false);
        }
    }
}
