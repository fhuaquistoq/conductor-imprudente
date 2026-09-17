# Proposal: Build TaxiVR Playable Base

## Intent

Create the full TaxiVR playable-base foundation through staged, hardware-first slices. The first acceptance gate is deliberately narrower than the eventual product: prove a Windows executable can deliver Quest 2 PCVR view and tracking, controller-driven virtual hands, physical wheel, Forward/Reverse shifter, separate FootTracker red/green Up/Down/Unknown UDP input, throttle/brake/skid behavior, and a drivable taxi. Passenger, GPS, request, fault, scoring, and result systems remain blocked until that chain has stable evidence.

This is a proposal only. No commits or pull requests are authorized in this phase.

## Product and technical decisions preserved

- Unity does not process video; FootTracker is a separately packageable process/component boundary communicating over UDP.
- Prefer direct grab/poke interactions where practical; do not use ray interaction as the default substitute.
- Joysticks never drive the taxi. A keyboard fallback exists for development and recovery.
- The foot system starts **Unarmed** while both markers are Down and arms only on the first Up. Unknown is neither Up nor Down.
- Prolonged tracker loss fails safe to neutral.
- Prefer one main Unity scene, with deterministic test seams rather than a collection of disconnected gameplay scenes.
- Pure route, foot, request, fault, outcome, and score logic receives priority in automated tests; hardware/runtime evidence complements, but does not replace, those tests.

## Staged scope

### Gate 0 — Windows PCVR and executable bootstrap

Establish a Windows x86-64 standalone build profile, OpenXR/Quest Link configuration, dedicated taxi input action map, packaging/runtime diagnostics, and a build-enabled main scene. Confirm the executable launches and presents a stable Quest 2 view and head tracking.

### Gate 1 — Seated cockpit interaction

Replace the blank sample composition with a seated XR origin, graybox cockpit, controller-animated virtual hands, physical wheel interaction, and Forward/Reverse shifter. Use direct grab/poke where feasible. Add keyboard fallback without allowing joystick input to drive. Validate interaction anchors, seated origin, hand visuals, and shifter gating in headset and in deterministic test seams.

### Gate 2 — FootTracker boundary

Define and version a small UDP packet contract and a separately packageable FootTracker adapter/source. Keep packet parsing, marker classification, arming, debouncing/hold behavior, Unknown handling, timeout detection, and neutral fail-safe pure and independently testable. Unity receives off the main thread only as needed, then applies the latest validated state on the main thread; lifecycle shutdown and malformed/stale packets are explicit.

### Gate 3 — Driving controls and taxi

Map the validated foot state to throttle/brake/skid behavior, retain neutral on Unknown/loss, tune a basic taxi physics/controller setup around the supplied taxi asset, and connect wheel, shifter, and pedals into a minimal drivable test route. Add colliders, steering pivots, interaction anchors, and a repeatable runtime acceptance scenario.

### Gate 4 — Full playable-base follow-ons

Only after Gate 3 evidence is accepted, implement the remaining product systems as separate reviewable change slices: passenger/session lifecycle, GPS and route logic, request generation/handling, faults and recovery, scoring/outcomes/results, and final content/polish. These follow-ons may share the main scene and pure domain assemblies but must not be smuggled into the hardware MVP.

## Scope boundaries and non-goals

### First acceptance gate includes

- Windows executable and Quest 2 PCVR view/tracking.
- Controller-tracked, animated virtual hands.
- Direct wheel interaction and Forward/Reverse shifter.
- Physical/separate FootTracker UDP input with Red/Green Up/Down/Unknown semantics.
- Keyboard fallback, never joystick driving.
- Foot-to-throttle/brake/skid mapping, safe neutral behavior, and a drivable taxi.
- Graybox route/cockpit, diagnostics, test seams, pure logic tests, and runtime evidence.

### Explicitly deferred

- Passenger spawning, boarding, comfort, and passenger state.
- GPS presentation, route progression, requests, and request UI.
- Fault taxonomy, fault UI, scoring, outcome, result, and progression systems.
- Final art, city assembly beyond the minimum deterministic drive route, audio, tutorialization, and production polish.
- Unity video processing, embedded FootTracker implementation, or treating FootTracker as an inseparable Unity subsystem.

“Complete playable base” therefore describes the staged program, not one review unit or one acceptance claim.

