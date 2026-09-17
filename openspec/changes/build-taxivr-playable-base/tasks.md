# TaxiVR Playable Base — Implementation Tasks

Implement Gates 0–3 as small, independently reversible work units. Keep tests with the behavior they verify and keep deferred passenger, product GPS, request, fault, scoring, outcome, and results systems absent.

## Review Workload Forecast

| Work unit | Estimated changed lines | Boundary |
|---|---:|---|
| 0. Unity bootstrap and build setup | 140–190 | Windows profile, Main scene bootstrap, diagnostics, TaxiVR action map |
| 1. Pure domain contracts and tests | 260–340 | packet/state/command/arbitration math with EditMode tests |
| 2. Unity UDP adapter and handoff | 170–230 | receiver lifecycle, latest-packet handoff, Unity integration tests |
| 3. Python FootTracker CV package and tests | 300–380 | protocol/source/CV/CLI, Python fixtures and tests |
| 4. Cockpit XR interactions | 300–380 | seated origin, hands, wheel, shifter, keyboard fallback |
| 5. Vehicle physics and driving seams | 260–340 | taxi adapter, pedals, skid, physics tuning, PlayMode tests |
| 6. Scene and prefab composition | 180–240 | taxi/cockpit/route/systems prefabs and Main composition |
| 7. Hardware validation and evidence | 120–180 | executable and Quest/webcam/wheel acceptance records |
| **Total** | **1,730–2,280** | **Eight reviewable chained units** |

| Field | Value |
|-------|-------|
| Estimated changed lines | 1,730–2,280 authored lines across eight units |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1: bootstrap → PR 2: pure domain → PR 3: Unity UDP → PR 4: Python FootTracker → PR 5: cockpit → PR 6: vehicle → PR 7: composition → PR 8: hardware evidence |
| Delivery strategy | auto-chain |
| Chain strategy | stacked-to-main (explicitly selected by the user) |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High

Each PR must remain independently reviewable, include its focused tests, state its rollback boundary, and identify the immediately preceding PR dependency. Generated Unity metadata and evidence files are included in final snapshot identity but are excluded from the authored-line estimate. Do not combine slices merely to reduce line count.

## Shared implementation rules

- Use `Assets/_Project/Scripts/TaxiVR/` and `Assets/_Project/Tests/TaxiVR/`; do not create `Assets/_TaxiVR`.
- Use the planned `TaxiVR.Domain`, `TaxiVR.Runtime`, `TaxiVR.Editor`, `TaxiVR.Tests.EditMode`, and `TaxiVR.Tests.PlayMode` assembly boundaries. Domain code must reference no Unity types.
- For automatable behavior, execute strict TDD in order: RED (failing focused test), GREEN (minimal implementation), TRIANGULATE (edge cases/property/table coverage), then REFACTOR (remove duplication without changing behavior).
- Record for every unit: focused test command and exact result, runtime scenario or `N/A` with reason, changed-file boundary, and rollback boundary.
- Hardware evidence is human-observed evidence only; it must never be described as automated test coverage.
- Do not implement passenger, product GPS/route progression, requests, faults, scoring, outcomes, results, final art, audio, or production polish.

## Gate 0 — Windows PCVR and executable bootstrap

### Work unit 0 — Unity project/build setup

**Depends on:** repository baseline and installed Unity/OpenXR/Meta XR packages. **Finishes with:** a build-enabled Windows x86-64 `Main` scene that starts a diagnostic bootstrap and exposes feature flags for later gates. **Rollback:** revert only the build profile, settings, input asset, bootstrap scene/scripts, and associated tests.

**Likely edit surfaces:** `ProjectSettings/EditorBuildSettings.asset`, `Assets/Settings/Build/`, `Assets/Settings/OpenXR/`, `Assets/InputSystem_Actions.inputactions` or a project-owned taxi input asset, `Assets/_Project/Scenes/Main.unity`, `Assets/_Project/Scripts/TaxiVR/Bootstrap/`, `Assets/_Project/Tests/TaxiVR/`.

