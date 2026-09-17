"""Contrato UDP entre FootTracker y TaxiVR.

Debe coincidir exactamente con Assets/_Project/Scripts/TaxiVR/Playable/FootProtocol.cs.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, replace
from enum import Enum
from typing import Any

PROTOCOL_VERSION = 1
DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 5055
DEFAULT_RATE = 30
INT32_MAX = 2_147_483_647
INT32_MIN = -2_147_483_648


class FootState(str, Enum):
    """Estado de un marcador. `Unknown` nunca equivale a `Up` ni a `Down`."""

    UP = "up"
    DOWN = "down"
    UNKNOWN = "unknown"


class ProtocolError(ValueError):
    """El texto recibido no cumple el contrato."""


@dataclass(frozen=True)
class FootPacket:
    sequence: int
    red: FootState
    green: FootState
    red_valid: bool = True
    green_valid: bool = True
    version: int = PROTOCOL_VERSION

    def to_dict(self) -> dict[str, Any]:
        return {
            "version": int(self.version),
            "seq": int(self.sequence),
            "red": FootState(self.red).value,
            "green": FootState(self.green).value,
            "redValid": bool(self.red_valid),
            "greenValid": bool(self.green_valid),
        }

    def to_json(self) -> str:
        return json.dumps(self.to_dict(), separators=(",", ":"))

    def encode(self) -> bytes:
        return self.to_json().encode("utf-8")

    @classmethod
    def from_json(cls, text: str) -> "FootPacket":
        try:
            raw = json.loads(text)
        except (TypeError, ValueError) as error:
            raise ProtocolError(f"JSON invalido: {error}") from error
        if not isinstance(raw, dict):
            raise ProtocolError("El mensaje debe ser un objeto JSON.")
        version = raw.get("version", PROTOCOL_VERSION)
        if not isinstance(version, int) or isinstance(version, bool):
            raise ProtocolError("version debe ser entero.")
        if version != PROTOCOL_VERSION:
            raise ProtocolError(f"Version no soportada: {version}.")
        sequence = raw.get("seq")
        if not isinstance(sequence, int) or isinstance(sequence, bool):
            raise ProtocolError("seq debe ser entero.")
        try:
            red = FootState(raw.get("red"))
            green = FootState(raw.get("green"))
        except ValueError as error:
            raise ProtocolError(f"Estado de marcador invalido: {error}") from error
        return cls(
            sequence=sequence,
            red=red,
            green=green,
            red_valid=bool(raw.get("redValid", False)),
            green_valid=bool(raw.get("greenValid", False)),
            version=version,
        )


@dataclass
class Sequence:
    """Contador de secuencia con envolvente de 32 bits con signo, igual que Unity."""

    value: int = 0

    def next(self) -> int:
        current = self.value
        following = current + 1
        self.value = INT32_MIN if following > INT32_MAX else following
        return current

    def peek(self) -> int:
        return self.value


def is_newer(previous: int, following: int) -> bool:
    """Misma semantica de mitad de rango que FootProtocol.IsNewer."""

    difference = (following - previous) & 0xFFFFFFFF
    return difference != 0 and difference < 0x80000000


def packet(sequence: int, red: FootState, green: FootState, red_valid: bool = True, green_valid: bool = True) -> FootPacket:
    return FootPacket(sequence=sequence, red=red, green=green, red_valid=red_valid, green_valid=green_valid)


def with_validity(source: FootPacket, red_valid: bool, green_valid: bool) -> FootPacket:
    return replace(source, red_valid=red_valid, green_valid=green_valid)
