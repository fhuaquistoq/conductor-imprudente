"""Contrato UDP entre FootTracker y TaxiVR.

Debe coincidir exactamente con Assets/_Project/Scripts/TaxiVR/Playable/FootProtocol.cs.

Version 2: cada pedal viaja como objeto (`pressed`, `confidence`, `value`) y el paquete lleva
la marca de tiempo del frame y si el clasificador esta calibrado, que es lo que permite medir
latencia y perdida en la vista debug del visor.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from enum import Enum
from typing import Any

PROTOCOL_VERSION = 2
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


def _number(value: Any, field: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ProtocolError(f"{field} debe ser numerico.")
    return float(value)


def _boolean(value: Any, field: str) -> bool:
    if not isinstance(value, bool):
        raise ProtocolError(f"{field} debe ser booleano.")
    return value


@dataclass(frozen=True)
class Pedal:
    """Un pedal: si esta pisado, con cuanta confianza se vio y cuanto se levanto (0 = pisado)."""

    pressed: bool
    confidence: float = 0.0
    value: float = 0.0

    def to_dict(self) -> dict[str, Any]:
        return {
            "pressed": bool(self.pressed),
            "confidence": round(float(self.confidence), 3),
            "value": round(float(self.value), 3),
        }

    @classmethod
    def from_dict(cls, raw: Any, field: str) -> "Pedal":
        if not isinstance(raw, dict):
            raise ProtocolError(f"{field} debe ser un objeto JSON.")
        if "pressed" not in raw:
            raise ProtocolError(f"{field}.pressed es obligatorio.")
        return cls(
            pressed=_boolean(raw["pressed"], f"{field}.pressed"),
            confidence=_number(raw.get("confidence", 0.0), f"{field}.confidence"),
            value=_number(raw.get("value", 0.0), f"{field}.value"),
        )


@dataclass(frozen=True)
class FootPacket:
    sequence: int
    brake: Pedal
    accelerator: Pedal
    timestamp: float = 0.0
    calibrated: bool = False
    version: int = PROTOCOL_VERSION

    def to_dict(self) -> dict[str, Any]:
        return {
            "version": int(self.version),
            "sequence": int(self.sequence),
            "timestamp": round(float(self.timestamp), 3),
            "calibrated": bool(self.calibrated),
            "brake": self.brake.to_dict(),
            "accelerator": self.accelerator.to_dict(),
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
        sequence = raw.get("sequence")
        if not isinstance(sequence, int) or isinstance(sequence, bool):
            raise ProtocolError("sequence debe ser entero.")
        if "brake" not in raw or "accelerator" not in raw:
            raise ProtocolError("Faltan brake o accelerator.")
        return cls(
            sequence=sequence,
            brake=Pedal.from_dict(raw["brake"], "brake"),
            accelerator=Pedal.from_dict(raw["accelerator"], "accelerator"),
            timestamp=_number(raw["timestamp"], "timestamp") if "timestamp" in raw else 0.0,
            calibrated=_boolean(raw["calibrated"], "calibrated") if "calibrated" in raw else False,
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


def pedal_of_state(state: FootState, confidence: float = 0.0, value: float | None = None) -> Pedal:
    """Traduce el resultado del clasificador al pedal que viaja por UDP.

    Un marcador perdido (`unknown`) va con confianza 0: "no se ve" nunca es "pisado".
    `value` es el recorrido normalizado que ya calcula el clasificador; si falta, se cae a
    un binario (0 pisado, 1 levantado) hasta que el HITO 3 lo haga analogico de verdad.
    """

    pressed = state is FootState.DOWN
    if value is None:
        value = 0.0 if pressed else 1.0
    return Pedal(pressed=pressed, confidence=0.0 if state is FootState.UNKNOWN else confidence, value=value)


def packet(sequence: int, brake: Pedal, accelerator: Pedal, timestamp: float = 0.0, calibrated: bool = False) -> FootPacket:
    return FootPacket(sequence=sequence, brake=brake, accelerator=accelerator, timestamp=timestamp, calibrated=calibrated)
