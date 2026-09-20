"""Deteccion de webcams conectadas y seleccion automatica de camara.

El sondeo se prueba con capturas falsas: no hace falta una webcam real.
"""

from __future__ import annotations

import io

import numpy as np
import pytest

import foottracker.cli as cli
from foottracker.cli import build_parser, build_source, list_cameras_mode, resolve_camera
from foottracker.source import CameraError, CameraInfo, first_available_camera, list_cameras, probe_camera

FRAME = np.full((480, 640, 3), 9, np.uint8)


class FakeCapture:
    def __init__(self, frame=None, *, opened=True):
        self.frame = frame
        self.opened = opened
        self.released = False

    def isOpened(self):
        return self.opened and not self.released

    def read(self):
        if self.frame is None:
            return False, None
        return True, self.frame

    def release(self):
        self.released = True
        return None


def factory_of(devices: dict[int, FakeCapture]):
    return lambda index, *args: devices.get(int(index), FakeCapture(opened=False))


def test_probe_camera_reports_resolution_and_releases():
    capture = FakeCapture(FRAME)
    info = probe_camera(2, factory_of({2: capture}))
    assert info == CameraInfo(2, True, 640, 480)
    assert capture.released


def test_probe_camera_never_raises():
    info = probe_camera(0, lambda *args: (_ for _ in ()).throw(RuntimeError("sin driver")))
    assert info == CameraInfo(0, False)


def test_list_cameras_stops_at_the_first_gap_after_a_camera():
    devices = {0: FakeCapture(opened=False), 1: FakeCapture(FRAME), 2: FakeCapture(opened=False)}
    infos = list_cameras(4, factory_of(devices))
    assert [info.index for info in infos] == [0, 1]
    assert [info.available for info in infos] == [False, True]


def test_first_available_camera_picks_the_lowest_index():
    devices = {0: FakeCapture(opened=False), 1: FakeCapture(FRAME), 2: FakeCapture(FRAME)}
    finder = lambda max_devices: list_cameras(max_devices, factory_of(devices))
    assert first_available_camera(4, finder=finder) == 1


def test_first_available_camera_is_none_without_devices():
    assert first_available_camera(3, finder=lambda max_devices: list_cameras(max_devices, factory_of({}))) is None


def test_resolve_camera_uses_the_first_available_for_auto(monkeypatch):
    monkeypatch.setattr(cli, "first_available_camera", lambda *args, **kwargs: 1)
    assert resolve_camera("auto") == 1
    assert resolve_camera("AUTO") == 1


def test_resolve_camera_fails_loudly_without_devices(monkeypatch):
    monkeypatch.setattr(cli, "first_available_camera", lambda *args, **kwargs: None)
    with pytest.raises(CameraError):
        resolve_camera("auto")


def test_build_source_resolves_auto_to_an_index(monkeypatch):
    monkeypatch.setattr(cli, "first_available_camera", lambda *args, **kwargs: 3)
    source = build_source(build_parser().parse_args(["--camera", "auto"]))
    assert source.source == 3


def test_list_cameras_mode_suggests_the_first_available(monkeypatch):
    monkeypatch.setattr(cli, "list_cameras", lambda *args, **kwargs: [CameraInfo(0, False), CameraInfo(1, True, 1280, 720)])
    stdout = io.StringIO()
    assert list_cameras_mode(stdout) == 0
    assert "1280x720" in stdout.getvalue()
    assert "--camera 1" in stdout.getvalue()


def test_list_cameras_mode_reports_when_nothing_answers(monkeypatch):
    monkeypatch.setattr(cli, "list_cameras", lambda *args, **kwargs: [CameraInfo(0, False)])
    stdout = io.StringIO()
    assert list_cameras_mode(stdout) == 1
    assert "sin senal" in stdout.getvalue()
