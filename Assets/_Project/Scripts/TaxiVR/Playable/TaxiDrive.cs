using UnityEngine;
using UnityEngine.InputSystem;

namespace TaxiVR.Playable
{
    public sealed class TaxiDrive : MonoBehaviour
    {
        public CockpitInteractable Wheel;
        public PlayerHands Player;
        public FootReceiver Feet;

        public float TotalMass = 1250f;
        public float MaxSpeed = 19f;
        public float ReverseSpeed = 7f;
        public float MotorTorque = 1500f;
        public float BrakeTorque = 3200f;
        public float MaxSteer = 36f;
        public float MinSteer = 6f;
        public float SteerRate = 120f;
        public float Downforce = 42f;
        public float AntiRollFactor = .25f;
        public float MinimumDirectionChangeSpeed = 2.5f;

        /// <summary>Resistencia aerodinamica: media densidad por coeficiente por superficie de un turismo. Da
        /// una velocidad punta natural en lugar de un tope duro.</summary>
        public float DragCoefficient = .46f;

        /// <summary>Altura del centro de masas sobre el origen del coche. Es lo que hace que la carroceria se
        /// cargue en la curva y se hunda al frenar; a cero el coche giraria como un patin.</summary>
        public float CentreOfMassHeight = .42f;

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
        public int MotorWheelCount => motor.Length;
        public int GroundedWheels { get; private set; }
        public float SteerAngle => steerAngle;

        const float Track = .69f;
        const float FrontZ = 1.30f;
        const float RearZ = -1.14f;
        const float WheelRadius = .25f;
        const float SuspensionDistance = .22f;
        const float RestCompression = .5f;

        WheelCollider[] wheels;
        WheelCollider[] motor;
        WheelCollider[] steered;
        Transform[] visuals;
        bool[] centered;
        float steerAngle;
        float lastCollision;
        float rearGrip = 1f;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.mass = TotalMass;
            Body.linearDamping = .04f;
            // Amortiguacion angular baja: el coche tiene que poder cabecear al frenar y balancearse en la curva.
            // El tope de velocidad angular evita que un golpe lo mande a girar como una peonza.
            Body.angularDamping = .45f;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.centerOfMass = new Vector3(0, CentreOfMassHeight, .02f);
            Body.maxAngularVelocity = 6f;
            BuildWheels();
        }

        void BuildWheels()
        {
            float height = WheelRadius + SuspensionDistance * (1f - RestCompression);
            wheels = new WheelCollider[4];
            centered = new bool[4];
            motor = new[] { Create(2, new Vector3(Track, height, RearZ)), Create(3, new Vector3(-Track, height, RearZ)) };
            steered = new[] { Create(0, new Vector3(Track, height, FrontZ)), Create(1, new Vector3(-Track, height, FrontZ)) };
            wheels[0].ConfigureVehicleSubsteps(4f, 12, 18);
        }

        WheelCollider Create(int index, Vector3 position)
        {
            // Primero se emparenta al coche: un WheelCollider necesita un Rigidbody en su jerarquia
            // en el momento de anadirse, o Unity avisa de que no puede funcionar.
            var go = new GameObject("Rueda fisica " + index);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            var wheel = go.AddComponent<WheelCollider>();
            wheel.mass = 22;
            wheel.radius = WheelRadius;
            wheel.suspensionDistance = SuspensionDistance;
            wheel.forceAppPointDistance = .08f;
            var spring = wheel.suspensionSpring;
            // Muelle calculado para que el coche se asiente justo por debajo de su recorrido medio: si el muelle
            // empuja mas que el peso, el coche va sobre las puntas y pierde agarre en cuanto se apoya en una rueda.
            spring.spring = 26000;
            spring.damper = 2200;
            spring.targetPosition = RestCompression;
            wheel.suspensionSpring = spring;
            wheel.forwardFriction = Curve(1.55f, .55f);
            wheel.sidewaysFriction = Curve(1.5f, .48f);
            wheels[index] = wheel;
            return wheel;
        }

