# Windows PCVR Bootstrap Specification

## Purpose

Define the first executable acceptance boundary for TaxiVR on Windows x86-64 with Quest 2 PCVR through OpenXR.

## Requirements

### Requirement: Windows OpenXR executable bootstrap

The system MUST provide a build-enabled Windows x86-64 standalone configuration using the repository's OpenXR and Meta XR packages, a main scene, and a dedicated taxi input action map.

#### Scenario: Packaged executable launches

- GIVEN a supported Windows x86-64 machine with the required runtime and Quest Link configuration
- WHEN the packaged executable is launched
- THEN it MUST enter the TaxiVR main scene without a blank or sample-only composition
- AND runtime diagnostics MUST identify build, OpenXR, scene, and input initialization status.

#### Scenario: Quest view and head tracking are validated

- GIVEN a Quest 2 headset connected through Quest Link
- WHEN an evaluator launches the packaged executable and wears the headset
- THEN the evaluator MUST observe a stable stereoscopic cockpit view
- AND head movement MUST update the view without an unrecoverable tracking failure.
- AND the result MUST be recorded as hardware acceptance evidence rather than represented as an automated test claim.

### Requirement: Hardware-chain scope remains gated

The bootstrap MUST expose seams for later cockpit, foot, and driving integration without implementing passenger, GPS/routes, requests, fault management, scoring, or results systems.

#### Scenario: Deferred systems are unavailable

- GIVEN the Gate 0 executable and main scene
- WHEN the evaluator exercises the available bootstrap surface
- THEN passenger, GPS/routes, request, fault, scoring, and results behavior MUST NOT be required for acceptance or silently included as part of this change.
