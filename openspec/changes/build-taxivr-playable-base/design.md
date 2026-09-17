# TaxiVR playable base — technical design (Gates 0–3)

## Decision summary

Build one Windows PCVR main scene around a project-owned composition root. Keep pure protocol, foot-state, input arbitration, and vehicle-command logic independent of Unity; Unity adapters translate those contracts to the verified Meta XR Interaction SDK/OpenXR and Input System APIs, UDP, and Rigidbody physics. Do not introduce Unity XR Interaction Toolkit as a dependency. Gates are additive and independently disableable: bootstrap, cockpit, tracker boundary, then driving.

This design intentionally does not implement passengers, product GPS/route progression, requests, faults, scoring, results, or production content.

## Scope and assembly boundaries

Use the existing `Assets/_Project` convention as the physical project boundary. The requested `_TaxiVR` shape is represented by the `TaxiVR` namespace and the following subtrees; do **not** create a parallel `Assets/_TaxiVR` root or duplicate assets:

```text
Assets/_Project/
  Scripts/TaxiVR/
    Bootstrap/       // composition root, diagnostics, gate feature flags
    Domain/          // pure C# contracts and state machines
    Input/           // action readers, arbitration, keyboard fallback
    XR/              // seated origin, hands, wheel, shifter adapters
    FootTracker/     // Unity UDP lifecycle and main-thread handoff
    Driving/         // pedal mapping, skid signal, vehicle adapter
  Tests/TaxiVR/
    EditMode/        // pure domain and parser tests
    PlayMode/        // deterministic Unity seam tests
  Scenes/Main.unity
  Prefabs/{Taxi, Cockpit, Hands, Route, Systems}/
  Settings/{Input, OpenXR, Build}/
  Evidence/Gates-0-3/

FootTracker/         // separate Python package; never imported by Unity
  pyproject.toml
  src/foottracker/{protocol,source,cli}.py
  tests/
  README.md
```

Recommended asmdefs are `TaxiVR.Domain`, `TaxiVR.Runtime`, `TaxiVR.Editor`, `TaxiVR.Tests.EditMode`, and `TaxiVR.Tests.PlayMode`. `TaxiVR.Domain` references no Unity assemblies. `TaxiVR.Runtime` owns Unity-facing adapters. Tests reference only the assembly under test plus the required Unity test framework.

## Runtime composition and data flow

`TaxiVRCompositionRoot` is the only scene-owned coordinator. It wires interfaces in `Awake`, validates required references, starts services in `OnEnable`, applies latest external state in `Update`, and stops services in `OnDisable`/`OnDestroy` before references are released.

```text
XR controllers ──> wheel/shifter interactors ──┐
Keyboard ────────> TaxiKeyboardSource ─────────┤
FootTracker UDP -> receiver -> latest handoff ──┤
                                                v
                                      IControlArbiter
                                                v
                                      VehicleCommand
                                                v
                                  TaxiVehicleController
                                                v
                              Rigidbody / wheel visual pivots
```

`VehicleCommand` is a per-frame immutable value containing steering `[-1,1]`, direction (`Neutral|Forward|Reverse`), throttle/brake `[0,1]`, and diagnostics flags. No input source writes directly to the Rigidbody. `TaxiVehicleController.FixedUpdate` consumes the most recently published command, applies forces/torques and braking, and updates visual wheel pivots. `Update` handles interaction and diagnostics; `FixedUpdate` is the only physics mutation point.

## Interfaces and contracts

Core interfaces:

```csharp
public interface IFootPacketParser {
    ParseResult Parse(ReadOnlySpan<byte> datagram, long receiveTimeMs);
}
public interface IFootStateMachine {
    FootSnapshot Step(FootObservation observation, long nowMs);
}
public interface IFootPacketSource : IDisposable {
    void Start(Action<ValidatedFootPacket> onAccepted, Action<PacketRejection> onRejected);
    void Stop();
}
public interface IControlSource { ControlSample Read(); }
public interface IControlArbiter { VehicleCommand Resolve(ControlSample xr, ControlSample keyboard, FootSnapshot foot); }
public interface IVehicleDrive { void Apply(VehicleCommand command, float fixedDeltaTime); }
public interface IDiagnosticSink { void Set(DiagnosticEvent value); }
```

