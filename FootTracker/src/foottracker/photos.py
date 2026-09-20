"""Analisis por lotes de una carpeta de fotos: freno rojo y acelerador verde/azul.

Recorre las imagenes en orden, sigue cada marcador recordando donde estaba en la foto
anterior y decide si el pie pisa o no. La linea del suelo se aprende de la propia sesion:
el marcador apoyado es el que baja mas en la imagen, asi que se toma un percentil alto de
las posiciones observadas como suelo y el tamano del marcador en esa banda como escala.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np

from .classifier import GREEN, RED, MarkerResult, MarkerSample, MarkerTracker, candidates, select_candidate

IMAGE_SUFFIXES = (".png", ".jpg", ".jpeg", ".bmp", ".webp")
REST_PERCENTILE = 90.0
REST_BAND_PX = 10.0


@dataclass(frozen=True)
class PhotoRow:
    name: str
    red: MarkerResult
    green: MarkerResult
    red_sample: MarkerSample
    green_sample: MarkerSample


@dataclass(frozen=True)
class PhotoReport:
    folder: Path
    rows: list[PhotoRow]
    red_floor_y: float | None
    green_floor_y: float | None
    red_side_px: float | None
    green_side_px: float | None

    def counts(self, side: str) -> dict[str, int]:
        tally = {"down": 0, "up": 0, "unknown": 0}
        for row in self.rows:
            result = row.red if side == "red" else row.green
            tally[result.state.value] = tally.get(result.state.value, 0) + 1
        return tally

    def lines(self) -> list[str]:
        lines = [
            f"fotos {len(self.rows)}   suelo rojo {_px(self.red_floor_y)}   suelo verde {_px(self.green_floor_y)}",
            f"escala rojo {_scale(self.red_side_px)}   escala verde {_scale(self.green_side_px)}",
            "",
        ]
        for row in self.rows:
            lines.append(f"{row.name}   rojo {_cell(row.red)}   verde {_cell(row.green)}")
        lines.append("")
        red, green = self.counts("red"), self.counts("green")
        lines.append(f"resumen: rojo {_tally(red)}   verde {_tally(green)}")
        return lines


def _px(value: float | None) -> str:
    return "n/d" if value is None else f"y={value:.0f}"


def _scale(value: float | None) -> str:
    return "n/d" if not value else f"{value:.0f}px"


def _cell(result: MarkerResult) -> str:
    if not result.valid:
        return "sin datos"
    height = "" if result.height_cm is None else f" {result.height_cm:4.1f}cm"
    move = "" if result.delta_cm is None else f" ({result.delta_cm:+.1f})"
    return f"{result.state.value}{height}{move}"


def _tally(tally: dict[str, int]) -> str:
    return f"{tally.get('down', 0)} pisado, {tally.get('up', 0)} levantado, {tally.get('unknown', 0)} sin datos"


def _sort_key(path: Path):
    digits = "".join(character for character in path.stem if character.isdigit())
    return (0, int(digits), path.name) if digits else (1, 0, path.name)


def image_paths(folder: str | Path) -> list[Path]:
    """Imagenes de la carpeta ordenadas por el numero del nombre (foto_0002 antes que foto_0010)."""

    base = Path(folder)
    found = [path for path in base.iterdir() if path.is_file() and path.suffix.lower() in IMAGE_SUFFIXES]
    return sorted(found, key=_sort_key)


def track_samples(
    frames: list[np.ndarray | None],
    settings,
    previous: tuple[float, float] | None = None,
    max_jump_px: float = 200.0,
    relock_after: int = 4,
) -> list[MarkerSample]:
    """Sigue el marcador foto a foto eligiendo el candidato mas cercano al anterior.

    Si se pierde varios frames seguidos, se olvida la referencia para reengancharlo donde
    reaparezca (que es justo cuando la otra persona mueve el pie delante de la camara).
    """

    samples = []
    misses = 0
    for frame in frames:
        sample = select_candidate(candidates(frame, settings), previous, max_jump_px)
        if sample.valid:
            previous = (sample.centroid_x, sample.centroid_y)
            misses = 0
        else:
            misses += 1
            if misses >= relock_after:
                previous = None
                misses = 0
        samples.append(sample)
    return samples


def rest_reference(samples: list[MarkerSample]) -> tuple[float | None, float | None]:
    """Linea del suelo (percentil alto de y) y tamano del marcador apoyado (px)."""

    ys = [float(item.centroid_y) for item in samples if item.valid and item.centroid_y is not None]
    if not ys:
        return (None, None)
    floor_y = float(np.percentile(ys, REST_PERCENTILE))
    resting = [float(np.sqrt(item.area)) for item in samples if item.valid and item.centroid_y is not None and abs(item.centroid_y - floor_y) <= REST_BAND_PX]
    sides = resting or [float(np.sqrt(item.area)) for item in samples if item.valid]
    return (floor_y, float(np.median(sides)))


def analyze_folder(
    folder: str | Path,
    *,
    marker_cm: float = 6.0,
    lift_threshold: float = 18.0,
    debounce_frames: int = 1,
    max_jump_px: float = 200.0,
    delta_seconds: float = 1 / 30,
) -> PhotoReport:
    """Clasifica cada foto de la carpeta y devuelve el informe con suelo y escala aprendidos."""

    paths = image_paths(folder)
    frames = [cv2.imread(str(path)) for path in paths]
    red_samples = track_samples(frames, RED, max_jump_px=max_jump_px)
    green_samples = track_samples(frames, GREEN, max_jump_px=max_jump_px)
    red_floor, red_side = rest_reference(red_samples)
    green_floor, green_side = rest_reference(green_samples)

    def tracker(settings, floor_y, side_px):
        return MarkerTracker(
            settings,
            start_down_y=floor_y,
            marker_cm=marker_cm,
            ref_side_px=side_px,
            lift_threshold=lift_threshold,
            debounce_frames=debounce_frames,
            max_jump_px=max_jump_px,
        )

    red_tracker = tracker(RED, red_floor, red_side)
    green_tracker = tracker(GREEN, green_floor, green_side)
    rows = []
    for path, red_sample, green_sample in zip(paths, red_samples, green_samples):
        rows.append(
            PhotoRow(
                path.name,
                red_tracker.observe(red_sample, delta_seconds),
                green_tracker.observe(green_sample, delta_seconds),
                red_sample,
                green_sample,
            )
        )
    return PhotoReport(Path(folder), rows, red_floor, green_floor, red_side, green_side)


def annotate(frame: np.ndarray, row: PhotoRow):
    """Copia de la foto con el marcador seguido y el estado escrito, para revisar a ojo."""

    view = frame.copy()
    for result, sample, color, label in (
        (row.red, row.red_sample, (0, 0, 255), "ROJO freno"),
        (row.green, row.green_sample, (0, 255, 0), "VERDE/azul acelerador"),
    ):
        if sample.valid and sample.centroid_x is not None and sample.centroid_y is not None:
            cv2.circle(view, (int(sample.centroid_x), int(sample.centroid_y)), 8, color, 2)
            height = "" if result.height_cm is None else f" {result.height_cm:.1f}cm"
            cv2.putText(view, f"{label}: {result.state.value}{height}", (int(sample.centroid_x) - 60, int(sample.centroid_y) - 14), cv2.FONT_HERSHEY_SIMPLEX, .5, color, 2)
    return view


def write_annotated(report: PhotoReport, out_dir: str | Path) -> list[Path]:
    """Guarda una copia anotada de cada foto para revisar el resultado a ojo."""

    target = Path(out_dir)
    target.mkdir(parents=True, exist_ok=True)
    written = []
    for row in report.rows:
        frame = cv2.imread(str(report.folder / row.name))
        if frame is None:
            continue
        path = target / row.name
        cv2.imwrite(str(path), annotate(frame, row))
        written.append(path)
    return written
