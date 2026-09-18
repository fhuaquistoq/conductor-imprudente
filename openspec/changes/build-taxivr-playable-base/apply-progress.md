> **Estado (2026-09): superado.** Lo de abajo narra el bootstrap XR inicial y cita archivos que ya no existen
> (`BootstrapXrViewCompositionTests.cs`, `Assets/_Project/Scenes/Main.unity`, `TaxiVRCompositionRoot`,
> `OVRCameraRig`). La composición viva es `Assets/Main.unity` con `PlayableRoot` y `EndlessCity`. El estado real
> y la verificación reproducible viven en `PLAYABLE.md`.

# Apply progress — build-taxivr-playable-base

## Work Unit 0 — PR 1 bootstrap

Status: **blocked during verification**. The source slice is implemented, but no task checkbox is complete because the required Unity Test Runner GREEN gate and Windows build could not run while another Unity instance held the project and remained in assembly reload.

### TDD Cycle Evidence

| Task | Test file | Layer | Safety net | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|---|---|
| Bootstrap | `Assets/_Project/Tests/TaxiVR/EditMode/BootstrapBaselineTests.cs` | EditMode | N/A (new) | Written against absent Main/map/seams; runner disconnected on reload | Runtime/editor/test assemblies compile with Roslyn; Unity execution blocked | 6 cases cover Windows target, bindings, build/runtime identity, modes, recovery, and flags; execution blocked | Composition root, diagnostics, flags, and editor build seam isolated; compile clean |
| Runtime seam | `Assets/_Project/Tests/TaxiVR/PlayMode/BootstrapPlayModeTests.cs` | PlayMode | N/A (new) | Written before Main bootstrap execution | Compile clean; PlayMode execution blocked | Main scene/map/diagnostics assertions added | No further refactor required |

### Verification evidence

- Focused command: Unity `-runTests -testPlatform EditMode -testFilter TaxiVR.Tests.EditMode`.
- Result: **blocked**, exit 1: `It looks like another Unity instance is running with this project open.`
- Meta XR TestRunner MCP result: disconnected during assembly reload; the connected server did not recover.
- Fallback compile: runtime, editor, EditMode, and PlayMode assemblies compiled cleanly with Unity's Roslyn compiler response references.
- JSON validation: `TaxiVR.inputactions` and `TaxiVR.Windows.build.json` parsed successfully; grep found no `Joystick` or `Gamepad` binding.
- Windows development build/launch/diagnostic output: **not run** because the same Unity project lock blocks batch build and MCP is offline.
- Quest 2 view/head tracking: pending Work Unit 7; no hardware claim made.

### Files changed

`ProjectSettings/EditorBuildSettings.asset`; `Assets/_Project/Scenes/Main.unity`; bootstrap runtime/editor scripts and asmdefs under `Assets/_Project/Scripts/TaxiVR/`; TaxiVR input/build settings under `Assets/_Project/Settings/`; EditMode/PlayMode tests under `Assets/_Project/Tests/TaxiVR/`; generated metadata for those surfaces.

### Deviations and risks

- Unity 6 BuildProfile asset creation was not safely available through the connected API, so `TaxiVRBuildConfiguration` is the validated Windows x86-64 editor seam and `TaxiVR.Windows.build.json` is its declarative contract; no brittle BuildProfile YAML was hand-authored.
- Existing standalone OpenXR/Meta XR loader settings were validated by source inspection and left unchanged as required by the allowed edit surfaces.
- Authored source/config/test count is 334 added lines plus 4 build-settings churn lines; this progress artifact adds 45 lines. The generated 154-line Unity scene and 56 current metadata lines are reported separately from the 383/400 authored review budget.
- PR boundary: stacked-to-main PR 1, Work Unit 0 only. Rollback is all files listed above; no deferred gameplay system was added.

### Remaining Work Unit 0 tasks

- [ ] RED: add EditMode/bootstrap tests proving the required scene, TaxiVR action map, Windows target/profile, and missing-reference diagnostics are absent or invalid in the baseline; record the focused Unity Test Runner command/result. <!-- sdd-owner: implementation -->
- [ ] GREEN: create the Windows x86-64 build profile, make `Main.unity` the required scene, configure the existing OpenXR/Meta XR packages and runtime diagnostics, and add a `TaxiVR` action map containing only approved controller/interactor and keyboard bindings; prove joystick bindings are absent. <!-- sdd-owner: implementation -->
- [ ] TRIANGULATE: cover build identity, Unity version, active loader/runtime, scene name, action-map enablement, recovery diagnostics, development/release diagnostic modes, and deferred-system absence through EditMode/PlayMode seam tests. <!-- sdd-owner: implementation -->
- [ ] REFACTOR: isolate `TaxiVRCompositionRoot`/`RuntimeDiagnostics` and feature flags so later units can add cockpit, tracker, and driving without changing bootstrap behavior. <!-- sdd-owner: implementation -->
- [ ] Verify a Windows development build launches `Main.unity` and writes diagnostic JSONL/text output; record automated result separately from the human Quest 2 view/head-tracking checkpoint, which remains pending until Work Unit 7. <!-- sdd-owner: implementation -->

### Structured status consumed

Change `build-taxivr-playable-base`; authoritative apply state `ready`; action context `repo-local`; allowed workspace root matched; delivery `auto-chain` / `stacked-to-main`; current boundary PR 1 / Work Unit 0; no action-context warnings. Verification is now blocked by the live Unity editor/project lock, not by SDD dependency state.