- [ ] RED: add EditMode/bootstrap tests proving the required scene, TaxiVR action map, Windows target/profile, and missing-reference diagnostics are absent or invalid in the baseline; record the focused Unity Test Runner command/result. <!-- sdd-owner: implementation -->
- [ ] GREEN: create the Windows x86-64 build profile, make `Main.unity` the required scene, configure the existing OpenXR/Meta XR packages and runtime diagnostics, and add a `TaxiVR` action map containing only approved controller/interactor and keyboard bindings; prove joystick bindings are absent. <!-- sdd-owner: implementation -->
- [ ] TRIANGULATE: cover build identity, Unity version, active loader/runtime, scene name, action-map enablement, recovery diagnostics, development/release diagnostic modes, and deferred-system absence through EditMode/PlayMode seam tests. <!-- sdd-owner: implementation -->
- [ ] REFACTOR: isolate `TaxiVRCompositionRoot`/`RuntimeDiagnostics` and feature flags so later units can add cockpit, tracker, and driving without changing bootstrap behavior. <!-- sdd-owner: implementation -->
- [ ] Verify a Windows development build launches `Main.unity` and writes diagnostic JSONL/text output; record automated result separately from the human Quest 2 view/head-tracking checkpoint, which remains pending until Work Unit 7. <!-- sdd-owner: implementation -->

## Gate 1 — Seated cockpit interaction

### Work unit 1 — Pure domain contracts and tests

**Depends on:** Work Unit 0 assembly layout. **Finishes with:** Unity-independent contracts and deterministic logic for foot packets/state, input arbitration, wheel math, shifter gating, and vehicle commands. **Rollback:** remove `TaxiVR.Domain` and its EditMode tests without affecting Unity scene assets.

**Likely edit surfaces:** `Assets/_Project/Scripts/TaxiVR/Domain/`, `Assets/_Project/Scripts/TaxiVR/Input/` for contracts only, `Assets/_Project/Tests/TaxiVR/EditMode/`, asmdef files under `Assets/_Project/Tests/TaxiVR/`.

- [ ] RED: write failing EditMode tests for `IFootPacketParser`, packet rejection categories, sequence ordering/wrap, freshness, `FootStateMachine`, arming, Unknown, loss timeout, the pedal truth table, and neutral command gating. <!-- sdd-owner: implementation -->
- [ ] GREEN: implement pure `VehicleCommand`, marker/packet records, parser result types, `FootStateMachine`, `FootCommandMapper`, `IControlArbiter`, and deterministic keyboard/XR source arbitration with no Unity, socket, thread, or `InputAction` dependency. <!-- sdd-owner: implementation -->
- [ ] TRIANGULATE: add table/property coverage for startup Down/Down, first debounced Up, Unknown never arming, invalid input not extending commands, stale/out-of-order packets, sequence wrap ambiguity, joystick exclusion, wheel angle continuity math, shifter hysteresis, and neutral/forward/reverse output. <!-- sdd-owner: implementation -->
- [ ] REFACTOR: separate protocol validation, state transitions, mapping, and arbitration responsibilities while preserving immutable per-frame command semantics and documented numeric ranges. <!-- sdd-owner: implementation -->
- [ ] Verify with Unity EditMode tests and retain the exact pass count; runtime evidence is `N/A` because this unit has no Unity-object or hardware boundary. <!-- sdd-owner: implementation -->

### Work unit 2 — Cockpit XR interactions

**Depends on:** Work Units 0–1. **Finishes with:** seated XR origin, controller-driven virtual hands, direct wheel interaction, constrained Forward/Reverse shifter, and keyboard fallback through shared seams. **Rollback:** disable `CockpitFeature` and remove cockpit interaction prefabs/scripts/tests while retaining Gate 0 launch.

