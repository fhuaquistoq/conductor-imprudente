import json

import pytest

from foottracker.protocol import (
    PROTOCOL_VERSION,
    FootPacket,
    FootState,
    Pedal,
    ProtocolError,
    Sequence,
    is_newer,
    packet,
    pedal_of_state,
)


def test_defaults_match_unity():
    assert PROTOCOL_VERSION == 2
    message = packet(
        1,
        Pedal(pressed=True, confidence=0.9, value=0.1),
        Pedal(pressed=False, confidence=0.8, value=0.9),
        timestamp=1789800123.22,
        calibrated=True,
    )
    assert message.to_dict() == {
        "version": 2,
        "sequence": 1,
        "timestamp": 1789800123.22,
        "calibrated": True,
        "brake": {"pressed": True, "confidence": 0.9, "value": 0.1},
        "accelerator": {"pressed": False, "confidence": 0.8, "value": 0.9},
    }


def test_packet_is_compact_utf8_json():
    message = packet(7, Pedal(True), Pedal(False))
    assert message.encode().decode("utf-8") == message.to_json()
    assert " " not in message.to_json()


def test_packet_round_trips():
    original = packet(42, Pedal(True, 0.5, 0.2), Pedal(False, 0.75, 0.8), timestamp=1789800123.22, calibrated=True)
    assert FootPacket.from_json(original.to_json()) == original


@pytest.mark.parametrize(
    "payload",
    [
        "no es json",
        "[1, 2, 3]",
        "{}",
        '{"version":2,"brake":{"pressed":true},"accelerator":{"pressed":false}}',
        '{"version":2,"sequence":"1","brake":{"pressed":true},"accelerator":{"pressed":false}}',
        '{"version":2,"sequence":1,"brake":"down","accelerator":{"pressed":false}}',
        '{"version":2,"sequence":1,"brake":{"pressed":"si"},"accelerator":{"pressed":false}}',
        '{"version":2,"sequence":1,"brake":{"pressed":true},"accelerator":{"pressed":false},"timestamp":"ayer"}',
        '{"version":1,"seq":1,"red":"up","green":"down","redValid":true,"greenValid":true}',
        '{"version":99,"sequence":1,"brake":{"pressed":true},"accelerator":{"pressed":false}}',
    ],
)
def test_invalid_payloads_are_rejected(payload):
    with pytest.raises(ProtocolError):
        FootPacket.from_json(payload)


def test_parsed_json_keeps_pedal_details():
    restored = FootPacket.from_json(
        json.dumps(
            {
                "version": 2,
                "sequence": 3,
                "timestamp": 12.5,
                "calibrated": True,
                "brake": {"pressed": False, "confidence": 0.25, "value": 0.9},
                "accelerator": {"pressed": True, "confidence": 0.75, "value": 0.1},
            }
        )
    )
    assert restored.brake == Pedal(False, 0.25, 0.9)
    assert restored.accelerator == Pedal(True, 0.75, 0.1)


def test_unknown_marker_never_counts_as_pressed():
    pedal = pedal_of_state(FootState.UNKNOWN, 1.0)
    assert pedal.pressed is False
    assert pedal.confidence == 0.0


def test_pedal_keeps_the_travel_the_classifier_measured():
    assert pedal_of_state(FootState.DOWN, 0.9, 0.05).value == pytest.approx(0.05)
    assert pedal_of_state(FootState.UP, 0.9, 0.9).value == pytest.approx(0.9)
    down = pedal_of_state(FootState.DOWN, 0.9)
    assert down.value == 0.0
    up = pedal_of_state(FootState.UP, 0.9)
    assert up.value == 1.0


def test_sequence_wraps_as_signed_32_bits():
    sequence = Sequence(2_147_483_646)
    assert [sequence.next() for _ in range(4)] == [2_147_483_646, 2_147_483_647, -2_147_483_648, -2_147_483_647]


def test_is_newer_matches_unity_half_range_rule():
    assert is_newer(0, 1)
    assert not is_newer(1, 0)
    assert not is_newer(5, 5)
    assert is_newer(2_147_483_647, -2_147_483_648)
    assert is_newer(2_147_483_645, -2_147_483_645)
    assert not is_newer(-2_147_483_645, 2_147_483_645)
