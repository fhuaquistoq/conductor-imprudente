# Exploration — build-taxivr-playable-base

## Scope and method

Read-only repository exploration for the approved Taxi VR specification. No gameplay or product files were implemented. CodeGraph was already initialized at the repository root and used before filesystem inspection. The supplied phase skills were loaded from the injected paths (`skill_resolution: paths-injected`). Unity/XR MCP runtime inspection was not performed because no Unity MCP tools were exposed in this session; the repository does contain Meta XR Operator tooling in the local Unity package cache.

## Repository baseline

- Unity editor is pinned to `6000.6.0f1` in `ProjectSettings/ProjectVersion.txt`.
- Project is a URP blank-template baseline. The only enabled build scene is `Assets/Scenes/SampleScene.unity` (`ProjectSettings/EditorBuildSettings.asset`).
- `SampleScene` contains only `Main Camera`, `Directional Light`, and `Global Volume`; there is no XR rig, taxi, interactable cockpit, road, player, or gameplay object.
- Project-owned folders exist as empty source locations under `Assets/_Project/Animations`, `Art`, `Audio`, `Materials`, `Prefabs`, `Scenes`, and `Scripts`; only folder metadata was found.
- No project-owned C# scripts, asmdefs, prefabs, animation controllers, or tests were found under `Assets`. The only scripts are Unity tutorial files under `Assets/TutorialInfo`.
- Generated `Library`, `Temp`, `Logs`, and `UserSettings` state is present locally and should not be treated as product source.

## Packages and configuration

- Installed package set supports the target stack: `com.meta.xr.sdk.all` `205.0.0`, Meta Interaction/Core/Haptics packages `205.0.0`, `com.unity.xr.openxr` `1.18.0`, Input System `1.20.0`, URP `17.6.0`, AI Navigation `2.0.14`, and Unity Test Framework `1.8.0` (`Packages/manifest.json`, `Packages/packages-lock.json`).
- `ProjectSettings/EditorBuildSettings.asset` references Unity Input System actions, XR Management loader settings, and OpenXR settings assets by GUID.
- `ProjectSettings/BuildProfileUtilityOpenXR.asset` records `isMetaQuestInitialized: 1`.
- `ProjectSettings/XRSettings.asset` has VR device disabled set to `False`; this is a generic project setting, not proof of a complete runtime rig.
- `Assets/InputSystem_Actions.inputactions` is the template Player/UI map. It includes keyboard/gamepad/XR controller bindings for generic Move, Look, Interact, Jump, and related actions, but no taxi wheel, shifter, pedal, foot-state, or driving actions.
- `Assets/Oculus/OculusProjectConfig.asset` targets multiple device types and has `handTrackingSupport: 0`; it is not yet configured as a controller-animated-hands gameplay setup.
- `Assets/Settings/Build Profiles/Meta Quest.asset` is an Android Meta Quest build profile (build target 13), not the required Windows PCVR shipping profile. It has no scene override and its scene list is empty. Global settings contain Windows standalone graphics APIs, but no dedicated Windows PCVR profile was found.
- `ProjectSettings/ProjectSettings.asset` still carries template product/application identifiers and names (`Conductor Imprudente`, Unity template identifiers). No product packaging or FootTracker inclusion configuration was found.
- `ProjectSettings/QualitySettings.asset` contains `PC` and `Meta Quest (Build Profile)` quality levels; the Quest profile uses mobile render settings. No measured PCVR performance budget or validation configuration was found.
- `Library/PackageCache/com.meta.xr.sdk.core.../Editor/MetaXROperator/Tools~/Windows/meta-xr-operator-mcp-proxy.exe` exists, and `Library/AgentBridge` exists, indicating local tooling availability, but no runtime/editor session was available to verify operation.

## Reusable assets

- Taxi candidates: `Assets/ThirdParty/Vehicles/Models/Taxi_Full.fbx` and `Assets/ThirdParty/Vehicles/Models/Taxi.fbx`. Additional traffic candidates include `SUV.fbx`, `NormalCar1.fbx`, `NormalCar2.fbx`, `SportsCar.fbx`, `SportsCar2.fbx`, and `Cop.fbx` in the same folder.
- Vehicle FBX metadata uses Unity ModelImporter with material import enabled, no generated colliders (`addColliders: 0`), no authored animation clips, and no generated mesh LODs (`Assets/ThirdParty/Vehicles/Models/Taxi*.fbx.meta`). These models require gameplay-ready colliders, wheel/steering pivots, interaction anchors, materials, and likely visual cleanup.
- City kit is extensive under `Assets/ThirdParty/DowntownCity/Models`, including `Street_Asphalt_6x6.fbx`, `Street_Asphalt_9x9.fbx`, `Street_2Lane.fbx`, `Street_4Lane.fbx`, `Street_4WayIntersection.fbx`, `Street_TIntersection.fbx`, curve/intersection pieces, sidewalks, decals, buildings, doors, windows, and props. This is suitable for a grid-road prototype but no assembled city scene or nav graph exists.
- City textures are available under `Assets/ThirdParty/DowntownCity/Textures`, including asphalt/dirt, brick, metal, roof, street decal, and interior sets. No project-owned materials were found to bind them into a coherent runtime environment.
- Character/animation source exists under `Assets/ThirdParty/UniversalCharacters` and `Assets/ThirdParty/UniversalAnimations`, including full-body male/female models, mannequin assets, and standard animation FBX files. No passenger prefabs or ScriptableObject passenger data exist.

