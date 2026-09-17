using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using InputDevice = UnityEngine.XR.InputDevice;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace TaxiVR.Playable
{
    public sealed class PlayerHands : MonoBehaviour
    {
        public Camera View;
        public CityAssets Assets;
        public bool Desktop;
        public bool HeadTracked { get; private set; }
        public bool HadHeadTracking { get; private set; }
        public float LeftTrigger { get; private set; }
        public float RightTrigger { get; private set; }
        public string TrackingStatus { get; private set; } = "TECLADO + RATON";
        public string HoverCaption { get; private set; }
        public Vector3 SeatEye = new(-.36f, 1.06f, -.28f);
        readonly List<XRHandSubsystem> subsystems = new();
        readonly HandState[] hands = { new(), new() };
        XRHandSubsystem subsystem;
        Vector3 headReference;
        Quaternion yawCorrection = Quaternion.identity;
        bool calibrated;
        float yaw, pitch;
        CockpitInteractable desktopHeld;
        float desktopDistance = .5f;
        class HandState
        {
            public Transform Palm;
            public Transform[] Fingers = new Transform[5];
            public Transform[] Joints = new Transform[26];
            public Transform[] Bones = new Transform[26];
            public CockpitInteractable Held;
            public CockpitInteractable Touching;
            public bool WasGrip;
            public bool Tracked;
        }
        void Start()
        {
            for (int side = 0; side < 2; side++)
            {
                var hand = hands[side];
                hand.Palm = new GameObject(side == 0 ? "Mano izquierda" : "Mano derecha").transform; hand.Palm.SetParent(transform, false);
                Shape.Part("Palm", hand.Palm, Vector3.zero, new Vector3(.078f, .026f, .09f), Assets.Skin, PrimitiveType.Cube);
                for (int i = 0; i < 5; i++)
                {
                    hand.Fingers[i] = Shape.Part("Finger", hand.Palm, new Vector3((i - 2) * .018f, 0, .06f), new Vector3(.014f, i == 0 ? .02f : .034f, .014f), Assets.Skin, PrimitiveType.Capsule).transform;
                    hand.Fingers[i].localRotation = Quaternion.Euler(90, 0, 0);
                }
                for (int i = 0; i < 26; i++)
                {
                    hand.Joints[i] = Shape.Part("Tracked joint", transform, Vector3.zero, Vector3.one * .015f, Assets.Skin, PrimitiveType.Sphere).transform;
                    hand.Joints[i].gameObject.SetActive(false);
                    hand.Bones[i] = Shape.Part("Tracked phalanx", transform, Vector3.zero, Vector3.one, Assets.Skin, PrimitiveType.Capsule).transform;
                    hand.Bones[i].gameObject.SetActive(false);
                }
                hand.Palm.gameObject.SetActive(false);
            }
        }
        void OnEnable() { Application.onBeforeRender += TrackHead; }
        void OnDisable()
        {
            Application.onBeforeRender -= TrackHead;
            for (int i = 0; i < 2; i++) Release(i);
            desktopHeld?.Release(2); desktopHeld = null;
            LeftTrigger = RightTrigger = 0;
        }
        void OnApplicationFocus(bool focus)
        {
            if (!focus) { for (int i = 0; i < 2; i++) Release(i); desktopHeld?.Release(2); desktopHeld = null; LeftTrigger = RightTrigger = 0; }
        }
        void Update()
        {
            if (Keyboard.current?.hKey.wasPressedThisFrame == true) calibrated = false;
            if (Desktop) { DesktopInput(); return; }
            TrackHead();
            if (subsystem == null || !subsystem.running)
            {
                SubsystemManager.GetSubsystems(subsystems);
                subsystem = subsystems.Find(s => s.running);
            }
            bool realHands = false;
            for (int i = 0; i < 2; i++) realHands |= UpdateHand(i);
            TrackingStatus = realHands ? "TRACKING DE MANOS" : hands[0].Tracked || hands[1].Tracked ? "MANDOS TOUCH" : "SIN TRACKING DE MANOS / MANDOS";
        }
        void TrackHead()
        {
            if (Desktop || View == null) return;
            var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(XRNode.Head);
            HeadTracked = head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
            if (!HeadTracked) return;
            HadHeadTracking = true;
            head.TryGetFeatureValue(CommonUsages.devicePosition, out var position);
            head.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation);
            if (!calibrated) { headReference = position; yawCorrection = Quaternion.Euler(0, -rotation.eulerAngles.y, 0); calibrated = true; }
            View.transform.localPosition = TrackingPosition(position);
            View.transform.localRotation = yawCorrection * rotation;
        }
        Vector3 TrackingPosition(Vector3 position) => SeatEye + yawCorrection * (position - headReference);
        bool UpdateHand(int index)
        {
            var state = hands[index]; var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(index == 0 ? XRNode.LeftHand : XRNode.RightHand);
            var xrhand = subsystem == null ? default : index == 0 ? subsystem.leftHand : subsystem.rightHand;
            bool real = subsystem != null && xrhand.isTracked;
            bool controller = device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
            state.Tracked = real || controller;
            state.Palm.gameObject.SetActive(controller && !real);
            for (int j = 0; j < state.Joints.Length; j++) { state.Joints[j].gameObject.SetActive(real); state.Bones[j].gameObject.SetActive(real); }
            if (!state.Tracked || !HeadTracked) { Release(index); if (index == 0) LeftTrigger = 0; else RightTrigger = 0; return false; }
            Vector3 point, tip; Quaternion rotation; float grip, trigger = 0;
            if (real)
            {
                if (!xrhand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm) || !xrhand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var finger) || !xrhand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb))
                { Release(index); return false; }
                point = transform.TransformPoint(TrackingPosition((finger.position + thumb.position) * .5f)); rotation = transform.rotation * yawCorrection * palm.rotation;
                tip = transform.TransformPoint(TrackingPosition(finger.position));
                float pinch = Vector3.Distance(finger.position, thumb.position);
                grip = pinch < (state.WasGrip ? .045f : .027f) ? 1 : 0;
                for (int j = 0; j < state.Joints.Length; j++)
                {
                    if (xrhand.GetJoint(XRHandJointIDUtility.FromIndex(j)).TryGetPose(out var joint)) state.Joints[j].position = transform.TransformPoint(TrackingPosition(joint.position));
                    else state.Joints[j].gameObject.SetActive(false);
                }
                for (int j = 0; j < state.Bones.Length; j++)
                {
                    int parent = j <= 2 || j == 6 || j == 11 || j == 16 || j == 21 ? 0 : j - 1;
                    if (j == 0 || !state.Joints[j].gameObject.activeSelf || !state.Joints[parent].gameObject.activeSelf) { state.Bones[j].gameObject.SetActive(false); continue; }
                    var from = state.Joints[parent].position; var to = state.Joints[j].position;
                    state.Bones[j].position = (from + to) * .5f; state.Bones[j].rotation = Quaternion.FromToRotation(Vector3.up, to - from);
                    state.Bones[j].localScale = new Vector3(.014f, Vector3.Distance(from, to) * .5f, .014f);
                }
            }
            else
            {
                device.TryGetFeatureValue(CommonUsages.devicePosition, out var position); device.TryGetFeatureValue(CommonUsages.deviceRotation, out var q);
                device.TryGetFeatureValue(CommonUsages.grip, out grip); device.TryGetFeatureValue(CommonUsages.trigger, out trigger);
                point = transform.TransformPoint(TrackingPosition(position)); rotation = transform.rotation * yawCorrection * q;
                state.Palm.SetPositionAndRotation(point, rotation);
                for (int j = 0; j < 5; j++) state.Fingers[j].localRotation = Quaternion.Euler(90 + (j == 1 ? trigger : grip) * 75, 0, 0);
                tip = point + rotation * new Vector3(0, 0, .11f);
            }
            if (index == 0) LeftTrigger = trigger; else RightTrigger = trigger;
            bool gripping = grip > .65f;
            var touch = Closest(tip, true);
            if (touch != null && touch != state.Touching) { touch.Press(); Pulse(index == 0); }
            state.Touching = touch;
            if (gripping && !state.WasGrip)
            {
                var target = Closest(point, false);
                if (target != null && target.Grab(index, point, rotation)) { state.Held = target; Pulse(index == 0); }
            }
            if (gripping && state.Held != null) state.Held.Move(index, point, rotation);
            else if (!gripping) Release(index);
            state.WasGrip = gripping;
            return real;
        }
        CockpitInteractable Closest(Vector3 point, bool buttons)
        {
            CockpitInteractable closest = null; float distance = float.MaxValue;
            foreach (var target in CockpitInteractable.All)
            {
                if ((target.Kind == CockpitKind.Button) != buttons || !target.Near(point)) continue;
                float d = Vector3.SqrMagnitude(point - target.transform.position);
                if (d < distance) { closest = target; distance = d; }
            }
            return closest;
        }
        void Release(int index)
        {
            hands[index].Held?.Release(index); hands[index].Held = null; hands[index].WasGrip = false;
        }
        public void Pulse(bool left)
        {
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);
            if (device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse) device.SendHapticImpulse(0, .18f, .045f);
        }
        void DesktopInput()
        {
            TrackingStatus = "TECLADO + RATON";
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue(); yaw += delta.x * .12f; pitch = Mathf.Clamp(pitch - delta.y * .12f, -65, 70);
            }
            if (Keyboard.current?.hKey.wasPressedThisFrame == true) { yaw = 0; pitch = 0; }
            View.transform.localPosition = SeatEye; View.transform.localRotation = Quaternion.Euler(pitch, yaw, 0);
            Ray ray = View.ScreenPointToRay(mouse.position.ReadValue());
            CockpitInteractable hover = null;
            if (Physics.Raycast(ray, out var hit, 1.5f, 1 << 8, QueryTriggerInteraction.Collide)) hover = hit.collider.GetComponentInParent<CockpitInteractable>();
            HoverCaption = hover == null ? string.Empty : hover.Caption;
            if (mouse.leftButton.wasPressedThisFrame && hover != null)
            {
                desktopDistance = Mathf.Clamp(Vector3.Distance(View.transform.position, hit.point), .25f, .8f);
                if (hover.Kind == CockpitKind.Button) hover.Press();
                else if (hover.Grab(2, ray.GetPoint(desktopDistance), View.transform.rotation)) desktopHeld = hover;
            }
            if (desktopHeld != null)
            {
                if (!mouse.leftButton.isPressed) { desktopHeld.Release(2); desktopHeld = null; }
                else
                {
                    desktopDistance = Mathf.Clamp(desktopDistance + mouse.scroll.ReadValue().y * .0003f, .22f, .9f);
                    desktopHeld.Move(2, ray.GetPoint(desktopDistance), View.transform.rotation);
                    if (desktopHeld.Kind != CockpitKind.Loose) desktopHeld.DesktopAdjust(mouse.delta.ReadValue().x * .55f + mouse.scroll.ReadValue().y * .025f);
                }
            }
        }
    }
}