**Likely edit surfaces:** `Assets/_Project/Scripts/TaxiVR/XR/`, `Assets/_Project/Scripts/TaxiVR/Input/`, `Assets/_Project/Prefabs/Cockpit/`, `Assets/_Project/Prefabs/Hands/`, `Assets/_Project/Tests/TaxiVR/EditMode/`, `Assets/_Project/Tests/TaxiVR/PlayMode/`.

- [ ] RED: add failing EditMode tests for one-hand/two-hand/release wheel continuity, normalized steering clamp/deadband, shifter detent/hysteresis/release gating, fixed `W/S/A/D/Q/E/N` fallback mapping, and joystick non-effect; add PlayMode seam tests for hand-count transitions and no-post-disable input. <!-- sdd-owner: implementation -->
- [ ] GREEN: implement `WheelInteractor`, `ShifterInteractor`, `TaxiInputActionSource`, `TaxiKeyboardSource`, seated origin anchors, controller pose hand visuals, direct grab/poke adapters using the installed Meta XR Interaction SDK, and shared `ControlSample` output. <!-- sdd-owner: implementation -->
- [ ] TRIANGULATE: test near-coincident two-hand chords, release/rebase behavior, ambiguous shifter positions, invalid detents, controller absence, fallback precedence, and that the animation adapter cannot directly mutate vehicle physics. <!-- sdd-owner: implementation -->
- [ ] REFACTOR: keep visual hand animation, wheel/shifter interaction, and source arbitration separate; validate serialized anchors and feature-disable lifecycle from the composition root. <!-- sdd-owner: implementation -->
- [ ] Verify EditMode/PlayMode results automatically; separately list the human Quest/controller checkpoint for seated origin, visible hand tracking, direct wheel continuity, and shifter behavior as not automated. <!-- sdd-owner: implementation -->

## Gate 2 — FootTracker boundary

### Work unit 3 — Unity UDP adapter

**Depends on:** Work Units 0–1; Work Unit 1 is the canonical owner of the finalized versioned protocol types/schema, which this adapter implements without importing Python. **Finishes with:** validated UDP receive, bounded latest-packet handoff, main-thread application, timeout diagnostics, and safe shutdown. **Rollback:** disable `FootTrackerFeature`; keyboard pedal testing remains available.

**Likely edit surfaces:** `Assets/_Project/Scripts/TaxiVR/FootTracker/`, `Assets/_Project/Tests/TaxiVR/PlayMode/`, `Assets/_Project/Tests/TaxiVR/EditMode/`, `Assets/_Project/Scripts/TaxiVR/Bootstrap/`.

- [ ] RED: add failing tests for valid/malformed/unsupported/stale/out-of-order datagrams, latest-packet replacement, main-thread-only application, loss timeout, cancellation, bounded join, and callback suppression after disable/destroy. <!-- sdd-owner: implementation -->
- [ ] GREEN: implement `UdpFootPacketSource`, `LatestPacketHandoff`, Unity lifecycle wiring, validated packet callbacks, diagnostics, and `Update`-only state application; keep sockets/threads away from Unity objects. <!-- sdd-owner: implementation -->
- [ ] TRIANGULATE: exercise burst packets, superseded packets, socket cancellation while blocked, malformed UTF-8/JSON, sequence wrap, clock skew, repeated enable/disable, and receiver failure recovery with mocked UDP fixtures. <!-- sdd-owner: implementation -->
- [ ] REFACTOR: isolate transport, handoff, and state application so packet parsing remains domain-owned and shutdown behavior is deterministic and observable. <!-- sdd-owner: implementation -->
- [ ] Verify Unity EditMode/PlayMode results and a mocked local UDP scenario; real webcam/FootTracker evidence is explicitly deferred to Work Unit 7. <!-- sdd-owner: implementation -->

### Work unit 4 — Python FootTracker CV and tests

**Depends on:** Work Unit 1’s finalized versioned protocol types/schema; derive Python serialization and fixtures directly from those canonical decisions, with no dependency on Work Unit 3. **Finishes with:** separately installable/runnable Python process that performs camera classification and emits only validated state packets. **Rollback:** remove only `FootTracker/` and its documentation/fixtures; Unity continues with disabled tracker or keyboard fallback.

