# Cockpit Interaction Specification

## Purpose

Define seated Quest controller interaction and development fallback for the graybox taxi cockpit.

## Requirements

### Requirement: Seated tracked cockpit

The system MUST provide a seated XR origin, graybox cockpit, controller-driven virtual hands, interaction anchors, and a physical wheel and Forward/Reverse shifter in the main scene.

#### Scenario: Controller hands track and animate

- GIVEN a user seated in the configured cockpit origin with Quest controllers active
- WHEN either controller moves or changes pose
- THEN the corresponding virtual hand MUST follow and visibly animate that controller
- AND the hand MUST remain aligned to its interaction anchor within the accepted hardware validation procedure.
- AND this headset/controller result MUST be recorded as acceptance evidence, not claimed as an automated hardware test.

### Requirement: Direct wheel and shifter interaction

The system MUST support direct grab/poke interaction where practical, with the wheel preserving one-hand and two-hand continuity and the shifter exposing only Forward and Reverse driving selections.

#### Scenario: Wheel remains continuous across hand count changes

- GIVEN the wheel is idle or held by one controller
- WHEN a user grabs with one hand, adds a second hand, releases either hand, or releases both hands
- THEN steering MUST remain continuous without a discontinuous jump caused solely by the hand-count transition
- AND the wheel MUST report a normalized steering value in the range [-1, 1].

#### Scenario: Shifter constrains direction

- GIVEN the shifter is interactable
- WHEN the user selects a gear
- THEN the only driving direction states MUST be Forward, Reverse, or neutral during transition
- AND an invalid or ambiguous position MUST NOT select a driving direction
- AND the selected direction MUST be available through the same control seam used by the taxi.

### Requirement: Keyboard fallback excludes joystick driving

The system MUST provide a keyboard fallback for steering, direction, throttle, and brake development/recovery controls, while joystick axes or buttons MUST NOT drive the taxi.

#### Scenario: Keyboard drives through shared seams

- GIVEN controllers, wheel, and FootTracker input are unavailable
- WHEN the evaluator uses the documented keyboard fallback
- THEN the taxi control seams MUST receive steering, direction, throttle, and brake values equivalent to the supported fallback mapping.

#### Scenario: Joystick input cannot drive

- GIVEN an attached joystick or gamepad
- WHEN joystick axes or buttons change
- THEN no taxi steering, direction, throttle, or brake command MUST be produced from that device.
