"""Perfil de destino del FootTracker (`config.toml`).

Dos modos:

- `desktop`: el juego corre en el mismo PC que este programa, asi que el destino es loopback.
- `quest`: el juego corre independiente en el visor; `target_host` tiene que ser la IP del
  Quest en la LAN (misma red, aislamiento de clientes desactivado).

La linea de comandos siempre gana al fichero, y el fichero a los valores por defecto.
"""

from __future__ import annotations

import tomllib
from dataclasses import dataclass
from pathlib import Path

from .protocol import DEFAULT_HOST, DEFAULT_PORT, DEFAULT_RATE

MODES = ("desktop", "quest")
DEFAULT_CONFIG = "config.toml"


class ConfigError(ValueError):
    """El perfil de configuracion no es valido."""


@dataclass(frozen=True)
class Profile:
    mode: str = "desktop"
    host: str = DEFAULT_HOST
    port: int = DEFAULT_PORT
    rate: int = DEFAULT_RATE
    camera: str = "0"


def load(path: str | Path | None, required: bool = False) -> Profile | None:
    """Lee el perfil. Ausente y no exigido no es error: el FootTracker funciona sin fichero."""

    if not path:
        return None
    file = Path(path)
    if not file.exists():
        if required:
            raise ConfigError(f"No se encontro el perfil de configuracion {str(file)!r}.")
        return None
    try:
        raw = tomllib.loads(file.read_text(encoding="utf-8"))
    except tomllib.TOMLDecodeError as error:
        raise ConfigError(f"Perfil {str(file)!r} invalido: {error}") from error
    except OSError as error:
        raise ConfigError(f"No se pudo leer {str(file)!r}: {error}") from error
    return build(raw, file)


def build(raw: dict, source: str | Path = DEFAULT_CONFIG) -> Profile:
    mode = str(raw.get("mode", "desktop")).strip().lower()
    if mode not in MODES:
        raise ConfigError(f"{source}: mode debe ser uno de {', '.join(MODES)} (no {mode!r}).")

    host = raw.get("target_host", DEFAULT_HOST)
    if not isinstance(host, str) or not host.strip():
        raise ConfigError(f"{source}: target_host debe ser una cadena con la IP del destino.")
    host = host.strip()
    if mode == "quest" and host in (DEFAULT_HOST, "localhost"):
        raise ConfigError(
            f'{source}: en mode = "quest" target_host tiene que ser la IP del Quest en la LAN, '
            f"no {host!r} (ese es el propio visor)."
        )

    port = raw.get("target_port", DEFAULT_PORT)
    if isinstance(port, bool) or not isinstance(port, int) or not 1 <= port <= 65535:
        raise ConfigError(f"{source}: target_port debe ser un entero entre 1 y 65535.")

    rate = raw.get("send_rate", DEFAULT_RATE)
    if isinstance(rate, bool) or not isinstance(rate, int) or rate < 1:
        raise ConfigError(f"{source}: send_rate debe ser un entero positivo.")

    camera = raw.get("camera_index", 0)
    if isinstance(camera, bool) or not isinstance(camera, (int, str)) or not str(camera).strip():
        raise ConfigError(f"{source}: camera_index debe ser un indice, una URL o 'auto'.")

    return Profile(mode=mode, host=host, port=port, rate=rate, camera=str(camera).strip())


def apply(args, profile: Profile | None) -> None:
    """Rellena en `args` solo lo que no vino por linea de comandos."""

    args.host = args.host or (profile.host if profile else DEFAULT_HOST)
    args.port = args.port or (profile.port if profile else DEFAULT_PORT)
    args.rate = args.rate or (profile.rate if profile else DEFAULT_RATE)
    args.camera = args.camera or (profile.camera if profile else "0")