**Likely edit surfaces:** `FootTracker/pyproject.toml`, `FootTracker/src/foottracker/protocol.py`, `source.py`, `cli.py`, `FootTracker/tests/`, `FootTracker/README.md`, shared protocol fixture documentation.

- [ ] RED: add failing Python tests for schema vectors, exact marker enums, sequence/freshness validation, red hue wraparound, contour selection, morphology, confidence gates, calibration, lift threshold modes, debounce, Unknown on invalid/occluded shapes, and CLI configuration. <!-- sdd-owner: implementation -->
- [ ] GREEN: implement the installable `foottracker.protocol`, configurable USB/cellphone-webcam source, BGR-to-HSV red/green masks, morphology, contour/shape/orientation/rectangularity confidence, Down calibration, temporal debounce, and `foottracker.cli` UDP launcher without any Unity/video dependency. <!-- sdd-owner: implementation -->
- [ ] TRIANGULATE: use deterministic synthetic images/fixtures for hue boundaries, multiple contours, insufficient area, malformed shapes, occlusion, camera failure, calibration timeout, threshold pixel/relative-scale modes, and packet heartbeat/failure behavior. <!-- sdd-owner: implementation -->
- [ ] REFACTOR: keep acquisition, classification, protocol serialization, and CLI configuration separate; document camera index/resolution/FPS/HSV/target/bind/heartbeat options and calibration diagnostics. <!-- sdd-owner: implementation -->
- [ ] Verify `python -m pip install .`, the focused Python test command, and a loopback packet emission test; webcam classification remains a human hardware checkpoint, not automated acceptance. <!-- sdd-owner: implementation -->

## Gate 3 — Driving controls and taxi

### Work unit 5 — Vehicle physics and driving adapter

**Depends on:** Work Units 1–4 and cockpit control seams. **Finishes with:** deterministic command-to-taxi physics, pedal/skid behavior, colliders/pivots validation hooks, and keyboard/tracker source integration. **Rollback:** disable `DrivingFeature` and retain cockpit diagnostics without powered movement.

**Likely edit surfaces:** `Assets/_Project/Scripts/TaxiVR/Driving/`, `Assets/_Project/Scripts/TaxiVR/Domain/` for pure tuning/command seams, `Assets/_Project/Tests/TaxiVR/EditMode/`, `Assets/_Project/Tests/TaxiVR/PlayMode/`, `Assets/_Project/Settings/Driving/`.

- [ ] RED: add failing tests for neutral no-force, signed Forward/Reverse force, steering clamp, brake force, speed limits/damping, dual-pedal skid warning/grip reduction, Unknown/loss neutralization, and deterministic command recording. <!-- sdd-owner: implementation -->
- [ ] GREEN: implement `RigidbodyTaxiDrive`, pedal/skid integration, ScriptableObject tuning, `TaxiPrefabValidator`, fixed-update-only physics mutation, wheel visual pivot updates, and `DrivingScenarioHarness`. <!-- sdd-owner: implementation -->
- [ ] TRIANGULATE: cover missing references, direction changes, zero/negative values, collision boundaries, single-pedal no-skid, threshold edges, repeated neutral frames, and tracker-disabled keyboard throttle/brake fallback. <!-- sdd-owner: implementation -->
- [ ] REFACTOR: keep physics adapter, tuning data, diagnostics, and test harness independent; ensure no source writes directly to the Rigidbody outside `FixedUpdate`. <!-- sdd-owner: implementation -->
- [ ] Verify focused EditMode/PlayMode tests and deterministic harness output; Quest, wheel, and webcam runtime behavior remains human evidence only. <!-- sdd-owner: implementation -->

### Work unit 6 — Scene/prefab composition

**Depends on:** Work Units 0–5. **Finishes with:** one build-enabled Main scene containing the graybox seated cockpit, taxi, route, systems, and explicit feature flags/anchors. **Rollback:** revert only composition/prefab/scene assets and disable Gate 3, preserving independently tested code.

