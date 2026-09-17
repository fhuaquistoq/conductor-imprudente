# Driving MVP Specification

## Purpose

Define the minimum safe driving chain from wheel, shifter, foot state, and keyboard fallback to a deterministic drivable taxi route.

## Requirements

### Requirement: Validated foot state controls pedals safely

The system MUST map each validated Armed Red/Green Up/Down combination to the fixed throttle/brake truth table below, keep Unknown and unavailable input neutral, and prevent simultaneous pedal intent from creating an unsafe drive command.

| Armed marker state | Throttle | Brake |
|---|---:|---:|
| Green Up + Red Up | false | false |
| Green Down + Red Up | true | false |
| Green Up + Red Down | false | true |
| Green Down + Red Down | true | true |

#### Scenario: Armed Up/Up is neutral

- GIVEN the foot state is Armed with Green Up and Red Up
- WHEN the state is consumed
- THEN throttle MUST be false
- AND brake MUST be false.

#### Scenario: Armed Green Down drives throttle only

- GIVEN the foot state is Armed with Green Down and Red Up
- WHEN the state is consumed
- THEN throttle MUST be true
- AND brake MUST be false.

#### Scenario: Armed Red Down applies brake only

- GIVEN the foot state is Armed with Green Up and Red Down
- WHEN the state is consumed
- THEN throttle MUST be false
- AND brake MUST be true.

#### Scenario: Armed Green Down and Red Down applies both pedals

- GIVEN the foot state is Armed with Green Down and Red Down
- WHEN the state is consumed
- THEN throttle MUST be true
- AND brake MUST be true
- AND the dual-pedal skid condition MUST be active.

#### Scenario: Startup Down/Down remains neutral until arming

- GIVEN startup has Green Down and Red Down and the foot state is Unarmed
- WHEN that combination is consumed
- THEN throttle MUST be false
- AND brake MUST be false
- AND the foot state MUST remain Unarmed.

#### Scenario: First accepted Up arms and maps the resulting combination

- GIVEN startup has Green Down and Red Down while Unarmed
- WHEN the first accepted and debounced state is Red Down and Green Up
- THEN the foot state MUST become Armed
- AND throttle MUST be false
- AND brake MUST be true.

#### Scenario: Unknown or loss returns neutral

- GIVEN Unknown markers, invalidated input, or tracker loss
- WHEN the effective state is updated
- THEN throttle and brake MUST resolve to neutral/failsafe values
- AND the taxi MUST not continue accelerating from the last valid foot command.

### Requirement: Dual-pedal skid behavior is explicit

The driving model MUST expose skid behavior when throttle and brake are applied together above the configured dual-pedal condition, including a skidWarning diagnostic, and MUST clear that condition when the conflicting input ends.

#### Scenario: Dual pedal produces skid warning

- GIVEN the taxi is receiving throttle and brake above their configured thresholds at the same time
- WHEN the driving update is evaluated
- THEN skid behavior MUST be applied according to the MVP tuning contract
- AND skidWarning MUST be observable for diagnostics and acceptance evidence.

#### Scenario: Single pedal does not falsely skid

- GIVEN only throttle or only brake is above its active threshold
- WHEN the driving update is evaluated
- THEN the dual-pedal skid condition MUST remain clear.

### Requirement: Wheel, direction, and pedals drive a taxi

The system MUST connect normalized wheel steering, constrained Forward/Reverse direction, and throttle/brake controls to a basic taxi controller with required colliders, steering pivots, and interaction anchors.

#### Scenario: Taxi moves forward and reverses on a deterministic route

- GIVEN a valid direction selection, steering input, and applicable throttle or keyboard fallback
- WHEN the user drives the graybox taxi route
- THEN the taxi MUST move in the selected direction, respond to normalized steering, collide with route boundaries, and remain controllable through the route.

#### Scenario: Neutral prevents powered movement

- GIVEN neutral direction or neutral/failsafe pedal input
- WHEN the taxi controller updates
- THEN it MUST not apply powered forward or reverse movement.

### Requirement: Runtime acceptance is evidenced on hardware

The change MUST define repeatable runtime acceptance procedures and retain evidence for the Windows executable, Quest view/head tracking, tracked hands, direct wheel/shifter interaction, FootTracker input, pedal behavior, skid behavior, and route driving. Hardware evidence MUST complement pure logic tests and MUST NOT be represented as automated hardware coverage.

#### Scenario: Hardware-chain evidence is recorded

- GIVEN the packaged executable and available Quest, controllers, wheel, and FootTracker hardware
- WHEN an evaluator performs the documented Gate 0-3 procedure
- THEN each hardware result, configuration, build identity, and observed failure MUST be recorded as acceptance evidence
- AND the gate MUST remain unaccepted when required hardware evidence is absent.

### Requirement: Deferred product systems remain out of the MVP

The driving MVP MUST NOT require or implement passenger systems, GPS or routes as a product navigation system, request flows, fault management, scoring, or results; interfaces MAY exist only when required to keep the hardware chain replaceable and testable.

#### Scenario: Hardware chain can be evaluated without deferred systems

- GIVEN the minimal cockpit and deterministic test route
- WHEN the evaluator completes the driving acceptance procedure
- THEN no passenger, GPS/routes request, fault, scoring, or results state MUST be necessary to pass the hardware-chain acceptance.