Unity-specific implementations include `UdpFootPacketSource`, `TaxiInputActionSource`, `TaxiKeyboardSource`, `WheelInteractor`, `ShifterInteractor`, `TaxiControlArbiter`, and `RigidbodyTaxiDrive`. Domain code receives values, not `InputAction`, `Transform`, `GameObject`, threads, or sockets.

### Input arbitration

Priority is explicit and deterministic: active XR wheel/shifter values win for their respective controls; otherwise the fixed keyboard fallback wins; absent controls resolve neutral. FootTracker owns pedals when its effective snapshot is valid and armed. When FootTracker is disabled or unavailable, the same keyboard fallback may provide throttle and brake under an explicit development setting; it never silently overrides a live valid tracker. XR joystick/gamepad bindings are absent from the Taxi map and `TaxiInputActionSource` rejects any device other than the configured controller pose/interactor or keyboard. A diagnostic records the selected source per control.

Fixed keyboard reference mapping: `W` = throttle, `S` = brake, `A/D` = steering, and `Q/E` = Forward/Reverse direction. `N` is an optional neutral key. Direction keys select constrained shifter states, not a continuous gear value; Space and Ctrl are not pedal bindings.

## FootTracker packet and freshness semantics

The wire format is UTF-8 JSON datagrams, one packet per datagram, with a strict schema and no unknown-field requirement:

```json
{"v":1,"seq":1842,"ts":1710000000123,"red":"up","green":"down"}
```

`v` is protocol version; `seq` is a uint32 monotonic sequence with wrap-aware comparison; `ts` is producer Unix epoch milliseconds; marker values are exactly `up|down|unknown`. `seq` is authoritative for ordering. `ts` must be within configured clock-skew bounds and packet age must be `<= maxPacketAgeMs`; receive time is retained separately for loss detection. A packet is accepted only if version, fields, enum values, numeric ranges, sequence ordering, and freshness pass. First valid packet establishes the sequence baseline. Equal or older sequence is ignored/rejected. Wrap comparison uses unsigned modular distance and rejects ambiguous half-range jumps.

`ValidatedFootPacket` contains protocol version, sequence, producer timestamp, receive monotonic time, and `RedMarker`/`GreenMarker`. `ParseResult` distinguishes malformed, unsupported-version, invalid-metadata, stale, and out-of-order for diagnostics without changing state.

The Python package owns camera/device acquisition, marker classification, and serialization only. `foottracker.protocol` defines the same schema, validation, sequence, and fixture vectors; `foottracker.cli` provides a documented launcher and configurable camera source, bind/target, and heartbeat settings. It must be installable with `python -m pip install .` and runnable without Unity. Unity receives UDP only; it does not process video or import Python.

### Separate Python/OpenCV camera-to-state pipeline

`FootTracker/` is a separately packageable Python process/component. Its source adapter accepts a configurable USB webcam index or a cellphone-as-webcam device exposed by the operating system, with camera resolution, frame rate, HSV ranges, and output target configurable from the CLI. For each frame it resizes the image, converts BGR to HSV, builds red and green masks (including the red hue wraparound), applies morphology to remove noise and close gaps, extracts contours, and selects the largest contour that passes configured area and image-bound checks for each marker.

For each selected contour the pipeline runs `approxPolyDP` and computes a confidence record from convexity, contour area, aspect ratio, near-right-angle corner geometry, and rectangularity (contour area divided by approximated polygon area). It also reports the contour centroid Y and marker-relative scale. A marker is `Up` or `Down` only when shape/orientation confidence and visibility checks pass; invalid shape, invalid orientation, insufficient area, or occlusion produces `Unknown`, never a guessed state.

At startup, before normal classification, the process requires both markers to be visibly Down and calibrates their ground centroid-Y values for 0.5–1.0 seconds. The configured lift threshold is expressed either in pixels or as a marker-relative scale, with the selected mode recorded in diagnostics. Classification then applies the calibrated baseline, confidence gates, and temporal debounce before emitting a state packet. Calibration cannot arm the Unity state machine; the first accepted Up after calibration does. Camera frames and video remain entirely inside Python and are never sent to or decoded by Unity.

