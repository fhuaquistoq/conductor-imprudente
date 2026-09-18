"""Fuente de video por Wi-Fi: timeouts de red, reconexion y frame mas reciente.

Todo se prueba con capturas falsas y un reloj inyectado, asi que no hace falta camara
ni red. `CaptureScript` sustituye a `cv2.VideoCapture` y `poll()` ejecuta una sola
iteracion del bucle de lectura, de modo que la reconexion se comprueba sin dormir.
"""

from __future__ import annotations

import threading
import time

import cv2
import numpy as np
import pytest

from foottracker.source import (
    CameraError,
    CameraSource,
    FrameBuffer,
    FrameReader,
    is_network,
    open_capture,
)

FRAME = np.full((4, 4, 3), 7, np.uint8)


class FakeClock:
    """Reloj manual: el test decide cuanto tiempo pasa."""

    def __init__(self, now=0.0):
        self.now = now

    def __call__(self):
        return self.now

    def advance(self, seconds):
        self.now += seconds


class FakeCapture:
    """Capture minimo con la interfaz que usa `source.py`."""

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
        frame = self.frames.pop(0)
        return (True, frame) if frame is not None else (False, None)

    def release(self):
        self.closed = True
        return None


class SteadyCapture(FakeCapture):
    """Siempre entrega el mismo frame: simula un stream continuo."""

    def read(self):
        if self.closed:
            return False, None
        return True, FRAME.copy()


class BlockingCapture(FakeCapture):
    """`read()` se queda bloqueado hasta que el test abre la puerta."""

    def __init__(self, gate):
        super().__init__()
        self.gate = gate

    def read(self):
        self.gate.wait(5.0)
        return False, None


class CaptureScript:
    """Fabrica de captures segun un guion de pasos; repite el ultimo paso."""

    def __init__(self, *steps):
        self.steps = list(steps)
        self.calls = []

    def __call__(self, *args):
        self.calls.append(args)
        step = self.steps.pop(0) if len(self.steps) > 1 else self.steps[0]
        return step()


def opened(*frames):
    return lambda: FakeCapture(frames)


def closed():
    return lambda: FakeCapture(opened=False)


def steady():
    return lambda: SteadyCapture()


def reader_for(script, clock, **options):
    options.setdefault("stale_seconds", 0.3)
    options.setdefault("reconnect_delay", 0.5)
    return FrameReader("http://192.168.0.5:8080/video", factory=script, clock=clock, **options)


def await_frame(source, timeout=2.0):
    deadline = time.perf_counter() + timeout
    while time.perf_counter() < deadline:
        frame = source.read()
        if frame is not None:
            return frame
        time.sleep(0.01)
    return None


def test_is_network_detects_stream_urls_and_not_indices():
    assert is_network("http://192.168.0.5:8080/video")
    assert is_network("RTSP://192.168.0.5:8554/live")
    assert not is_network(0)
    assert not is_network("0")
    assert not is_network("grabacion.mp4")


def test_network_source_opens_with_ffmpeg_and_bounded_timeouts():
    script = CaptureScript(opened())
    open_capture("http://192.168.0.5:8080/video", 5.0, script)
    url, backend, params = script.calls[0]
    assert url == "http://192.168.0.5:8080/video"
    assert backend == cv2.CAP_FFMPEG
    options = dict(zip(params[::2], params[1::2]))
    assert options[cv2.CAP_PROP_OPEN_TIMEOUT_MSEC] == 5000
    assert options[cv2.CAP_PROP_READ_TIMEOUT_MSEC] == 1000


def test_usb_source_keeps_the_automatic_backend():
    script = CaptureScript(opened())
    open_capture(0, 5.0, script)
    assert script.calls[0] == (0,)


def test_open_raises_when_the_camera_does_not_answer():
    source = CameraSource(0, factory=CaptureScript(closed()))
    with pytest.raises(CameraError):
        source.open()
    assert source.status().connected is False
    source.close()


def test_read_before_open_is_an_error():
    with pytest.raises(CameraError):
        CameraSource(0, factory=CaptureScript(opened())).read()


def test_frame_buffer_hands_out_copies_and_lets_them_expire():
    buffer = FrameBuffer()
    buffer.put(FRAME, 0.0)
    handed = buffer.take(0.3, 0.3)
    assert handed is not None and handed.any(), "el frame justo en el borde sigue fresco"
    handed[:] = 0
    assert buffer.take(0.3, 0.3).any(), "la copia no debe tocar el frame guardado"
    assert buffer.take(0.31, 0.3) is None, "un frame mas viejo que la ventana no es imagen"
    assert FrameBuffer().take(0.0, 0.3) is None, "sin frame no hay imagen"
    assert buffer.age(0.3) == pytest.approx(0.3)