**Likely edit surfaces:** `Assets/_Project/Scenes/Main.unity`, `Assets/_Project/Prefabs/{Taxi,Cockpit,Hands,Route,Systems}/`, `Assets/_Project/Materials/`, `Assets/_Project/Scripts/TaxiVR/Bootstrap/`, `Assets/_Project/Tests/TaxiVR/PlayMode/`.

- [ ] RED: add failing PlayMode composition/validation tests for required anchors (`SeatOrigin`, hands, wheel, shifter detents, taxi pivots, contact points), build scene presence, boundary colliders, and deferred-system absence. <!-- sdd-owner: implementation -->
- [ ] GREEN: compose the Main scene, graybox route loop/straight with spawn/end/boundaries, project-owned Taxi prefab around `Taxi_Full.fbx`, cockpit and hands prefabs, system references, materials, colliders, Rigidbody, center-of-mass marker, and serialized reference validation. <!-- sdd-owner: implementation -->
- [ ] TRIANGULATE: test disabled feature flags, missing/invalid serialized references, scene reload, deterministic spawn, route collision boundaries, prefab instance integrity, and bootstrap startup recovery diagnostics. <!-- sdd-owner: implementation -->
- [ ] REFACTOR: keep scene-owned coordination in `TaxiVRCompositionRoot`; avoid FBX-name-based control references and avoid duplicating a second project root or disconnected gameplay scenes. <!-- sdd-owner: implementation -->
- [ ] Verify Unity PlayMode composition tests and a development build smoke launch; do not claim headset, wheel, or FootTracker hardware acceptance from this automated result. <!-- sdd-owner: implementation -->

### Work unit 7 — Hardware validation and evidence

**Depends on:** Work Units 0–6 and a packaged Windows executable. **Finishes with:** human-recorded Gate 0–3 evidence and an explicit accepted/unaccepted status. **Rollback:** remove or correct evidence records only; do not alter implementation to manufacture a passing result.

**Likely edit surfaces:** `Assets/_Project/Evidence/Gates-0-3/` (or repository-approved evidence location), `FootTracker/README.md` for operator steps, build/package documentation.

- [ ] Prepare the repeatable operator checklist with build hash, Unity/package versions, Windows/OpenXR runtime, Quest firmware/configuration, controller/wheel/tracker configuration, timestamps, screenshots/video where permitted, observed failures, and recovery notes. <!-- sdd-owner: implementation -->
- [ ] Execute and record the human Windows executable launch, non-sample Main scene, desktop diagnostics, Quest 2 stereoscopic view, head tracking, both controller hands, one/two-hand wheel continuity, Forward/Reverse/Neutral shifter, keyboard fallback, and joystick non-effect. <!-- sdd-owner: implementation -->
- [ ] Execute and record the human webcam/FootTracker process, Down calibration, first-Up arming, all four pedal truth-table states, Unknown, malformed/lost timeout neutral behavior, and Unity latest-packet behavior. <!-- sdd-owner: implementation -->
- [ ] Execute and record the human route drive forward/reverse, steering, collision boundary, neutral behavior, dual-pedal skid warning, and safe recovery/shutdown scenario; distinguish every observation from automated Unity/Python test results. <!-- sdd-owner: implementation -->
- [ ] Review evidence for missing hardware checkpoints and mark Gates 0–3 accepted only when required real-device evidence exists; explicitly state that passenger, GPS, request, fault, scoring, outcome, and result systems remain deferred. <!-- sdd-owner: implementation -->

## Completion gate

- [ ] Confirm each chained work unit has its focused automated result, runtime scenario result or justified `N/A`, exact rollback boundary, and no unauthorized deferred-system implementation. <!-- sdd-owner: implementation -->
- [ ] Confirm the final review snapshot remains split at the proposed chain boundaries and that no slice is represented as accepted solely by package presence, editor success, MCP availability, or simulated hardware. <!-- sdd-owner: implementation -->