        static WheelFrictionCurve Curve(float stiffness, float extremumSlip)
        {
            return new WheelFrictionCurve
            {
                extremumSlip = extremumSlip,
                extremumValue = 1f,
                asymptoteSlip = extremumSlip * 2f,
                asymptoteValue = .55f,
                stiffness = stiffness
            };
        }

        public void BindVisualWheels(Transform cabin)
        {
            visuals = new Transform[4];
            visuals[0] = Pivot(0, Find(cabin, "Taxi_FrontLeftWheel"), "Pivote rueda frontal izq", false);
            visuals[1] = Pivot(1, Find(cabin, "Taxi_FrontRightWheel"), "Pivote rueda frontal der", false);
            visuals[2] = Pivot(2, Find(cabin, "Taxi_BackWheels"), "Pivote eje trasero", true);
            visuals[3] = visuals[2];
        }

        Transform Find(Transform cabin, string name)
        {
            foreach (var candidate in cabin.GetComponentsInChildren<Transform>(true)) if (candidate.name == name) return candidate;
            return null;
        }

        Transform Pivot(int index, Transform wheel, string label, bool keepCentered)
        {
            if (wheel == null) return null;
            var pivot = new GameObject(label).transform;
            pivot.SetParent(transform, false);
            pivot.position = wheel.position;
            pivot.rotation = wheel.rotation;
            wheel.SetParent(pivot, true);
            centered[index] = keepCentered;
            return pivot;
        }

