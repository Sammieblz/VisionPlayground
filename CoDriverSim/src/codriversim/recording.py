"""Bounded camera ring and durable event clips; replay uses identical observer inputs."""

import json
import threading
from collections import deque
from pathlib import Path
from uuid import uuid4

from codriversim.contracts import CameraFrame


class EventRecorder:
    def __init__(
        self,
        root: Path,
        *,
        pre=10.0,
        post=10.0,
        max_bytes=128 * 1024 * 1024,
        max_automatic_bytes=2 * 1024 * 1024 * 1024,
    ):
        self.root, self.pre, self.post, self.max_bytes = root, pre, post, max_bytes
        self.frames = deque()
        self.bytes = 0
        self.active = []
        self.error = ""
        self.saved = 0
        self.ring_incomplete_until = -1.0
        self.max_automatic_bytes = max_automatic_bytes
        self.automatic_bytes = 0
        # A full automatic cache stops new automatic writes. It never deletes a
        # recording, and an explicit user save is permitted outside this quota.
        try:
            for path in root.glob("clip-*/recording.json"):
                report = json.loads(path.read_text(encoding="utf-8"))
                if report.get("schema_version") == 2 and report.get("explicitly_saved") is False:
                    self.automatic_bytes += sum(
                        p.stat().st_size for p in path.parent.iterdir() if p.is_file()
                    )
        except (OSError, ValueError) as exc:
            self.error = "Cannot inspect automatic recording cache: " + str(exc)
            self.automatic_bytes = self.max_automatic_bytes

    def push(self, frame: CameraFrame, observation: dict):
        self.frames.append((frame, observation))
        self.bytes += len(frame.image)
        while self.frames and (
            self.bytes > self.max_bytes
            or self.frames[0][0].timestamp < frame.timestamp - self.pre - 2
        ):
            self.bytes -= len(self.frames.popleft()[0].image)
        for clip in self.active[:]:
            if frame.timestamp <= clip["end"]:
                self._write(clip, frame, observation)
            if frame.timestamp - clip["checkpoint_at"] >= 1 and clip in self.active:
                self._checkpoint(clip, complete=False)
                clip["checkpoint_at"] = frame.timestamp
            if frame.timestamp >= clip["end"]:
                self._finish(clip, complete=clip["purpose"] == "event")
                if clip["purpose"] == "scenario":
                    self.error = "Scenario capture reached its ten-minute storage limit"

    def trigger(self, timestamp: float, reason: str, *, explicit=False):
        # Automatic overlapping triggers extend a single clip; saved clips are never evicted.
        events = [clip for clip in self.active if clip["purpose"] == "event"]
        if events and not explicit:
            clip = events[-1]
            clip["end"] = min(timestamp + self.post, clip["start"] + 60)
            clip["reasons"].append({"timestamp": timestamp, "reason": reason})
            return
        if len(self.active) >= 3:
            self.error = "Three clips already being saved"
            return
        if not explicit and self.automatic_bytes + 4096 > self.max_automatic_bytes:
            self.error = "Automatic recording cache full; save manually or remove clips and restart"
            return
        path = self.root / f"clip-{uuid4().hex}"
        try:
            path.mkdir(parents=True, exist_ok=False)
        except OSError as exc:
            self.error = str(exc)
            return
        clip = {
            "path": path,
            "start": timestamp,
            "end": timestamp + self.post,
            "frame_count": 0,
            "first_timestamp": None,
            "last_timestamp": None,
            "dropped_frames": int(timestamp <= self.ring_incomplete_until),
            "reasons": [{"timestamp": timestamp, "reason": reason}],
            "explicitly_saved": explicit,
            "checkpoint_at": timestamp,
            "purpose": "event",
            "pre": self.pre,
            "post": self.post,
        }
        self.active.append(clip)
        if not explicit:
            self.automatic_bytes += 4096  # bounded manifest header/reason allowance
        for frame, observation in self.frames:
            if timestamp - self.pre <= frame.timestamp <= clip["end"]:
                self._write(clip, frame, observation)
        if clip in self.active:
            self._checkpoint(clip, complete=False)

    def start_capture(self, timestamp: float, capture_id: str):
        """Record a bounded scenario stream, independently of event clip triggers."""
        if len(capture_id) != 32 or any(c not in "0123456789abcdef" for c in capture_id):
            raise ValueError("Capture ID must be an opaque lowercase hex UUID")
        self.stop_capture(timestamp)
        if self.automatic_bytes + 4096 > self.max_automatic_bytes:
            self.error = "Automatic recording cache full; scenario capture unavailable"
            return
        if len(self.active) >= 3:
            self.error = "Three clips already being saved"
            return
        path = self.root / f"clip-{capture_id}"
        try:
            path.mkdir(parents=True, exist_ok=False)
        except OSError as exc:
            self.error = str(exc)
            return
        self.automatic_bytes += 4096
        clip = {
            "path": path,
            "start": timestamp,
            "end": timestamp + 600,
            "frame_count": 0,
            "first_timestamp": None,
            "last_timestamp": None,
            "dropped_frames": 0,
            "reasons": [{"timestamp": timestamp, "reason": "Scenario sensor replay"}],
            "explicitly_saved": False,
            "checkpoint_at": timestamp,
            "purpose": "scenario",
            "pre": 0,
            "post": 600,
        }
        self.active.append(clip)
        # Start on the current capture when the service command follows submit().
        if self.frames and self.frames[-1][0].timestamp == timestamp:
            self._write(clip, *self.frames[-1])
        if clip in self.active:
            self._checkpoint(clip, complete=False)

    def stop_capture(self, timestamp: float):
        for clip in self.active[:]:
            if clip["purpose"] == "scenario":
                clip["end"] = timestamp
                clip["post"] = max(0, timestamp - clip["start"])
                self._finish(clip, complete=True)

    def _write(self, clip, frame, observation):
        if clip not in self.active:
            return
        name = f"{frame.frame_id:010d}.jpg"
        row = {"camera": frame.metadata(), "image": name, "observation": observation}
        encoded = json.dumps(row) + "\n"
        reserved = len(frame.image) + len(encoded.encode("utf-8")) + 64
        if (
            not clip["explicitly_saved"]
            and self.automatic_bytes + reserved > self.max_automatic_bytes
        ):
            self.error = "Automatic recording cache full; event clip is partial"
            self._finish(clip, complete=False)
            return
        try:
            with (clip["path"] / name).open("xb") as output:
                output.write(frame.image)
            # Append once. Rewriting an ever-growing manifest held the Python GIL
            # long enough to stall camera delivery during continuous captures.
            with (clip["path"] / "frames.jsonl").open(
                "a", encoding="utf-8", newline="\n"
            ) as output:
                output.write(encoded)
            clip["frame_count"] += 1
            if clip["first_timestamp"] is None:
                clip["first_timestamp"] = frame.timestamp
            clip["last_timestamp"] = frame.timestamp
            if not clip["explicitly_saved"]:
                self.automatic_bytes += reserved
        except FileExistsError:
            pass
        except OSError as exc:
            self.error = str(exc)
            self._finish(clip, complete=False)

    def _checkpoint(self, clip, *, complete):
        try:
            before = max(0, clip["start"] - clip["first_timestamp"]) if clip["frame_count"] else 0
            after = max(0, clip["last_timestamp"] - clip["start"]) if clip["frame_count"] else 0
            report = {
                "schema_version": 2,
                "complete": complete
                and clip["dropped_frames"] == 0
                and before >= clip["pre"] - 0.1
                and after >= clip["post"] - 0.1,
                "pre_seconds": clip["pre"],
                "post_seconds": clip["post"],
                "available_pre_seconds": before,
                "available_post_seconds": after,
                "reasons": clip["reasons"],
                "frames_file": "frames.jsonl",
                "frame_count": clip["frame_count"],
                "recording_queue_dropped_frames": clip["dropped_frames"],
                "vehicle_control": False,
                "explicitly_saved": clip["explicitly_saved"],
                "purpose": clip["purpose"],
            }
            temporary = clip["path"] / "recording.json.tmp"
            temporary.write_text(json.dumps(report), encoding="utf-8")
            temporary.replace(clip["path"] / "recording.json")
            return True
        except OSError as exc:
            self.error = str(exc)
            return False

    def _finish(self, clip, *, complete):
        if clip not in self.active:
            return
        if self._checkpoint(clip, complete=complete):
            self.saved += 1
        self.active.remove(clip)

    def close(self):
        for clip in self.active[:]:
            self._finish(clip, complete=False)

    def mark_dropped(self, count):
        self.error = "Recording storage fell behind; frames were dropped and clips are partial"
        if self.frames:
            self.ring_incomplete_until = self.frames[-1][0].timestamp + self.pre
        for clip in self.active:
            clip["dropped_frames"] += count