## Authorized correction — work-unit-0-xr-view-fix

Status: **implemented on disk; live Unity validation blocked by unavailable Editor MCP relay**. This correction is limited to the missing seated XR view and visible bootstrap proof. No authority tokens were acquired/reset/settled, no packages/settings/deferred systems were changed, and no Windows build was retried.

### TDD Cycle Evidence

| Phase | Evidence | Result |
|---|---|---|
| RED | Added `BootstrapXrViewCompositionTests` before the scene correction. Baseline `Main.unity` inspection showed only `TaxiVR Composition Root` and `Bootstrap Camera`; no seated origin, Meta XR rig, geometry, or light. Focused Unity Test Runner request for `TaxiVR.Tests.EditMode.BootstrapXrViewCompositionTests.MainContainsTrackedCameraRigAndVisibleBootstrapGeometry` timed out; the relay then disconnected, so no Unity failure count is claimed. | Baseline defect reproduced by persisted scene inspection; runner result unavailable. |
| GREEN | Replaced `Bootstrap Camera` with a package prefab instance of verified `OVRCameraRig` (`126d619cf4daa52469682f85c1378b4a`) under `SeatOrigin`; added `Bootstrap Geometry` and `Bootstrap Light`; extended root reference validation and recovery text. | Persisted scene/source changes compile in the fallback C# build. |
| TRIANGULATE | EditMode and PlayMode composition assertions cover `SeatOrigin`, `OVRCameraRig/TrackingSpace/CenterEyeAnchor`, one enabled camera, one enabled listener, enabled renderer/light, and the preserved Main diagnostics root. | Assertions authored; Unity execution pending relay recovery. |
| REFACTOR | Kept `TaxiVRCompositionRoot` as the sole coordinator; view validation remains a small private reference seam and does not add cockpit/driving behavior. | No deferred-system implementation added. |

### Correction files and exact composition

- `Assets/_Project/Scenes/Main.unity`: package `OVRCameraRig` prefab under `SeatOrigin`; one active CenterEye camera/listener; visible cube `Bootstrap Geometry` at `(0, 1.6, 4)` with built-in default material and collider; directional `Bootstrap Light`; original `TaxiVR Composition Root` retained; `Bootstrap Camera` removed.
- `Assets/_Project/Scripts/TaxiVR/Bootstrap/TaxiVRCompositionRoot.cs`: startup references now require the seated rig, enabled CenterEye camera, visible geometry, and light.
- `Assets/_Project/Scripts/TaxiVR/Bootstrap/RuntimeDiagnostics.cs`: missing-view recovery instruction included.
- `Assets/_Project/Tests/TaxiVR/EditMode/BootstrapXrViewCompositionTests.cs`: focused saved-scene composition test.
- `Assets/_Project/Tests/TaxiVR/PlayMode/BootstrapPlayModeTests.cs`: runtime composition assertions added.
- `Assets/_Project/Tests/TaxiVR/EditMode/BootstrapXrViewCompositionTests.cs.meta`: generated metadata only.

### Verification evidence and limits

- Verified installed Meta XR source asset: `Library/PackageCache/com.meta.xr.sdk.core@c0efcbf2ba70/Prefabs/OVRCameraRig.prefab`; its hierarchy includes `OVRCameraRig/TrackingSpace/CenterEyeAnchor`, with CenterEye camera and AudioListener enabled and per-eye cameras disabled.
- Verified BuildingBlocks catalog before editing: `Camera Rig`, ID `e47682b9-c270-40b1-b16d-90b627a5ce1b`; `GetBlockInfo` reported installable and not installed. The BuildingBlocks install call was not attempted after the Editor relay auto-shutdown, so the persisted package prefab instance is the selected verified API path.
- Fallback compile commands: `dotnet restore TaxiVR.Tests.EditMode.csproj --ignore-failed-sources && dotnet build TaxiVR.Tests.EditMode.csproj --no-restore` and `dotnet restore TaxiVR.Tests.PlayMode.csproj --ignore-failed-sources && dotnet build TaxiVR.Tests.PlayMode.csproj --no-restore` — **both passed, 0 warnings, 0 errors**; subsequent `dotnet build` commands also passed after restore.
- Unity Test Runner focused execution: **not available** after `meta-xr-unity-runtime` returned `fetch failed`; the earlier cached run was 21 passed but predates this correction and is not used as correction evidence.
- Editor screenshot, hierarchy query, Play Mode diagnostics/hierarchy, and live stereoscopic view: **not obtained** because `meta-xr-unity-runtime` disconnected and its relay port was no longer listening. No Quest headset, head-tracking, or hardware acceptance claim is made.
- Windows development build/launch: **not run**, per narrow-correction instruction and existing separate build-crash record.

### Task and workload boundary

No original Work Unit 0 checkbox was marked complete: the five broad Work Unit 0 rows still require their full build/test/runtime evidence. The correction remains within the authorized PR 1 / Work Unit 0 review boundary and does not represent Work Unit 0 acceptance. Source correction authored additions are below the 400-line budget; generated metadata is excluded from that count.

Remaining implementation tasks are unchanged; exact unchecked Work Unit 0 lines remain in the preceding `Remaining Work Unit 0 tasks` section, and all later Work Unit 1–7 and completion-gate rows remain unchecked in `tasks.md`.