Unity's receiver may block on a dedicated background thread, but it publishes only to a bounded single-slot `LatestPacketHandoff` using an atomic exchange/lock. It never touches Unity objects. `Update` atomically takes and applies the slot, so superseded packets may be discarded. `Stop` cancels the socket, closes it, joins with a bounded timeout, then marks the source inactive; callbacks are suppressed after shutdown.

## Pure foot state machine

`FootStateMachine` starts `Unarmed`. Each marker has candidate and effective values plus candidate-start time. A candidate must remain unchanged for `debounceMs` and satisfy the configured hold sample/time requirement before becoming effective. `Unknown` is a real effective value, neither Up nor Down, and cannot arm. The first accepted effective combination containing either Up transitions `Unarmed -> Armed`; it never returns to Unarmed during the session. Invalid packets do not extend an active command. If `now - lastAcceptedReceive > lossTimeoutMs`, the snapshot is `Lost`, neutral, and exposes `footWarning`; an accepted packet can clear the warning after normal debounce.

Truth table used by pure `FootCommandMapper` (Green is the first column, Red the second):

| Armed Green | Armed Red | Throttle | Brake | Skid intent |
|---|---|---:|---:|---:|
| Up | Up | 0 | 0 | 0 |
| Down | Up | 1 | 0 | 0 |
| Up | Down | 0 | 1 | 0 |
| Down | Down | 1 | 1 | 1 |
| either Unknown / Lost | any | 0 | 0 | 0 |
| Unarmed | any | 0 | 0 | 0 |

The mapper outputs normalized pedal intent, not force. The driving layer applies thresholds, speed limits, and the configured dual-pedal skid response. This preserves the specified “both pedals” state while guaranteeing Unknown/loss cannot sustain acceleration.

## Wheel, shifter, and hands

The cockpit prefab contains explicit `SeatOrigin`, `LeftHandAnchor`, `RightHandAnchor`, `WheelGrabZone`, `WheelCenter`, `WheelAxis`, `ShifterPivot`, `ForwardDetent`, `ReverseDetent`, and `NeutralZone` transforms. Controller-driven virtual hands follow the corresponding Meta XR controller/hand pose through the package's direct grab/hand interaction components; animation is a visual adapter and does not own drive input.

Wheel steering is calculated in the wheel plane around `WheelAxis`. For one hand, project the hand position into the wheel plane and compute signed angle from the hand's grab vector to its current vector; apply a calibrated angular-to-normalized scale and clamp to `[-1,1]`. On second-hand grab, capture the current normalized steering and both hand angular references; solve the wheel angle from the signed angle between the two-hand chord (or its stable perpendicular when nearly coincident), choosing the branch nearest the prior angle. On release, retain the current wheel angle and rebase the remaining hand before continuing. This prevents hand-count jumps. A deadband handles a chord below epsilon. Wheel rotation is visualized from the continuous internal angle and reports only normalized steering.

The shifter is a constrained one-axis/arc interactable. Its local position is projected onto the detent axis; Forward and Reverse are finite regions separated by a Neutral deadband. Hysteresis prevents chatter at boundaries. Only `Forward`, `Reverse`, and `Neutral` are emitted; invalid/ambiguous or unheld transitions emit Neutral. Direction is changed only after release or stable detent dwell, preventing accidental gear changes during a grab.

## Vehicle seam and route

`Taxi_Full.fbx` is consumed as a visual child of a project-owned `Taxi` prefab; because importer metadata has no colliders, the prefab adds primitive/mesh colliders explicitly, a Rigidbody, center-of-mass marker, four wheel contact points, front steering pivots, and interaction anchors. Avoid relying on FBX object names for control references; serialized child references are validated by `TaxiPrefabValidator`.

`RigidbodyTaxiDrive` is a deliberately small arcade controller: direction gates signed longitudinal motor force, normalized steering applies front axle yaw/steer and capped lateral response, brake applies opposing force, and neutral applies no powered force while allowing configured damping. It exposes `skidWarning` when both pedal values exceed the dual-pedal threshold and applies the documented MVP reduction in grip/controlled slip. Physics tuning is ScriptableObject data so tests and calibration do not require code edits.

