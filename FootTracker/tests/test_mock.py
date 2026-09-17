"""Comprueba el contrato de cable entre el emisor y un receptor real."""

import importlib.util
import io
import pathlib
import socket
import threading
import time

from foottracker.protocol import FootPacket, FootState, is_newer

ROOT = pathlib.Path(__file__).resolve().parents[1]


def load_emitter():
    spec = importlib.util.spec_from_file_location("emit_mock", ROOT / "tools" / "emit_mock.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def collect(script: str, seconds: float):
    """Levanta un receptor y ejecuta el emisor de prueba contra el."""

    receiver = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    receiver.bind(("127.0.0.1", 0))
    receiver.settimeout(.4)
    port = receiver.getsockname()[1]
    emitter = load_emitter()
    thread = threading.Thread(
        target=emitter.main,
        args=(["--script", script, "--host", "127.0.0.1", "--port", str(port), "--rate", "60", "--limit", str(seconds)],),
        kwargs={"stdout": io.StringIO()},
        daemon=True,
    )
    thread.start()
    datagrams = []
    deadline = time.perf_counter() + seconds
    while time.perf_counter() < deadline:
        try:
            datagrams.append(receiver.recv(2048))
        except socket.timeout:
            continue
    thread.join(timeout=2)
    receiver.close()
    return datagrams


def test_emitter_produces_parseable_packets():
    datagrams = collect("truth-table", 1.0)
    assert datagrams
    packets = [FootPacket.from_json(raw.decode("utf-8")) for raw in datagrams]
    assert all(packet.version == 1 for packet in packets)
    assert len({packet.sequence for packet in packets}) == len(packets)


def test_emitter_sequence_is_monotonic():
    packets = [FootPacket.from_json(raw.decode("utf-8")) for raw in collect("drive", 0.8)]
    assert all(is_newer(previous.sequence, following.sequence) for previous, following in zip(packets, packets[1:]))


def test_truth_table_script_starts_with_both_feet_down_and_unarmed():
    packets = [FootPacket.from_json(raw.decode("utf-8")) for raw in collect("truth-table", 1.0)]
    assert packets
    assert all(packet.red is FootState.DOWN and packet.green is FootState.DOWN for packet in packets)


def test_loss_script_reports_unknown_with_invalid_flags():
    packets = [FootPacket.from_json(raw.decode("utf-8")) for raw in collect("loss", 2.4)]
    assert packets
    assert any(packet.red is FootState.DOWN and packet.red_valid for packet in packets)
    lost = [packet for packet in packets if packet.red is FootState.UNKNOWN]
    assert lost
    assert all(not packet.red_valid and not packet.green_valid for packet in lost)
