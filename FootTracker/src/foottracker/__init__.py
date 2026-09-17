"""FootTracker: clasificacion de pedales por color para Taxi VR."""

from .protocol import (
    DEFAULT_HOST,
    DEFAULT_PORT,
    DEFAULT_RATE,
    PROTOCOL_VERSION,
    FootPacket,
    FootState,
    ProtocolError,
    Sequence,
    is_newer,
    packet,
)

__all__ = [
    "DEFAULT_HOST",
    "DEFAULT_PORT",
    "DEFAULT_RATE",
    "PROTOCOL_VERSION",
    "FootPacket",
    "FootState",
    "ProtocolError",
    "Sequence",
    "is_newer",
    "packet",
]
