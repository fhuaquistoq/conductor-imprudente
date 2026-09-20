"""Prueba el contrato leyendo el mismo fixture que el test C# FootTrackingTests.

Si C# y Python se desincronizaran, uno de los dos lados fallaria sobre el mismo fichero.
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from foottracker.protocol import PROTOCOL_VERSION, FootPacket, ProtocolError, is_newer

FIXTURE = Path(__file__).parent / "fixtures" / "protocol_samples.json"


def load() -> dict:
    return json.loads(FIXTURE.read_text(encoding="utf-8"))


def test_fixture_version_matches_protocol():
    assert load()["version"] == PROTOCOL_VERSION


@pytest.mark.parametrize("sample", load()["accepted"], ids=lambda s: s["json"])
def test_accepted_samples_parse(sample):
    packet = FootPacket.from_json(sample["json"])
    assert packet.sequence == sample["sequence"]
    assert packet.timestamp == pytest.approx(sample["timestamp"])
    assert packet.calibrated == sample["calibrated"]
    assert packet.brake.pressed == sample["brake"]["pressed"]
    assert packet.brake.confidence == pytest.approx(sample["brake"]["confidence"])
    assert packet.brake.value == pytest.approx(sample["brake"]["value"])
    assert packet.accelerator.pressed == sample["accelerator"]["pressed"]
    assert packet.accelerator.confidence == pytest.approx(sample["accelerator"]["confidence"])
    assert packet.accelerator.value == pytest.approx(sample["accelerator"]["value"])


@pytest.mark.parametrize("raw", load()["rejected"])
def test_rejected_samples_fail(raw):
    with pytest.raises(ProtocolError):
        FootPacket.from_json(raw)


@pytest.mark.parametrize("case", load()["newer"], ids=lambda c: f"{c['previous']}->{c['next']}")
def test_sequence_order_matches_csharp(case):
    assert is_newer(case["previous"], case["next"]) == case["expected"]
