"""Calibracion de los marcadores a partir de fotos tomadas con la webcam.

El usuario coloca el marcador dentro del recuadro guia, pulsa una tecla y el programa
guarda la foto, estima el rango HSV del color y anota donde cae el marcador apoyado en el
suelo. Con el lado real del marcador (por defecto 6 cm) se obtiene la escala pixeles->cm
que despues permite informar la altura del pie.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, replace
from datetime import datetime
from pathlib import Path

import cv2
import numpy as np

from .classifier import GREEN, RED, MarkerSettings, mask_of

HUE_MAX = 179
HUE_PERIOD = 180.0
DEFAULT_MARKER_CM = 6.0
DEFAULT_GUIDE_RATIO = 0.5

PIXEL_SAT_FLOOR = 40
PIXEL_VAL_FLOOR = 40
HUE_TOLERANCE = 25.0
SPAN_MARGIN = 4.0
MIN_SPAN = 4.0
MIN_PIXELS = 30

SAT_MIN_FLOOR, SAT_MIN_CEIL = 40, 140
VAL_MIN_FLOOR, VAL_MIN_CEIL = 30, 140

BASE_SETTINGS: dict[str, MarkerSettings] = {"red": RED, "green": GREEN}


@dataclass(frozen=True)
class ColorRange:
    """Rango HSV aprendido de las fotos. Un unico tramo de tono o dos si cruza el 0."""

    hue_low: int
    hue_high: int
    hue_wrap_low: int | None
    hue_wrap_high: int | None
    saturation_min: int
    value_min: int

    def intervals(self) -> list[tuple[float, float]]:
        spans = [(float(self.hue_low), float(self.hue_high))]
        if self.hue_wrap_low is not None and self.hue_wrap_high is not None:
            spans.append((float(self.hue_wrap_low), float(self.hue_wrap_high)))
        return spans

    def center_span(self) -> tuple[float, float]:
        centers: list[float] = []
        weights: list[float] = []
        endpoints: list[float] = []
        for low, high in self.intervals():
            centers.append((low + high) / 2.0)
            weights.append(max(high - low, 1.0))
            endpoints.extend((low, high))
        center = _circular_center_weighted(centers, weights)
        span = max(abs(_circular_delta(endpoint, center)) for endpoint in endpoints)
        return center, span

    def apply(self, settings: MarkerSettings) -> MarkerSettings:
        return replace(
            settings,
            hue_low=int(self.hue_low),
            hue_high=int(self.hue_high),
            hue_wrap_low=None if self.hue_wrap_low is None else int(self.hue_wrap_low),
            hue_wrap_high=None if self.hue_wrap_high is None else int(self.hue_wrap_high),
            saturation_min=int(self.saturation_min),
            value_min=int(self.value_min),
        )

    def to_dict(self) -> dict:
        return {
            "hueLow": int(self.hue_low),
            "hueHigh": int(self.hue_high),
            "hueWrapLow": None if self.hue_wrap_low is None else int(self.hue_wrap_low),
            "hueWrapHigh": None if self.hue_wrap_high is None else int(self.hue_wrap_high),
            "saturationMin": int(self.saturation_min),
            "valueMin": int(self.value_min),
        }

    @classmethod
    def from_dict(cls, raw: dict) -> "ColorRange":
        if not isinstance(raw, dict):
            raise ValueError("rango de color invalido")
        wrap_low = raw.get("hueWrapLow")
        wrap_high = raw.get("hueWrapHigh")
        return cls(
            hue_low=int(raw["hueLow"]),
            hue_high=int(raw["hueHigh"]),
            hue_wrap_low=None if wrap_low is None else int(wrap_low),
            hue_wrap_high=None if wrap_high is None else int(wrap_high),
            saturation_min=int(raw["saturationMin"]),
            value_min=int(raw["valueMin"]),
        )

    @classmethod
    def from_center(cls, center: float, span: float, saturation_min: int, value_min: int) -> "ColorRange":
        """Reparte `center ± span` en uno o dos tramos sobre el circulo de tono 0..179."""

        span = float(min(max(span, MIN_SPAN), HUE_PERIOD / 2.0))
        low = center - span
        high = center + span
        if low < 0:
            first = (0, int(round(high)))
            second = (int(round(low + HUE_PERIOD)), HUE_MAX)
        elif high > HUE_MAX:
            first = (int(round(low)), HUE_MAX)
            second = (0, int(round(high - HUE_PERIOD)))
        else:
            first = (int(round(low)), int(round(high)))
            second = None
        first = (max(first[0], 0), min(first[1], HUE_MAX))
        if second is not None:
            second = (max(second[0], 0), min(second[1], HUE_MAX))
        return cls(
            hue_low=first[0],
            hue_high=first[1],
            hue_wrap_low=None if second is None else second[0],
            hue_wrap_high=None if second is None else second[1],
            saturation_min=int(saturation_min),
            value_min=int(value_min),
        )


def _circular_delta(value: float, center: float) -> float:
    """Diferencia con signo mas corta sobre el circulo de tono."""

    return ((value - center + HUE_PERIOD / 2.0) % HUE_PERIOD) - HUE_PERIOD / 2.0


def _circular_distance(hue: np.ndarray, center: float) -> np.ndarray:
    return np.abs(_circular_delta(hue, center))


def _circular_center_weighted(values: list[float] | np.ndarray, weights: list[float] | np.ndarray) -> float:
    angles = np.asarray(values, dtype=np.float64) * (2.0 * np.pi / HUE_PERIOD)
    weights = np.asarray(weights, dtype=np.float64)
    cosine = float((np.cos(angles) * weights).sum())
    sine = float((np.sin(angles) * weights).sum())
    if cosine == 0.0 and sine == 0.0:
        return 0.0
    return float((np.arctan2(sine, cosine) / (2.0 * np.pi) * HUE_PERIOD) % HUE_PERIOD)


def _circular_center(hue: np.ndarray) -> float:
    return _circular_center_weighted(hue, np.ones_like(hue))


def estimate_range(
    roi_bgr: np.ndarray,
    *,
    sat_floor: int = PIXEL_SAT_FLOOR,
    val_floor: int = PIXEL_VAL_FLOOR,
    hue_tolerance: float = HUE_TOLERANCE,
    min_pixels: int = MIN_PIXELS,
) -> ColorRange | None:
    """Rango HSV del color dominante dentro del recuadro. None si no hay color suficiente."""

    if roi_bgr is None or getattr(roi_bgr, "size", 0) == 0:
        return None
    hsv = cv2.cvtColor(roi_bgr, cv2.COLOR_BGR2HSV)
    hue = hsv[..., 0].reshape(-1).astype(np.float64)
    saturation = hsv[..., 1].reshape(-1).astype(np.float64)
    value = hsv[..., 2].reshape(-1).astype(np.float64)
    colorful = (saturation >= sat_floor) & (value >= val_floor)
    if int(colorful.sum()) < min_pixels:
        return None
    hue, saturation, value = hue[colorful], saturation[colorful], value[colorful]

    histogram, _ = np.histogram(hue, bins=int(HUE_PERIOD), range=(0.0, HUE_PERIOD), weights=saturation)
    peak = float(np.argmax(histogram)) + 0.5
    near = _circular_distance(hue, peak) <= hue_tolerance
    if int(near.sum()) >= min_pixels:
        hue, saturation, value = hue[near], saturation[near], value[near]
    center = _circular_center(hue)
    span = float(np.percentile(_circular_distance(hue, center), 95)) + SPAN_MARGIN
    sat_min = int(np.clip(np.percentile(saturation, 5), SAT_MIN_FLOOR, SAT_MIN_CEIL))
    val_min = int(np.clip(np.percentile(value, 5), VAL_MIN_FLOOR, VAL_MIN_CEIL))
    return ColorRange.from_center(center, span, sat_min, val_min)


def merge_ranges(ranges: list[ColorRange]) -> ColorRange | None:
    """Une varios rangos en uno: tono envolvente, saturacion y valor mas permisivos."""

    ranges = [item for item in ranges if item is not None]
    if not ranges:
        return None
    if len(ranges) == 1:
        return ranges[0]
    centers: list[float] = []
    weights: list[float] = []
    endpoints: list[float] = []
    for item in ranges:
        for low, high in item.intervals():
            centers.append((low + high) / 2.0)
            weights.append(max(high - low, 1.0))
            endpoints.extend((low, high))
    center = _circular_center_weighted(centers, weights)
    span = max(abs(_circular_delta(endpoint, center)) for endpoint in endpoints) + SPAN_MARGIN
    sat_min = min(item.saturation_min for item in ranges)
    val_min = min(item.value_min for item in ranges)
    return ColorRange.from_center(center, span, sat_min, val_min)


def _settings_intervals(settings: MarkerSettings) -> list[tuple[float, float]]:
    spans = [(float(settings.hue_low), float(settings.hue_high))]
    if settings.hue_wrap_low is not None and settings.hue_wrap_high is not None:
        spans.append((float(settings.hue_wrap_low), float(settings.hue_wrap_high)))
    return spans


def matches_family(center: float, settings: MarkerSettings, tolerance: float = HUE_TOLERANCE) -> bool:
    """True si el tono aprendido pertenece al marcador esperado.

    Evita que pulsar R con el marcador verde delante aprenda el verde como si fuera el rojo.
    """

    for low, high in _settings_intervals(settings):
        span = high - low
        middle = (low + high) / 2.0
        distance = max(abs(_circular_delta(middle, center)) - span / 2.0, 0.0)
        if distance <= tolerance:
            return True
    return False


def guide_rect(shape: tuple[int, ...], ratio: float = DEFAULT_GUIDE_RATIO) -> tuple[int, int, int, int]:
    """Recuadro guia centrado donde el usuario debe encuadrar el marcador."""

    height, width = int(shape[0]), int(shape[1])
    side = max(int(min(height, width) * ratio), 2)
    half = side // 2
    center_x, center_y = width // 2, height // 2
    return (center_x - half, center_y - half, center_x + half, center_y + half)


@dataclass(frozen=True)
class Sample:
    """Una foto procesada: color aprendido y posicion del marcador apoyado en el suelo."""

    kind: str
    color_range: ColorRange
    floor_y: float
    side_px: float


@dataclass(frozen=True)
class MarkerCalibration:
    color_range: ColorRange
    floor_y: float
    side_px: float
    samples: int = 1

    def to_dict(self) -> dict:
        return {
            "range": self.color_range.to_dict(),
            "floorY": round(float(self.floor_y), 2),
            "sidePx": round(float(self.side_px), 2),
            "samples": int(self.samples),
        }

    @classmethod
    def from_dict(cls, raw: dict) -> "MarkerCalibration":
        if not isinstance(raw, dict):
            raise ValueError("calibracion de marcador invalida")
        return cls(
            color_range=ColorRange.from_dict(raw["range"]),
            floor_y=float(raw["floorY"]),
            side_px=float(raw["sidePx"]),
            samples=int(raw.get("samples", 1)),
        )


@dataclass(frozen=True)
class CalibrationProfile:
    """Perfil completo: rango y suelo por marcador, mas el lado real del marcador."""

    red: MarkerCalibration | None = None
    green: MarkerCalibration | None = None
    marker_cm: float = DEFAULT_MARKER_CM
    created: str = ""

    def marker(self, kind: str) -> MarkerCalibration | None:
        return self.red if kind == "red" else self.green

    def settings_for(self, kind: str, base: MarkerSettings) -> MarkerSettings:
        entry = self.marker(kind)
        return base if entry is None else entry.color_range.apply(base)

    def floor_for(self, kind: str) -> tuple[float | None, float | None]:
        entry = self.marker(kind)
        if entry is None:
            return (None, None)
        return (entry.floor_y, entry.side_px)

    def to_dict(self) -> dict:
        return {
            "version": 1,
            "markerCm": round(float(self.marker_cm), 2),
            "created": self.created,
            "red": None if self.red is None else self.red.to_dict(),
            "green": None if self.green is None else self.green.to_dict(),
        }

    @classmethod
    def from_dict(cls, raw: dict) -> "CalibrationProfile":
        if not isinstance(raw, dict):
            raise ValueError("perfil de calibracion invalido")
        marker_cm = float(raw.get("markerCm", DEFAULT_MARKER_CM))
        if marker_cm <= 0:
            raise ValueError("markerCm debe ser positivo")
        red = raw.get("red")
        green = raw.get("green")
        return cls(
            red=None if red is None else MarkerCalibration.from_dict(red),
            green=None if green is None else MarkerCalibration.from_dict(green),
            marker_cm=marker_cm,
            created=str(raw.get("created", "")),
        )

    def save(self, path: str | Path) -> Path:
        target = Path(path)
        if target.parent and str(target.parent) not in ("", "."):
            target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(self.to_dict(), indent=2, ensure_ascii=False), encoding="utf-8")
        return target

    @classmethod
    def load(cls, path: str | Path) -> "CalibrationProfile":
        raw = json.loads(Path(path).read_text(encoding="utf-8"))
        return cls.from_dict(raw)


def stamped(marker_cm: float = DEFAULT_MARKER_CM, red: MarkerCalibration | None = None, green: MarkerCalibration | None = None) -> CalibrationProfile:
    """Perfil con la fecha de creacion ya puesta."""

    return CalibrationProfile(red=red, green=green, marker_cm=marker_cm, created=datetime.now().isoformat(timespec="seconds"))


@dataclass
class SampleCollector:
    """Acumula fotos de cada marcador y las reduce a un perfil."""

    marker_cm: float = DEFAULT_MARKER_CM
    samples: dict[str, list[Sample]] = None  # type: ignore[assignment]

    def __post_init__(self) -> None:
        if self.samples is None:
            self.samples = {"red": [], "green": []}

    def count(self, kind: str) -> int:
        return len(self.samples.get(kind, []))

    def add(self, kind: str, frame_bgr: np.ndarray, roi: tuple[int, int, int, int] | None = None) -> Sample | None:
        """Procesa una foto: aprende el color del recuadro y localiza el marcador dentro."""

        if kind not in BASE_SETTINGS:
            raise ValueError(f"marcador desconocido: {kind!r}")
        if frame_bgr is None or getattr(frame_bgr, "size", 0) == 0:
            return None
        x0, y0, x1, y1 = roi if roi is not None else guide_rect(frame_bgr.shape)
        patch = frame_bgr[y0:y1, x0:x1]
        found_range = estimate_range(patch)
        if found_range is None:
            return None
        center, _ = found_range.center_span()
        if not matches_family(center, BASE_SETTINGS[kind]):
            return None
        settings = found_range.apply(BASE_SETTINGS[kind])
        mask = mask_of(cv2.cvtColor(patch, cv2.COLOR_BGR2HSV), settings)
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if not contours:
            return None
        contour = max(contours, key=cv2.contourArea)
        area = float(cv2.contourArea(contour))
        moments = cv2.moments(contour)
        if area <= 0 or moments["m00"] <= 0:
            return None
        sample = Sample(kind, found_range, float(y0 + moments["m01"] / moments["m00"]), float(np.sqrt(area)))
        self.samples.setdefault(kind, []).append(sample)
        return sample

    def build(self) -> CalibrationProfile | None:
        """Perfil con la mediana del suelo y del tamano, y los rangos unidos."""

        if not any(self.samples.values()):
            return None
        calibrations = {}
        for kind, entries in self.samples.items():
            if not entries:
                continue
            merged = merge_ranges([entry.color_range for entry in entries])
            if merged is None:
                continue
            floor_y = float(np.median([entry.floor_y for entry in entries]))
            side_px = float(np.median([entry.side_px for entry in entries]))
            calibrations[kind] = MarkerCalibration(merged, floor_y, side_px, len(entries))
        if not calibrations:
            return None
        return stamped(self.marker_cm, calibrations.get("red"), calibrations.get("green"))
