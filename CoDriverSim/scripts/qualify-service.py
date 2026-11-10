"""Exercise the real local observer's transport, storage, and restart boundaries.

Uses an existing camera JPEG and only processes started by this script. Results
are recovery evidence, not a perception-accuracy or performance benchmark.
"""

import argparse
import json
import socket
import struct
import subprocess
import sys
import time
from pathlib import Path
from uuid import uuid4

import cv2

from codriversim.contracts import Calibration, CameraFrame, EgoTelemetry
from codriversim.recording import read_recording
from codriversim.service import receive


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    destination = root / "outputs/qualification" / ("service-recovery-" + uuid4().hex)
    destination.mkdir(parents=True)
    image = args.image.read_bytes()
    decoded = cv2.imread(str(args.image))
    if decoded is None:
        raise ValueError("Expected an existing camera JPEG")
    height, width = decoded.shape[:2]
    results = []

    for case in (
        "frame-gaps",
        "hidden-truth",
        "duplicate-frame",
        "wrong-token",
        "oversized-packet",
        "storage-unavailable",
        "scenario-capture",
        "fresh-session",
    ):
        token = uuid4().hex
        ready = destination / (case + ".ready.json")
        output = destination / (case + "-recordings")
        if case == "storage-unavailable":
            output.write_text("This file deliberately occupies the recording directory.")
        with (destination / (case + ".log")).open("w") as log:
            process = subprocess.Popen(
                [
                    sys.executable,
                    "-m",
                    "codriversim.service",
                    "--model",
                    str(root / "models/object_detection_nanodet_2022nov.onnx"),
                    "--ready",
                    str(ready),
                    "--output",
                    str(output),
                    "--token",
                    token,
                ],
                stdout=log,
                stderr=subprocess.STDOUT,
            )
            try:
                deadline = time.monotonic() + 30
                while not ready.is_file():
                    if process.poll() is not None or time.monotonic() >= deadline:
                        raise RuntimeError(f"Observer failed to start for {case}; see its log")
                    time.sleep(0.05)
                endpoint = json.loads(ready.read_text())
                frame = CameraFrame(
                    token,
                    0,
                    1.0,
                    width,
                    height,
                    Calibration(width, width, width / 2, height / 2, source="uncalibrated"),
                    EgoTelemetry(),
                    image,
                )
                report = {}
                with socket.create_connection(
                    ("127.0.0.1", endpoint["port"]), timeout=5
                ) as connection:
                    connection.settimeout(5)

                    def send(index, *, extra=None, capability=token, camera_frame=frame):
                        header = camera_frame.metadata() | {
                            "frame_id": index,
                            "timestamp": 1 + index * 0.05,
                            "token": capability,
                        }
                        header.update(extra or {})
                        data = json.dumps(header).encode()
                        connection.sendall(struct.pack("<II", len(data), len(image)) + data + image)
                        count = struct.unpack("<I", receive(connection, 4))[0]
                        result = json.loads(receive(connection, count))
                        receive(connection, result["image_bytes"])
                        assert result["vehicle_control"] is False
                        return result

                    rejected = case in {
                        "hidden-truth",
                        "duplicate-frame",
                        "wrong-token",
                        "oversized-packet",
                    }
                    if rejected:
                        try:
                            if case == "hidden-truth":
                                send(0, extra={"objects": [{"hidden_position": [1, 2, 3]}]})
                            elif case == "duplicate-frame":
                                send(0)
                                send(0)
                            elif case == "wrong-token":
                                send(0, capability="different-session")
                            else:
                                connection.sendall(struct.pack("<II", 20000, 1))
                                receive(connection, 4)
                        except (EOFError, ConnectionResetError, ConnectionAbortedError):
                            report["invalid_packet_rejected"] = True
                        else:
                            raise AssertionError(f"Invalid packet was accepted: {case}")
                    else:
                        capture_id = uuid4().hex
                        if case == "scenario-capture":
                            ready.with_suffix(".capture").write_text(
                                json.dumps({"action": "start", "id": capture_id})
                            )
                        for index in range(0, 45, 3):
                            report = send(index)
                            if case == "storage-unavailable" and index == 6:
                                ready.with_suffix(".save").write_text("manual")
                            if case == "scenario-capture" and index == 30:
                                ready.with_suffix(".capture").write_text(
                                    json.dumps({"action": "stop"})
                                )
                            time.sleep(0.08)
                        assert report["processed_frames"] > 0
                        assert report["session_id"] == token
                        assert report["frame_id"] <= 42
                        if case == "storage-unavailable":
                            assert report["recording_error"], "Storage failure was hidden"
                        report = {
                            key: report[key]
                            for key in (
                                "received_frames",
                                "processed_frames",
                                "recording_error",
                                "vehicle_control",
                            )
                        }
                        if case == "scenario-capture":
                            recording = output / f"clip-{capture_id}" / "recording.json"
                            metadata = json.loads(recording.read_text())
                            frames = list(read_recording(recording))
                            assert metadata["complete"] and metadata["purpose"] == "scenario"
                            assert len(frames) >= 10
                            assert all(f.session_id == token and f.image == image for f in frames)
                            report["replay_frames"] = len(frames)
                            report["capture_complete"] = True
                # Closing a dropped client must finish its owned service and remove readiness.
                process.wait(timeout=10)
                assert process.returncode == 0 and not ready.exists()
                results.append(
                    {"case": case, "passed": True, "clean_exit": True, "evidence": report}
                )
            finally:
                if process.poll() is None:
                    # Windows venv launchers may have a child Python process. Target only
                    # this exact owned process tree if graceful connection close failed.
                    subprocess.run(
                        ["taskkill", "/PID", str(process.pid), "/T", "/F"],
                        capture_output=True,
                        check=False,
                    )
                    process.wait(timeout=10)
    (destination / "result.json").write_text(json.dumps({"checks": results}, indent=2))
    print(destination / "result.json")


if __name__ == "__main__":
    main()