def test_reader_publishes_the_latest_frame():
    clock = FakeClock()
    script = CaptureScript(opened(FRAME, FRAME))
    reader = reader_for(script, clock)
    reader.connect()
    assert reader.connected
    reader.poll()
    reader.poll()
    status = reader.status()
    assert status.connected and status.frames == 2
    assert status.last_frame_age == 0.0
    clock.advance(0.1)
    assert reader.status().last_frame_age == pytest.approx(0.1)
    reader.stop()


def test_reader_applies_the_requested_size_and_rate():
    script = CaptureScript(opened())
    reader = FrameReader("http://192.168.0.5:8080/video", width=1280, height=720, fps=15, factory=script, clock=FakeClock())
    reader.connect()
    capture = reader.capture
    assert capture.settings[cv2.CAP_PROP_FRAME_WIDTH] == 1280
    assert capture.settings[cv2.CAP_PROP_FRAME_HEIGHT] == 720
    assert capture.settings[cv2.CAP_PROP_FPS] == 15


def test_read_failure_drops_the_stream_and_counts_one_reconnect():
    clock = FakeClock()
    script = CaptureScript(opened(FRAME), closed())
    reader = reader_for(script, clock)
    reader.connect()
    reader.poll()
    assert reader.status().connected
    reader.poll()
    status = reader.status()
    assert status.connected is False
    assert status.reconnects == 1
    assert "sin senal" in status.message
    reader.stop()


def test_reconnect_waits_for_the_backoff_and_caps_it():
    clock = FakeClock()
    script = CaptureScript(opened(FRAME), closed())
    reader = reader_for(script, clock, reconnect_delay=0.5, max_reconnect_delay=5.0)
    reader.connect()
    reader.poll()
    reader.poll()
    assert reader.retry_delay == 0.5
    clock.advance(0.49)
    reader.poll()
    assert len(script.calls) == 1, "no debe reintentar antes de tiempo"
    clock.advance(0.02)
    reader.poll()
    assert len(script.calls) == 2
    assert reader.retry_delay == 1.0
    for _ in range(6):
        clock.advance(10.0)
        reader.poll()
    assert reader.retry_delay == 5.0, "el backoff crece pero nunca pasa del tope"
    reader.stop()


def test_reconnect_recovers_and_frames_flow_again():
    clock = FakeClock()
    script = CaptureScript(opened(FRAME), opened(FRAME), steady())
    reader = reader_for(script, clock)
    reader.connect()
    reader.poll()
    reader.poll()
    assert reader.status().reconnects == 1
    clock.advance(0.5)
    reader.poll()
    assert reader.connected is True
    assert reader.status().reconnects == 1, "recuperarse no cuenta como otra caida"
    reader.poll()
    assert reader.status().frames == 2
    reader.stop()


def test_stop_joins_within_its_timeout_while_a_read_is_blocked():
    gate = threading.Event()
    reader = reader_for(CaptureScript(lambda: BlockingCapture(gate)), FakeClock())
    reader.connect()
    reader.start()
    started = time.perf_counter()
    reader.stop(timeout=0.2)
    elapsed = time.perf_counter() - started
    assert elapsed < 1.5, "stop no debe esperar al read bloqueado"
    assert reader.connected is False
    gate.set()


def test_describe_reports_kind_and_state():
    assert CameraSource(1).describe() == "webcam 1 640x480@30"
    wifi = CameraSource("http://192.168.0.5:8080/video")
    assert "wifi" in wifi.describe()
    assert "http://192.168.0.5:8080/video" in wifi.describe()


def test_camera_read_expires_the_same_frame_instead_of_reclassifying_it():
    clock = FakeClock()
    reader = reader_for(CaptureScript(opened(FRAME)), clock)
    reader.connect()
    reader.poll()
    source = CameraSource("http://192.168.0.5:8080/video", clock=clock, stale_seconds=0.3, _reader=reader)
    assert source.read() is not None
    clock.advance(0.29)
    assert source.read() is not None, "un stream mas lento que el bucle puede repetir el ultimo frame"
    clock.advance(0.02)
    assert source.read() is None, "pasada la ventana ya no hay imagen que clasificar"
    reader.stop()


def test_camera_source_reads_through_the_reader_thread():
    source = CameraSource("http://192.168.0.5:8080/video", factory=CaptureScript(steady()), stale_seconds=1.0)
    source.open()
    try:
        frame = await_frame(source)
        assert frame is not None
        assert frame.shape == FRAME.shape
        status = source.status()
        assert status.connected and status.frames > 0 and status.reconnects == 0
        assert "conectada" in source.describe()
        frame[:] = 0
        assert source.read().any(), "read entrega una copia propia"
    finally:
        source.close()
    assert source.status().connected is False