## Affected areas

- `ProjectSettings/` and `Assets/Settings/`: Windows/OpenXR build and runtime configuration.
- `Assets/Scenes/`, `Assets/Prefabs/`, `Assets/Materials/`, and likely `Assets/Art/`: main scene, cockpit, route, taxi, hands, and interaction setup.
- `Assets/InputSystem_Actions.inputactions`: dedicated taxi actions and keyboard fallback policy.
- New project-owned runtime and pure-domain scripts under `Assets/_Project/Scripts/` (or repository-approved equivalent), with separate test assemblies under `Assets/_Project/Tests/`.
- New separately packageable FootTracker source/adapter/protocol location to be chosen during design, with mocked UDP fixtures and launch/configuration documentation.
- Build/package documentation and runtime evidence records.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Quest Link/OpenXR configuration works in editor but not executable | Make the Windows executable the first gate; record build, launch, headset, and tracking evidence. |
| Meta interaction/hand wiring or physical wheel alignment is unreliable | Use a graybox cockpit, explicit anchors, direct interaction, and a repeatable headset scenario before content polish. |
| UDP threading, malformed packets, or tracker loss destabilizes Unity | Pure parser/state tests, validated latest-packet handoff, explicit timeout/neutral rules, and clean shutdown tests. |
| Taxi asset lacks colliders and driving pivots | Treat collider/pivot/physics setup as its own gate with a small deterministic route. |
| Scope exceeds the 400-line review budget | Use the staged follow-on slices below and the configured auto-chain delivery strategy; do not combine deferred systems into the MVP. |
| Hardware is unavailable during development | Keep keyboard fallback and mocked UDP source, while requiring real Quest/wheel/FootTracker evidence before declaring the gate passed. |

## Rollback

Each gate is independently removable at its work-unit boundary. Roll back the latest gate by reverting its configuration, scene/prefab, runtime, tests, and evidence changes together; preserve earlier accepted gates. The FootTracker adapter must be disableable so the Unity project returns to keyboard-driven neutral/drive testing without video processing or embedded tracker dependencies. Deferred passenger and domain systems must not require rollback of the hardware chain.

## Success criteria

1. A packaged Windows executable launches into the main scene and provides stable Quest 2 PCVR view and head tracking.
2. Both tracked controller hands visibly animate and can directly operate the wheel and Forward/Reverse shifter.
3. Joysticks do not drive; keyboard fallback operates the same dedicated control seams.
4. FootTracker packets are independently parseable, testable, and separately packageable; Red/Green semantics produce Up/Down/Unknown without treating Unknown as either state.
5. Foot state begins Unarmed with both markers Down, arms only on first Up, and prolonged loss/invalid input results in neutral safely.
6. Wheel, shifter, foot controls, throttle/brake/skid behavior, and a taxi operate together on a deterministic route.
7. Pure logic tests cover foot, route, and later domain seams; runtime evidence records real hardware acceptance separately.
8. Passenger, GPS, request, fault, scoring, and result behavior remains unavailable/blocked until the hardware-chain evidence is accepted.
9. Every implementation slice has a clear scope, verification, and rollback boundary suitable for a review under 400 changed lines where practicable.

## Proposed follow-on change slices

1. `taxivr-windows-pcvr-bootstrap` — executable, OpenXR, build profile, main scene, and diagnostics.
2. `taxivr-cockpit-hands-controls` — seated rig, virtual hands, wheel, shifter, direct interactions, and keyboard fallback.
3. `taxivr-foottracker-protocol` — standalone package boundary, UDP contract, parser/state machine, fixtures, and fail-safe tests.
4. `taxivr-driving-mvp` — pedals, skid behavior, taxi physics, route, and real hardware acceptance evidence.
5. `taxivr-passenger-session` — passenger lifecycle and session state.
6. `taxivr-gps-requests` — GPS, route progression, and request flow.
7. `taxivr-faults-outcomes-scoring` — faults, scoring, outcome, and results.
8. `taxivr-content-polish` — production environment, audio, UX, accessibility, performance, and final packaging.

## Proposal question round

Not applicable: product decisions and the pre-proposal gate were confirmed by the orchestrator from the user’s comprehensive specification. This proposal records those decisions rather than reopening them.

## Skill resolution

`paths-injected`