`Route` is a graybox loop/straight with boundary colliders, spawn pose, and end marker. It is a driving test fixture, not product navigation. A deterministic `DrivingScenarioHarness` injects commands and records pose, direction, collision, and skid events for PlayMode tests.

## Gate 0 Windows/OpenXR setup

Create a Windows x86-64 Build Profile with `Main.unity` as its sole required scene, development diagnostics enabled for development builds, and release diagnostics retained at low overhead. Use the installed Unity 6.0.6, OpenXR 1.18.0, Meta XR 205.0.0, and Input System 1.20.0 packages already present. Configure the Windows OpenXR loader/runtime and Quest Link selection through project settings, validate the active loader at startup, and do not infer runtime success from package presence. Add a dedicated `TaxiVR` action map rather than extending the template Player map; include only explicitly approved controller pose/interactor and keyboard bindings.

`RuntimeDiagnostics` reports build identity, Unity version, scene name, active XR loader, OpenXR runtime/device status, action-map enablement, tracker status, selected input sources, and warnings (`footWarning`, `skidWarning`). F1 opens a monitor-only diagnostics window on the desktop display; it is never rendered into the headset or cockpit view. Diagnostics are also written to a timestamped JSONL/text evidence log. Startup fails visibly with a recovery instruction when the required scene/action/XR references are missing.

## Tests and evidence

EditMode tests cover packet vectors, version/field rejection, sequence wrap/order, freshness, debounce/hold, arming, Unknown, loss, truth table, arbitration, wheel angle math, shifter hysteresis, and command gating. PlayMode tests cover composition validation, main-thread handoff, shutdown/no-post-shutdown mutation, wheel hand-count continuity, shifter output, keyboard mapping, joystick exclusion, taxi neutral/forward/reverse, collider boundaries, and skid diagnostic. Hardware behavior is never represented as automated coverage.

Gate evidence is stored under `Assets/_Project/Evidence/Gates-0-3/` or the repository's evidence location with build hash, Unity/package versions, Windows/OpenXR runtime, device firmware/configuration, test results, timestamps, screenshots/video where permitted, and observed failures. The operator must record each step: packaged launch and scene; Quest stereoscopic view/head movement; both hands; one/two-hand wheel continuity; Forward/Reverse/Neutral shifter; keyboard fallback; joystick non-effect; valid/Unknown/lost FootTracker; pedal truth table; skid warning; forward/reverse route and collision boundary. Meta XR Operator is used only after Unity is in Play Mode/OpenXR is active. The current session has no online runtime evidence; MCP tool installation is not validation.

## Incremental rollout and rollback

1. **Gate 0:** settings, Build Profile, input asset, `Main` bootstrap, diagnostics, and bootstrap tests. Roll back these files as one unit to restore the blank baseline.
2. **Gate 1:** cockpit/hands/wheel/shifter prefabs, scene composition, input adapters, and interaction tests. Disable `CockpitFeature` to retain Gate 0 launch while reverting this unit.
3. **Gate 2:** Python package, protocol fixtures, Unity receiver/handoff, state machine, and tests. Disable `FootTrackerFeature` to return to keyboard pedal testing; no Python artifact is embedded in the executable.
4. **Gate 3:** taxi prefab/physics data, route, driving adapter, pedal/skid integration, and evidence. Disable `DrivingFeature` to retain cockpit diagnostics without powered movement.

Each gate is a reviewable work unit under the 400-line budget where practicable; if a gate forecasts above budget, split implementation and evidence into focused chained slices without crossing the feature-disable boundary.

## Implementation checklist

- [ ] Main scene and Windows x86-64 profile are build-enabled.
- [ ] Dedicated TaxiVR input map excludes joystick driving.
- [ ] Pure domain and Unity test assemblies exist.
- [ ] Seated cockpit anchors, direct hands, continuous wheel, and constrained shifter are wired.
- [ ] Versioned UDP/Python boundary, latest-packet handoff, timeout, and shutdown are tested.
- [ ] Taxi colliders/pivots/physics seam and deterministic route are validated.
- [ ] Gate 0–3 hardware evidence is recorded separately from automated test results.
- [ ] Deferred product systems remain absent from the acceptance path.
