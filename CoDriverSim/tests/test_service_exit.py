"""A transport failure must leave actionable evidence without weakening ordering."""

import json
import socket
import struct
import threading
import time

from codriversim import service
from codriversim.contracts import Calibration, CameraFrame, EgoTelemetry


def test_reordered_stream_is_rejected_and_its_exit_reason_survives(tmp_path, monkeypatch):
    closed = threading.Event()

    class FakeObserver:
        def __init__(self, *args, **kwargs):
            self.frames = self.processed = 0

        def submit(self, frame):
            self.frames += 1
            return {"status": "transport fixture"}

        def close(self):
            closed.set()

    monkeypatch.setattr(service, "ObservationService", FakeObserver)
    ready = tmp_path / "observer.json"
    thread = threading.Thread(
        target=service.serve, args=(tmp_path / "unused.onnx", ready, tmp_path, "capability")
    )
    thread.start()
    deadline = time.monotonic() + 5
    while not ready.exists() and time.monotonic() < deadline:
        time.sleep(0.01)
    assert ready.exists()
    port = json.loads(ready.read_text())["port"]
    frame = CameraFrame(
        session_id="stream",
        frame_id=7,
        timestamp=1,
        width=640,
        height=400,
        calibration=Calibration(300, 300, 320, 200),
        ego=EgoTelemetry(),
        image=b"jpeg",
    )
    header = json.dumps(frame.metadata() | {"token": "capability"}).encode()
    packet = struct.pack("<II", len(header), len(frame.image)) + header + frame.image
    with socket.create_connection(("127.0.0.1", port), timeout=5) as client:
        client.sendall(packet)
        size = struct.unpack("<I", service.receive(client, 4))[0]
        result = json.loads(service.receive(client, size))
        assert result["status"] == "transport fixture"
        assert service.receive(client, result["image_bytes"]) == b"jpeg"
        client.sendall(packet)  # Duplicate arrival must not reach perception.
        assert client.recv(1) == b""
    thread.join(5)
    assert not thread.is_alive() and closed.is_set()
    assert not ready.exists()
    evidence = json.loads(ready.with_suffix(".exit.json").read_text())
    assert evidence["error_type"] == "ValueError"
    assert "strictly increase" in evidence["reason"]
    assert evidence["accepted_frame_id"] == 7
    assert evidence["received_frames"] == 1
    assert "capability" not in ready.with_suffix(".exit.json").read_text()
