# Taxi VR — SDD Project Context

## Intent
Build Taxi VR as a Windows PCVR Unity 6.3 LTS game targeting Meta Quest 2 over Quest Link. Touch controllers drive virtual hands; a separate Python/OpenCV foot tracker sends red/green foot states over localhost UDP.

## Delivery scope and order
The MVP hardware chain is first: Quest/OpenXR runtime, virtual hands, wheel, shifter, foot-tracker UDP integration, and basic driving. Passenger, GPS, requests, and fault systems follow only after that chain is stable. This initialization phase makes no gameplay changes.

## Repository baseline
- Unity editor: `6000.6.0f1`.
- Rendering/runtime: URP, Input System, OpenXR, Meta XR SDK packages.
- Relevant packages include `com.meta.xr.sdk.all` `205.0.0`, `com.unity.xr.openxr` `1.18.0`, and `com.unity.test-framework` `1.8.0`.
- Starter/sample content exists under `Assets/Scenes`, `Assets/TutorialInfo`, and `Assets/ThirdParty`; project-owned folders are under `Assets/_Project`.
- No project-owned scripts, test assemblies, or test files were discovered under `Assets` during initialization.
- Generated Unity state is present locally in `Library`, `Temp`, `Logs`, and `UserSettings`; `.gitignore` excludes `.atl` local runtime state.

## SDD and testing policy
OpenSpec and Engram are both active. OpenSpec configuration is in `openspec/config.yaml`; this context is the durable project context artifact. Strict TDD is enabled for future implementation: establish RED evidence, implement GREEN, triangulate behavior, then refactor with verification evidence. Tests should use Unity Test Framework EditMode or PlayMode runners.

## Review and delivery policy
Execution mode is automatic. Delivery strategy is auto-chain; chain strategy remains deferred until a change forecast requires it. The canonical review budget is 400 changed lines. Preserve human consent for authorization, security, destructive/publishing, ambiguous-scope, and exceptional-size decisions.

## Next recommended phase
Create the MVP proposal/spec/design/tasks, then implement the hardware chain incrementally with focused Unity tests and Quest Link validation checkpoints.
