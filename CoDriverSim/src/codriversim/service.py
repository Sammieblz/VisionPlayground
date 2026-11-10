"""Local observation service. Receives only the strict CameraFrame interface.

Packet: little-endian uint32 header length, uint32 JPEG length, UTF-8 JSON,
JPEG. Replies: uint32 JSON length then JSON. Latest-frame slots bound work.
"""

import argparse
import json
import os
import secrets
import socket
import struct
import threading
import time
from dataclasses import asdict, replace
from pathlib import Path

from codriversim.contracts import CameraFrame, FrameOrder
from codriversim.recording import EventRecorder, RecordingWorker


def receive(sock, count):
    chunks = bytearray()
    while len(chunks) < count:
        part = sock.recv(count - len(chunks))
        if not part:
            raise EOFError("Connection closed")
        chunks.extend(part)
    return bytes(chunks)


def packet(sock):
    header_size, image_size = struct.unpack("<II", receive(sock, 8))
    if not 1 <= header_size <= 16384 or not 1 <= image_size <= 8_000_000:
        raise ValueError("Packet exceeds transport limits")
    return json.loads(receive(sock, header_size)), receive(sock, image_size)


def reply(sock, value, image=b""):
    data = json.dumps(value, allow_nan=False).encode()
    sock.sendall(struct.pack("<I", len(data)) + data + image)


class ObservationService:
    def __init__(self, model, output, road_file=None, evidence=None):
        from codriversim.perception import Observer

        self.observer = Observer(model)
        self.recorder = RecordingWorker(EventRecorder(output))
        self.lock = threading.Lock()
        self.road_file, self.evidence = road_file, evidence
        self.started = time.perf_counter()
        self.latencies = []
        self.latest = None
        self.result = {"status": "Waiting for a camera frame", "tracks": [], "risks": []}
        self.stop = threading.Event()
        self.frames = self.processed = self.dropped = 0
        self.last_trigger = -100.0
        self.thread = threading.Thread(target=self.work, daemon=True)
        self.thread.start()

    def submit(self, frame):
        with self.lock:
            if self.latest is not None:
                self.dropped += 1
            self.latest = frame
            result = self.result.copy()
        self.frames += 1
        self.recorder.request("push", frame, result)
        report = result | {
            "received_frames": self.frames,
            "processed_frames": self.processed,
            "dropped_frames": self.dropped,
            "recording_error": self.recorder.error,
            "saved_clips": self.recorder.saved,
            "recording_queue_dropped_frames": self.recorder.dropped,
            "vehicle_control": False,
            "camera_hz": self.frames / (time.perf_counter() - self.started),
            "detection_hz": self.processed / (time.perf_counter() - self.started),
        }
        if self.evidence:
            try:
                self.evidence.parent.mkdir(parents=True, exist_ok=True)
                if self.frames == 20:
                    self.evidence.with_suffix(".jpg").write_bytes(frame.image)
                if self.frames % 100 == 0:
                    self.evidence.write_text(json.dumps(report), encoding="utf-8")
            except OSError:
                pass
        return report

    def work(self):
        while not self.stop.is_set():
            start = time.perf_counter()
            with self.lock:
                frame, self.latest = self.latest, None
            if frame is not None:
                try:
                    road = None
                    if self.road_file and self.road_file.is_file():
                        try:
                            road = json.loads(self.road_file.read_text())
                        except (OSError, ValueError):
                            pass
                    result = asdict(self.observer.process(frame, road))
                    self.processed += 1
                    for risk in result["risks"]:
                        if risk["record"] and frame.timestamp - self.last_trigger > 10:
                            self.recorder.request("trigger", frame.timestamp, risk["message"])
                            self.last_trigger = frame.timestamp
                    with self.lock:
                        self.result = result
                # Model backends use their own exception types (e.g. cv2.error).
                # Keep the worker alive and expose the failure; never reuse a
                # previous risk as if it came from this failed frame.
                except Exception as exc:
                    with self.lock:
                        self.result = {
                            "status": f"Observer error: {exc}",
                            "tracks": [],
                            "risks": [],
                        }
            # Leave headroom above the 5 Hz delivery target for occasional OCR
            # passes. Slow inference still consumes only the latest queued frame;
            # there is no catch-up burst or backlog after an overrun.
            self.stop.wait(max(0, 0.15 - (time.perf_counter() - start)))

    def close(self):
        self.stop.set()
        self.thread.join(timeout=5)
        self.recorder.close()


def serve(model: Path, ready: Path, output: Path, token: str):
    service = ObservationService(
        model,
        output,
        road_file=ready.with_name(f"comma-{token}.road.json"),
        evidence=output.parent / "qualification" / f"observer-{token}.json",
    )
    ready.parent.mkdir(parents=True, exist_ok=True)
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        listener.listen(1)
        listener.settimeout(30)
        temporary = ready.with_suffix(".tmp")
        temporary.write_text(
            json.dumps({"port": listener.getsockname()[1], "pid": os.getpid(), "status": "ready"}),
            encoding="utf-8",
        )
        temporary.replace(ready)
        try:
            connection, _ = listener.accept()
            with connection:
                connection.settimeout(15)
                connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                connection.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 8 * 1024 * 1024)
                connection.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, 1024 * 1024)
                order = FrameOrder()
                while True:
                    header, image = packet(connection)
                    if not secrets.compare_digest(str(header.pop("token", "")), token):
                        raise ValueError("Invalid session capability")
                    frame = CameraFrame.parse(header, image)
                    order.accept(frame)
                    if frame.encoding.startswith("rgb24"):
                        import cv2
                        import numpy as np

                        rgb = np.frombuffer(frame.image, np.uint8).reshape(
                            frame.height, frame.width, 3
                        )
                        if frame.encoding == "rgb24-bottom-up":
                            rgb = cv2.flip(rgb, 0)
                        encoded = cv2.imencode(
                            ".jpg",
                            cv2.cvtColor(rgb, cv2.COLOR_RGB2BGR),
                            [cv2.IMWRITE_JPEG_QUALITY, 85],
                        )[1].tobytes()
                        frame = replace(frame, image=encoded, encoding="jpeg")
                    report = service.submit(frame)
                    report["image_bytes"] = len(frame.image)
                    reply(connection, report, frame.image)
                    save_request = ready.with_suffix(".save")
                    if save_request.is_file():
                        service.recorder.request(
                            "trigger", frame.timestamp, "Explicitly saved by driver", explicit=True
                        )
                        save_request.unlink(missing_ok=True)
                    capture_request = ready.with_suffix(".capture")
                    if capture_request.is_file():
                        try:
                            request = json.loads(capture_request.read_text())
                            if not isinstance(request, dict):
                                raise ValueError("Recording request must be an object")
                            if request.get("action") == "start":
                                service.recorder.request(
                                    "start_capture", frame.timestamp, request["id"]
                                )
                            elif request.get("action") == "stop":
                                service.recorder.request("stop_capture", frame.timestamp)
                            else:
                                raise ValueError("Unknown recording request")
                        except (OSError, ValueError, KeyError, TypeError) as exc:
                            service.recorder.failure = "Scenario capture request failed: " + str(
                                exc
                            )
                        capture_request.unlink(missing_ok=True)
        except (EOFError, OSError, ValueError) as exc:
            print(f"Observer stopped: {exc}", flush=True)
        finally:
            service.close()
            ready.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--ready", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--token", required=True)
    args = parser.parse_args()
    serve(args.model, args.ready, args.output, args.token)


if __name__ == "__main__":
    main()
