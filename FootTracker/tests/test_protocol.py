import json

import pytest

from foottracker.protocol import (
    PROTOCOL_VERSION,
    FootPacket,
    FootState,
    ProtocolError,
    Sequence,
    is_newer,
    packet,
)


def test_wire_strings_match_unity_contract():
    assert FootState.UP.value == "up"
    assert FootState.DOWN.value == "down"
    assert FootState.UNKNOWN.value == "unknown"


def test_defaults_match_unity():
    assert PROTOCOL_VERSION == 1
    assert packet(1, FootState.UP, FootState.DOWN).to_dict() == {
        "version": 1,
        "seq": 1,
        "red": "up",
        "green": "down",
        "redValid": True,
        "greenValid": True,
    }


def test_packet_is_compact_utf8_json():
    message = packet(7, FootState.DOWN, FootState.UNKNOWN, red_valid=True, green_valid=False)
    assert message.encode().decode("utf-8") == message.to_json()
    assert " " not in message.to_json()


def test_packet_round_trips():
    original = packet(42, FootState.UP, FootState.DOWN, red_valid=False, green_valid=True)
    restored = FootPacket.from_json(original.to_json())
    assert restored == original


@pytest.mark.parametrize(
    "payload",
    [
        "no es json",
        "[1, 2, 3]",
        "{}",
        '{"seq":1,"red":"up"}',
        '{"seq":"1","red":"up","green":"down"}',
        '{"seq":1,"red":"lado","green":"down"}',
        '{"version":99,"seq":1,"red":"up","green":"down"}',
    ],
)
def test_invalid_payloads_are_rejected(payload):
    with pytest.raises(ProtocolError):
        FootPacket.from_json(payload)


def test_parsed_json_keeps_validity_flags():
    restored = FootPacket.from_json(json.dumps({"seq": 3, "red": "up", "green": "down", "redValid": True, "greenValid": False}))
    assert restored.red_valid is True
    assert restored.green_valid is False


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