## Testing affordances

- Unity Test Framework `1.8.0` is installed and project context specifies EditMode/PlayMode runners.
- No test assemblies or test files exist in project Assets. Future hardware-chain work needs new test assembly boundaries and tests for pure foot packet parsing/state transitions, input mapping, shifter gating, driving behavior, and fail-safe handling before runtime validation.
- No existing CI/build scripts, Windows packaging scripts, FootTracker test harness, UDP fixtures, or recorded OpenXR test evidence were found.

## Implementation constraints and risks

1. The approved hardware-first chain is currently entirely absent: executable-to-headset view, controller-animated hands, wheel, forward/reverse shifter, foot UDP integration, pedals, and drivable taxi must be built from the template baseline.
2. The repository has Meta XR Interaction and OpenXR packages, but package availability alone does not establish correct action maps, rig prefabs, controller hand visual wiring, Quest Link runtime settings, or device detection.
3. FootTracker is not present: no Python source, executable, protocol schema, calibration assets, or launcher/package integration exists. The UDP contract must be made explicit and tested independently; Unity background receive plus main-thread latest-packet application is a key lifecycle/thread-safety risk.
4. The vehicle FBX assets have no colliders or authored driving setup. Wheel collider/visual pivot alignment, seated origin, physics tuning, and reverse/forward gating are unproven.
5. Only the blank sample scene is build-enabled. Scene composition, XR origin, lighting, road test track, and a deterministic hardware-chain test scene must be introduced later.
6. Existing generic Input System actions are not suitable as the final taxi control contract and may cause conflicts if extended without a dedicated action map and keyboard fallback policy.
7. The only checked-in build profile is Android/Quest while the confirmed target is Windows x86-64 PCVR over Quest Link. A Windows standalone profile, OpenXR runtime selection, architecture, graphics API, and packaging plan are gaps.
8. No project-owned content establishes seated wake-up, passenger/session state, GPS, requests, faults, scoring, results, or debug monitor; these remain later phases after the hardware gate.
9. The installed local Meta XR Operator proxy is evidence of tooling but not runtime verification. Unity must be open with the target project and in Play Mode before Operator tools can validate headset/controller behavior.
10. Review workload should be forecast before implementation. A complete base across scripts, scenes, prefabs, input, tests, and packaging is likely to exceed the 400-line review budget, so the configured auto-chain strategy should be applied as focused slices with chain strategy selected only when the forecast requires it.

## Recommended proposal boundary

Start with a narrowly verifiable hardware-chain slice: Windows/OpenXR project and scene bootstrap, seated XR origin plus controller-animated hands, a graybox cockpit with wheel and forward/reverse shifter, keyboard fallback, and test seams. Add the foot protocol as a separately testable pure boundary with a mocked UDP source, then connect it to pedal/brake behavior and only then enable a minimal drivable taxi. Defer passenger, GPS, requests, faults, scoring, and results until headset-to-driving acceptance evidence exists.

## Evidence paths

- `ProjectSettings/ProjectVersion.txt`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `ProjectSettings/EditorBuildSettings.asset`
- `ProjectSettings/BuildProfileUtilityOpenXR.asset`
- `ProjectSettings/XRSettings.asset`
- `ProjectSettings/ProjectSettings.asset`
- `Assets/Scenes/SampleScene.unity`
- `Assets/InputSystem_Actions.inputactions`
- `Assets/Oculus/OculusProjectConfig.asset`
- `Assets/Settings/Build Profiles/Meta Quest.asset`
- `Assets/ThirdParty/Vehicles/Models/Taxi_Full.fbx`
- `Assets/ThirdParty/Vehicles/Models/Taxi_Full.fbx.meta`
- `Assets/ThirdParty/DowntownCity/Models/`
- `Assets/ThirdParty/DowntownCity/Textures/`
- `Assets/ThirdParty/UniversalCharacters/`
- `Assets/ThirdParty/UniversalAnimations/`
