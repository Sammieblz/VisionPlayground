"""Stall evidence must remain bounded and independent of camera transport."""

import json
import time

from codriversim.service_diagnostics import ProgressWatchdog


def test_stalled_stage_keeps_stack_evidence_once_per_stage(tmp_path):
    path = tmp_path / "stalls.log"
    probe = ProgressWatchdog(path, threshold=0.03, interval=0.01, limit=2)
    try:
        probe.mark("reply_camera", 3600)
        deadline = time.monotonic() + 3
        while not path.exists() and time.monotonic() < deadline:
            time.sleep(0.01)
        assert path.exists()
        time.sleep(0.1)
        text = path.read_text()
        assert text.count('"event": "service_stage_stalled"') == 1
        event = json.loads(text.splitlines()[0])
        assert event["stage"] == "reply_camera" and event["frame_id"] == 3600
        assert "test_stalled_stage_keeps_stack_evidence_once_per_stage" in text
        probe.mark("receive_camera")
        deadline = time.monotonic() + 3
        while path.read_text().count('"event":') < 2 and time.monotonic() < deadline:
            time.sleep(0.01)
        assert path.read_text().count('"event":') == 2
        probe.mark("third_stall")
        time.sleep(0.1)
        assert path.read_text().count('"event":') == 2
    finally:
        probe.close()
    assert not probe.thread.is_alive()


def test_unavailable_diagnostic_storage_does_not_block_progress(tmp_path):
    parent = tmp_path / "file_not_directory"
    parent.write_text("preserve")
    probe = ProgressWatchdog(parent / "stalls.log", threshold=0.01, interval=0.01)
    try:
        probe.mark("encode_camera", 12)
        time.sleep(0.05)
        probe.mark("reply_camera", 13)
        assert probe.state[1:] == ("reply_camera", 13)
    finally:
        probe.close()
    assert parent.read_text() == "preserve"