class RecordingWorker:
    """One bounded disk worker; camera and inference threads never wait on storage."""

    def __init__(self, recorder, *, max_requests=80, max_bytes=32 * 1024 * 1024):
        self.recorder = recorder
        self.max_requests, self.max_bytes = max_requests, max_bytes
        self.pending = deque()
        self.bytes = self.dropped = self.unreported_drops = 0
        self.failure = ""
        self.stopping = False
        self.condition = threading.Condition()
        self.thread = threading.Thread(target=self._work, daemon=True)
        self.thread.start()

    @property
    def error(self):
        return self.failure or self.recorder.error

    @property
    def saved(self):
        return self.recorder.saved

    def request(self, action, *args, **kwargs):
        size = len(args[0].image) if action == "push" else 0
        with self.condition:
            # Leave room for stop/save commands even when frames fill the queue.
            limit = max(1, self.max_requests - 16) if action == "push" else self.max_requests
            if self.stopping or len(self.pending) >= limit or self.bytes + size > self.max_bytes:
                self.dropped += 1
                self.unreported_drops += 1
                self.failure = "Recording queue full; capture is partial"
                self.condition.notify()
                return False
            self.pending.append((action, args, kwargs, size))
            self.bytes += size
            self.condition.notify()
            return True

    def _work(self):
        try:
            while True:
                with self.condition:
                    self.condition.wait_for(
                        lambda: self.pending or self.stopping or self.unreported_drops
                    )
                    drops, self.unreported_drops = self.unreported_drops, 0
                    item = self.pending.popleft() if self.pending else None
                    if item:
                        self.bytes -= item[3]
                    finished = self.stopping and not item
                if drops:
                    self.recorder.mark_dropped(drops)
                if finished:
                    break
                if item:
                    try:
                        getattr(self.recorder, item[0])(*item[1], **item[2])
                    except Exception as exc:
                        self.failure = "Recording failed: " + str(exc)
                        self.recorder.mark_dropped(1)
        finally:
            self.recorder.close()

    def close(self):
        with self.condition:
            self.stopping = True
            self.condition.notify()
        self.thread.join(timeout=5)
        if self.thread.is_alive():
            self.failure = "Recording shutdown exceeded five seconds; clips may be partial"


def read_recording(path: Path):
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schema_version") != 2:
        raise ValueError("Expected a v2 sensor recording")
    from codriversim.contracts import FrameOrder

    order = FrameOrder()
    for row in recording_rows(path, data):
        image = (path.parent / row["image"]).resolve()
        if not image.is_relative_to(path.parent.resolve()):
            raise ValueError("Image reference escapes recording directory")
        frame = CameraFrame.parse(row["camera"], image.read_bytes())
        order.accept(frame)
        yield frame


def recording_rows(path: Path, data: dict):
    """Read both original embedded v2 frames and append-only v2 frame journals."""
    if "frames_file" not in data:
        if not isinstance(data.get("frames"), list):
            raise ValueError("Recording has no frame list or journal")
        yield from data["frames"]
        return
    journal = (path.parent / data["frames_file"]).resolve()
    if not journal.is_relative_to(path.parent.resolve()):
        raise ValueError("Frame journal escapes recording directory")
    if data.get("frame_count") == 0 and not journal.exists():
        return
    with journal.open(encoding="utf-8") as source:
        for line in source:
            # A hard interruption can leave a torn final append. All earlier
            # newline-terminated rows remain replayable; the header stays partial.
            if not line.endswith("\n"):
                break
            yield json.loads(line)
