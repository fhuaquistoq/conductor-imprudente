"""Diagnostico de camaras de red: medir un stream y sondear las URLs habituales.

Sin red ni camara real: el sondeo TCP, el prober y el reloj se inyectan.
"""

from __future__ import annotations

import importlib.util
import io
import pathlib

import numpy as np
import pytest

from foottracker.probe import KNOWN_STREAMS, CandidateReport, StreamProbe, lan_candidates, probe_stream, scan_lan

ROOT = pathlib.Path(__file__).resolve().parents[1]
FRAME = np.full((8, 6, 3), 3, np.uint8)


def load_tool():
    spec = importlib.util.spec_from_file_location("probe_camera", ROOT / "tools" / "probe_camera.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class StepClock:
    """Avanza un paso fijo en cada lectura: mide sin dormir."""

    def __init__(self, step=0.1):
        self.now = 0.0
        self.step = step

    def __call__(self):
        self.now += self.step
        return self.now


class FakeCapture:
    def __init__(self, frames=(), *, opened=True):
        self.frames = list(frames)
        self.opened = opened
        self.settings = {}
        self.closed = False

    def isOpened(self):
        return self.opened and not self.closed

    def set(self, prop, value):
        self.settings[prop] = value
        return True

    def read(self):
        if self.closed or not self.frames:
            return False, None
        return True, self.frames.pop(0)

    def release(self):
        self.closed = True
        return None


def factory_of(capture):
    return lambda *args: capture


def test_probe_reports_resolution_and_measured_fps():
    capture = FakeCapture([FRAME] * 20)
    report = probe_stream("http://cam/video", seconds=0.5, factory=factory_of(capture), clock=StepClock(0.1))
    assert report.opened
    assert (report.height, report.width) == (8, 6)
    assert report.frames == 4
    assert report.seconds == pytest.approx(0.5)
    assert report.measured_fps == pytest.approx(8.0)
    assert "6x8" in report.describe()
    assert capture.closed, "la sonda siempre cierra la camara"


def test_probe_reports_an_unreachable_source_without_raising():
    report = probe_stream("http://cam/video", factory=factory_of(FakeCapture(opened=False)), clock=StepClock(0.1))
    assert not report.opened
    assert "no se pudo abrir" in report.error
    assert "sin imagen" in report.describe()


def test_probe_swallows_capture_errors():
    def boom(*args):
        raise RuntimeError("ffmpeg exploto")

    report = probe_stream("http://cam/video", factory=boom)
    assert not report.opened
    assert "ffmpeg exploto" in report.error


def test_probe_reports_an_open_stream_that_never_delivers_a_frame():
    report = probe_stream("http://cam/video", factory=factory_of(FakeCapture()), clock=StepClock(0.1))
    assert not report.opened
    assert "no entrego ningun frame" in report.error


def test_lan_candidates_cover_the_known_camera_apps():
    urls = lan_candidates("192.168.0.5")
    assert len(urls) == len(KNOWN_STREAMS)
    assert "http://192.168.0.5:4747/video" in urls, "DroidCam"
    assert "http://192.168.0.5:8080/video" in urls, "IP Webcam y CamDroid"
    assert "rtsp://192.168.0.5:8554/live" in urls
    assert all("192.168.0.5" in url for url in urls)


def test_scan_lan_probes_only_the_candidates_that_answer():
    probed = []

    def connect(host, port, timeout):
        return port == 8080

    def prober(url, seconds):
        probed.append(url)
        return StreamProbe(True, 640, 480, 10, seconds, 5.0)

    reports = scan_lan("192.168.0.5", connect=connect, prober=prober)
    assert [report.url for report in reports] == lan_candidates("192.168.0.5")
    assert sorted(probed) == sorted(report.url for report in reports if report.port == 8080)
    assert [report.url for report in reports if report.usable] == [
        report.url for report in reports if report.port == 8080
    ]
    assert all("640x480" in report.describe() for report in reports if report.usable)
    assert all("sin respuesta" in report.describe() for report in reports if not report.reachable)


def test_scan_lan_reports_a_port_that_answers_without_a_stream():
    reports = scan_lan(
        "192.168.0.5",
        connect=lambda host, port, timeout: True,
        prober=lambda url, seconds: StreamProbe(False, error="codec desconocido"),
    )
    assert reports
    assert all(report.reachable and not report.usable for report in reports)
    assert all("codec desconocido" in report.describe() for report in reports)


def test_scan_lan_treats_a_refused_connection_as_unreachable():
    def refused(host, port, timeout):
        raise OSError("connection refused")

    reports = scan_lan("192.168.0.5", connect=refused, prober=lambda *args, **kwargs: pytest.fail("no debe sondear"))
    assert reports
    assert all(not report.reachable and report.probe is None for report in reports)


def test_probe_camera_tool_suggests_the_usable_url(monkeypatch):
    tool = load_tool()
    monkeypatch.setattr(
        tool,
        "scan_lan",
        lambda *args, **kwargs: [
            CandidateReport("http://192.168.0.5:4747/video", 4747),
            CandidateReport("http://192.168.0.5:8080/video", 8080, True, StreamProbe(True, 640, 480, 10, 2.0, 5.0)),
        ],
    )
    stdout = io.StringIO()
    assert tool.main(["--ip", "192.168.0.5"], stdout=stdout) == 0
    assert "--camera http://192.168.0.5:8080/video" in stdout.getvalue()
    assert "sin respuesta" in stdout.getvalue()


def test_probe_camera_tool_explains_a_scan_without_streams(monkeypatch):
    tool = load_tool()
    monkeypatch.setattr(tool, "scan_lan", lambda *args, **kwargs: [CandidateReport("http://192.168.0.5:4747/video", 4747)])
    stdout = io.StringIO()
    assert tool.main(["--ip", "192.168.0.5"], stdout=stdout) == 1
    assert "sin stream" in stdout.getvalue()
