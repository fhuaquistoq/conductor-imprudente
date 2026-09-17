using System;
using System.Collections.Generic;
using UnityEngine;

namespace TaxiVR.Playable
{
    public enum CockpitKind { Wheel, Knob, Mirror, Loose, Button, Gear }
    public sealed class CockpitInteractable : MonoBehaviour
    {
        public static readonly List<CockpitInteractable> All = new();
        public CockpitKind Kind;
        public string Caption;
        public Transform Visual;
        public float Radius = .08f;
        public float Value;
        public float TravelLength = .09f;
        public float Travel;
        public Action Activated;
        public Action<float> Changed;
        public bool IsHeld => holders.Count > 0;
        public int HolderCount => holders.Count;
        readonly Dictionary<int, Hold> holders = new();
        readonly List<int> stale = new();
        Quaternion initialRotation;
        Vector3 homePosition;
        Vector3 visualHome;
        Transform homeParent;
        float lastPress = -10;
        float change;
        int samples;
        float lever;
        int requested = int.MinValue;
        Rigidbody body;
        struct Hold { public float Angle; public Quaternion Rotation; public Vector3 Offset; public Quaternion ObjectOffset; public float Time; public float Lever; }
        void OnEnable() { All.Add(this); }
        void Start() { Visual ??= transform; initialRotation = Visual.localRotation; visualHome = Visual.localPosition; homeParent = transform.parent; homePosition = transform.localPosition; body = GetComponent<Rigidbody>(); lever = Value >= 0 ? TravelLength * .5f : -TravelLength * .5f; requested = Mathf.RoundToInt(Value); }
        void OnDisable() { All.Remove(this); holders.Clear(); }
        public bool Near(Vector3 point)
        {
            var p = transform.InverseTransformPoint(point);
            if (Kind == CockpitKind.Wheel) return Mathf.Abs(p.z) < .12f && Mathf.Abs(new Vector2(p.x, p.y).magnitude - Radius) < .09f;
            return Vector3.Distance(point, transform.position) < Radius;
        }
        float Angle(Vector3 point) { var p = transform.InverseTransformPoint(point); return Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg; }
        float LeverPosition(Vector3 point) => transform.parent.InverseTransformPoint(point).z - homePosition.z;
        public bool Grab(int hand, Vector3 point, Quaternion rotation)
        {
            if (Kind == CockpitKind.Button) { Press(); return false; }
            if (Kind != CockpitKind.Wheel && holders.Count != 0) return false;
            if (holders.ContainsKey(hand)) return true;
            holders[hand] = new Hold { Angle = Angle(point), Rotation = rotation, Offset = Quaternion.Inverse(rotation) * (transform.position - point), ObjectOffset = Quaternion.Inverse(rotation) * (Kind == CockpitKind.Mirror ? Visual.rotation : transform.rotation), Time = Time.time, Lever = lever - LeverPosition(point) };
            if (body != null) { body.isKinematic = true; body.useGravity = false; }
            return true;
        }
        public void Move(int hand, Vector3 point, Quaternion rotation)
        {
            if (!holders.TryGetValue(hand, out var hold)) return;
            float angle = Angle(point);
            if (Kind == CockpitKind.Wheel) { change += -CityMath.WheelDelta(hold.Angle, angle); samples++; }
            else if (Kind == CockpitKind.Knob)
            {
                var delta = Quaternion.Inverse(hold.Rotation) * rotation;
                Value = Mathf.Clamp01(Value - Mathf.DeltaAngle(0, delta.eulerAngles.z) / 240); Changed?.Invoke(Value);
            }
            else if (Kind == CockpitKind.Mirror)
            {
                var desired = Quaternion.Inverse(Visual.parent.rotation) * rotation * hold.ObjectOffset;
                Visual.localRotation = Quaternion.RotateTowards(initialRotation, desired, 35);
            }
            else if (Kind == CockpitKind.Gear) MoveLever(hold.Lever + LeverPosition(point));
            else if (Kind == CockpitKind.Loose) { transform.SetPositionAndRotation(point + rotation * hold.Offset, rotation * hold.ObjectOffset); }
            hold.Angle = angle; hold.Rotation = rotation; hold.Time = Time.time; holders[hand] = hold;
        }
        public void Release(int hand)
        {
            holders.Remove(hand);
            if (holders.Count == 0 && body != null) { body.isKinematic = false; body.useGravity = true; body.linearVelocity = Vector3.zero; }
        }
        public void Press()
        {
            if (Time.time - lastPress < .45f) return;
            lastPress = Time.time; Activated?.Invoke();
            PlayableRoot.Instance?.Click();
        }
        public void DesktopAdjust(float delta)
        {
            if (Kind == CockpitKind.Wheel) Value = Mathf.Clamp(Value + delta, -240, 240);
            else if (Kind == CockpitKind.Knob) { Value = Mathf.Clamp01(Value + delta / 180); Changed?.Invoke(Value); }
            else if (Kind == CockpitKind.Mirror) Visual.localRotation = Quaternion.RotateTowards(initialRotation, Visual.localRotation * Quaternion.Euler(0, delta, 0), 35);
            else if (Kind == CockpitKind.Gear) MoveLever(lever + delta * TravelLength * .012f);
        }
        void MoveLever(float position)
        {
            float half = TravelLength * .5f;
            lever = Mathf.Clamp(position, -half, half);
            Travel = Mathf.InverseLerp(-half, half, lever);
            if (Travel > .62f) SetGear(1);
            else if (Travel < .38f) SetGear(-1);
        }
        void SetGear(int direction)
        {
            if (direction == requested) return;
            requested = direction;
            Changed?.Invoke(direction);
        }
        void LateUpdate()
        {
            if (samples > 0) { Value = Mathf.Clamp(Value + change / samples, -240, 240); change = 0; samples = 0; }
            if (Kind == CockpitKind.Wheel) Visual.localRotation = initialRotation * Quaternion.Euler(0, 0, -Value);
            if (Kind == CockpitKind.Knob) Visual.localRotation = initialRotation * Quaternion.Euler(0, 0, -Value * 240);
            if (Kind == CockpitKind.Gear)
            {
                float home = Value >= 0 ? TravelLength * .5f : -TravelLength * .5f;
                if (!IsHeld) lever = Mathf.MoveTowards(lever, home, TravelLength * 8f * Time.deltaTime);
                Visual.localRotation = initialRotation * Quaternion.Euler(lever / Mathf.Max(.0001f, TravelLength) * 18f, 0, 0);
            }
            if (Kind == CockpitKind.Button) Visual.localPosition = visualHome + Vector3.forward * (Time.time - lastPress < .16f ? .006f : 0);
            if (Kind == CockpitKind.Loose && !IsHeld && homeParent != null && (transform.position.y < -.5f || Vector3.Distance(transform.position, homeParent.position) > 5)) ReturnHome();
            stale.Clear();
            foreach (var pair in holders) if (Time.time - pair.Value.Time > .25f) stale.Add(pair.Key);
            foreach (int hand in stale) Release(hand);
        }
        public void ReturnHome()
        {
            holders.Clear(); transform.SetParent(homeParent); transform.localPosition = homePosition; transform.localRotation = initialRotation;
            if (body != null) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.isKinematic = true; body.useGravity = false; }
        }
    }
}
