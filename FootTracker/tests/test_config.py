from types import SimpleNamespace

import pytest

from foottracker.config import ConfigError, Profile, apply, build, load
from foottracker.protocol import DEFAULT_HOST


def test_desktop_mode_defaults_to_loopback():
    profile = build({"mode": "desktop"})
    assert profile.mode == "desktop"
    assert (profile.host, profile.port, profile.rate) == (DEFAULT_HOST, 5055, 30)


def test_quest_mode_needs_the_headset_address():
    with pytest.raises(ConfigError):
        build({"mode": "quest"})
    with pytest.raises(ConfigError):
        build({"mode": "quest", "target_host": "127.0.0.1"})
    with pytest.raises(ConfigError):
        build({"mode": "quest", "target_host": "localhost"})


def test_quest_mode_accepts_the_lan_address():
    profile = build({"mode": "quest", "target_host": "192.168.1.42", "target_port": 6000, "send_rate": 60, "camera_index": 1})
    assert profile.mode == "quest"
    assert (profile.host, profile.port, profile.rate, profile.camera) == ("192.168.1.42", 6000, 60, "1")


def test_camera_index_accepts_a_url():
    assert build({"camera_index": "http://192.168.0.5:8080/video"}).camera == "http://192.168.0.5:8080/video"


@pytest.mark.parametrize(
    "raw",
    [
        {"mode": "cloud"},
        {"target_port": 0},
        {"target_port": 70000},
        {"send_rate": 0},
        {"target_host": ""},
        {"camera_index": ""},
    ],
)
def test_invalid_profiles_are_rejected(raw):
    with pytest.raises(ConfigError):
        build(raw)


def test_command_line_wins_over_the_profile():
    args = SimpleNamespace(host="10.0.0.9", port=None, rate=None, camera=None)
    apply(args, Profile(mode="quest", host="192.168.1.42", port=6000, rate=60, camera="1"))
    assert args.host == "10.0.0.9", "lo que vino por linea de comandos manda"
    assert args.port == 6000
    assert args.rate == 60
    assert args.camera == "1"


def test_without_a_profile_the_unity_defaults_apply():
    args = SimpleNamespace(host=None, port=None, rate=None, camera=None)
    apply(args, None)
    assert (args.host, args.port, args.rate, args.camera) == ("127.0.0.1", 5055, 30, "0")


def test_a_missing_default_file_is_not_an_error(tmp_path):
    assert load(tmp_path / "config.toml") is None


def test_a_missing_explicit_file_is_an_error(tmp_path):
    with pytest.raises(ConfigError):
        load(tmp_path / "config.toml", required=True)


def test_reads_a_real_file(tmp_path):
    path = tmp_path / "config.toml"
    path.write_text('mode = "quest"\ntarget_host = "192.168.1.42"\n', encoding="utf-8")
    profile = load(path)
    assert profile.mode == "quest" and profile.host == "192.168.1.42"


def test_invalid_toml_is_reported(tmp_path):
    path = tmp_path / "config.toml"
    path.write_text("mode = quest\n", encoding="utf-8")
    with pytest.raises(ConfigError):
        load(path)