        void Update()
        {
            var keys = Keyboard.current;
            if (keys?.qKey.wasPressedThisFrame == true) ChangeDirection(-1);
            if (keys?.eKey.wasPressedThisFrame == true) ChangeDirection(1);
            if (keys?.cKey.wasPressedThisFrame == true) Cruise = !Cruise;
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
                float target = (keys?.dKey.isPressed == true ? 1 : 0) - (keys?.aKey.isPressed == true ? 1 : 0);
                Wheel.Value = Mathf.MoveTowards(Wheel.Value, target * 180, 480 * Time.deltaTime);
            }
        }

        void FixedUpdate()
        {
            GroundedWheels = 0;
            foreach (var wheel in wheels) if (wheel.isGrounded) GroundedWheels++;

            float speed = Speed;
            float ratio = Mathf.Clamp01(Mathf.Abs(speed) / MaxSpeed);
            float target = Mathf.Clamp(Wheel == null ? 0 : Wheel.Value / 180f, -1f, 1f) * Mathf.Lerp(MaxSteer, MinSteer, ratio);
            steerAngle = Mathf.MoveTowards(steerAngle, target, SteerRate * Time.fixedDeltaTime);
            foreach (var wheel in steered) wheel.steerAngle = steerAngle;

            float brake = BrakeAnalog > .05f ? BrakeTorque * BrakeAnalog : 0;
            float torque = 0;
            if (BrakeAnalog < .5f && ThrottleAnalog > .05f)
            {
                float fade = 1f - Mathf.Clamp01((Direction > 0 ? speed : -speed) / (Direction > 0 ? MaxSpeed : ReverseSpeed));
                torque = MotorTorque * ThrottleAnalog * Direction * Mathf.Clamp01(fade) * (GroundedWheels > 1 ? 1f : .35f);
            }
            foreach (var wheel in wheels) wheel.brakeTorque = brake;
            foreach (var wheel in motor) wheel.motorTorque = torque;

            rearGrip = Mathf.MoveTowards(rearGrip, Skidding ? .55f : 1f, 2.5f * Time.fixedDeltaTime);
            var rear = Curve(1.5f * rearGrip, Skidding ? .62f : .48f);
            wheels[2].sidewaysFriction = rear;
            wheels[3].sidewaysFriction = rear;

            AntiRollBars();
            if (GroundedWheels > 0) Body.AddForce(-transform.up * Downforce * Mathf.Abs(speed));
            // La resistencia del aire crece con el cuadrado de la velocidad, asi que el coche encuentra su
            // velocidad punta solo, y a baja velocidad no estorba.
            var flat = new Vector3(Body.linearVelocity.x, 0, Body.linearVelocity.z);
            Body.AddForce(-flat * (DragCoefficient * flat.magnitude));
        }

        void AntiRollBars()
        {
            AntiRoll(wheels[0], wheels[1]);
            AntiRoll(wheels[2], wheels[3]);
        }

        // Fuerzas opuestas en los puntos de apoyo: reparte la carga entre el lado cargado y el descargado.
        void AntiRoll(WheelCollider left, WheelCollider right)
        {
            float difference = GroundForce(left) - GroundForce(right);
            float force = difference * AntiRollFactor;
            Body.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            Body.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }

        static float GroundForce(WheelCollider wheel)
        {
            if (!wheel.GetGroundHit(out var hit)) return 0f;
            return Mathf.Clamp(hit.force, 0f, 60000f);
        }

        void LateUpdate()
        {
            if (visuals == null) return;
            for (int i = 0; i < visuals.Length; i++) Follow(i);
        }

        void Follow(int index)
        {
            var pivot = visuals[index];
            if (pivot == null || wheels[index] == null) return;
            wheels[index].GetWorldPose(out var position, out var rotation);
            var localPosition = transform.InverseTransformPoint(position);
            if (centered[index]) localPosition.x = 0;
            pivot.localPosition = localPosition;
            pivot.localRotation = Quaternion.Inverse(transform.rotation) * rotation;
        }

        public bool ChangeDirection(int direction)
        {
            // Ir a punto muerto siempre es seguro; engranar una marcha exige ir casi parado.
            if (direction != 0 && Mathf.Abs(Speed) > MinimumDirectionChangeSpeed) return false;
            Direction = direction;
            Cruise = false;
            return true;
        }

        /// <summary>Devuelve el taxi a la calzada mas cercana. Elige el primer tramo del cruce que la ciudad haya
        /// dibujado, porque en una manzana rectangular el lado que falta es justo el que no se puede pisar, y
        /// coloca el coche en el carril que le toca por el lado de circulacion.</summary>
        public void ResetToRoad()
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.Sleep();
            Cruise = false;
            Direction = 1;

            var node = new Vector2Int(Mathf.RoundToInt(Body.position.x / CityMath.Block), Mathf.RoundToInt(Body.position.z / CityMath.Block));
            Vector2Int[] steps = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            var forward = Vector3.forward;
            foreach (var step in steps)
            {
                if (CityGrid.IsBlocked(node, node + step)) continue;
                forward = new Vector3(step.x, 0, step.y);
                break;
            }
            var lane = new Vector3(forward.z, 0, -forward.x) * 3f;
            Body.position = new Vector3(node.x * CityMath.Block, .1f, node.y * CityMath.Block) + forward * 20f + lane;
            Body.rotation = Quaternion.LookRotation(forward, Vector3.up);
            Body.WakeUp();
            steerAngle = 0;
            ThrottleAnalog = BrakeAnalog = 0;
            if (Wheel != null) Wheel.Value = 0;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.relativeVelocity.magnitude < 1.5f || Time.time - lastCollision < 1) return;
            lastCollision = Time.time;
            Collisions++;
            // El impacto no se amortigua a mano: el motor de fisicas ya reparte el momento entre los dos cuerpos,
            // y recortar la velocidad aqui era lo que hacia que chocar se sintiera como frenar en seco.
            // Lo unico que se toca es el control, porque el crucero no tiene sentido despues de un golpe.
            Cruise = false;
            Player?.Pulse(true); Player?.Pulse(false);
        }
    }
}
