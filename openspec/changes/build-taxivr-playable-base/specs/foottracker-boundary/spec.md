# FootTracker Boundary Specification

## Purpose

Define the versioned, separately packageable UDP boundary and deterministic foot-state behavior.

## Requirements

### Requirement: Versioned UDP foot packet contract

The FootTracker adapter MUST accept a versioned UDP packet containing the packet version, sequence or ordering information, timestamp or freshness information, and Red and Green marker states. Marker states MUST classify as Up, Down, or Unknown.

#### Scenario: Valid packet parses

- GIVEN a packet with the supported version, valid fields, acceptable ordering, and fresh timestamp
- WHEN the packet is parsed
- THEN it MUST produce a validated Red/Green Up, Down, or Unknown state and packet metadata
- AND the parser MUST be independently testable without Unity or hardware.

#### Scenario: Unsupported or malformed packet is rejected

- GIVEN a packet with an unsupported version, malformed encoding, missing required field, invalid marker value, or invalid metadata
- WHEN the packet is parsed
- THEN it MUST be rejected without changing the last validated foot state
- AND the rejection MUST be diagnosable.

### Requirement: Separate adapter and Unity handoff

The FootTracker source/adapter MUST remain separately packageable from Unity. UDP receive MAY run in a background context, but Unity state application MUST occur on the main thread through a latest-validated-packet handoff.

#### Scenario: Latest packet is consumed on the main thread

- GIVEN multiple valid packets received while Unity is busy
- WHEN the main thread consumes foot input
- THEN it MUST consume the latest validated packet available at that point
- AND it MUST not mutate Unity objects from the receive context
- AND older superseded packets MAY be discarded.

#### Scenario: Shutdown is safe

- GIVEN the adapter is receiving or waiting for UDP data
- WHEN the owning lifecycle is disabled or destroyed
- THEN receive resources MUST stop and release without blocking indefinitely or producing post-shutdown Unity mutations.

### Requirement: Freshness, ordering, and loss are fail-safe

The system MUST detect stale packets, reject out-of-order packets according to the versioned ordering contract, and transition to safe neutral behavior after the configured loss timeout.

#### Scenario: Stale or out-of-order packet arrives

- GIVEN a validated packet has already been accepted
- WHEN a stale or lower-order packet arrives
- THEN the packet MUST be rejected or ignored
- AND the latest accepted state MUST remain unchanged.

#### Scenario: Tracker input is lost

- GIVEN no acceptable packet arrives within the configured loss timeout
- WHEN the timeout is detected
- THEN the effective foot command MUST become neutral
- AND the system MUST expose a footWarning diagnostic for loss/staleness.

### Requirement: Foot state arms exactly on the first Up

The foot state MUST start Unarmed when both markers are Down. Unknown MUST be neither Up nor Down. The state MUST transition to Armed only when the first accepted, debounced marker state contains an Up, and MUST NOT arm from Down or Unknown alone.

#### Scenario: Initial Down remains Unarmed

- GIVEN startup with Red Down and Green Down
- WHEN repeated valid Down packets are consumed
- THEN the effective state MUST remain Unarmed and neutral.

#### Scenario: First Up arms

- GIVEN the state is Unarmed
- WHEN the first accepted marker state contains Red Up or Green Up after debounce/hold requirements
- THEN the state MUST become Armed
- AND subsequent valid control mapping MAY produce throttle or brake commands.

#### Scenario: Unknown does not arm

- GIVEN the state is Unarmed
- WHEN either marker is Unknown, including both markers Unknown
- THEN the state MUST remain Unarmed and neutral.

### Requirement: Debounce and hold behavior are deterministic

The system MUST apply documented debounce and hold timing to marker transitions, and transient changes shorter than the configured debounce MUST NOT change the effective state.

#### Scenario: Transient marker noise is ignored

- GIVEN a stable effective marker state
- WHEN a marker briefly changes and returns before the debounce threshold
- THEN the effective state and arming status MUST remain unchanged.

### Requirement: Invalid input preserves safe state

Malformed, stale, out-of-order, and unknown input MUST NOT produce a driving command; prolonged invalidity or loss MUST result in neutral behavior.

#### Scenario: Invalid input during driving

- GIVEN an Armed state with a non-neutral command
- WHEN malformed, stale, out-of-order, or Unknown input is consumed
- THEN the current command MUST not be extended by invalid data
- AND the command MUST resolve to neutral according to the documented timeout/hold policy.
