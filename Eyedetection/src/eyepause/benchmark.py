"""Replay annotated measurements or clips without sending any real media commands."""

import argparse
import json
import math
import statistics
import time
from dataclasses import asdict, dataclass, replace
from pathlib import Path

from eyepause.domain import Action, CommandResult, EyeState, Observation, PlaybackStatus, Session
from eyepause.policy import PlaybackPolicy
from eyepause.resources import ASSETS
from eyepause.settings import Settings

LABELS = {"open": EyeState.OPEN, "closed": EyeState.CLOSED, "unknown": EyeState.UNKNOWN}


@dataclass(frozen=True)
class AnnotatedSample:
    observation: Observation
    label: EyeState


def read_trace(path: Path) -> list[AnnotatedSample]:
    samples = []
    previous = -math.inf
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            continue
        try:
            row = json.loads(line)
            timestamp = float(row["timestamp"])
            if not math.isfinite(timestamp) or timestamp <= previous:
                raise ValueError("Timestamps must be finite and strictly increasing")
            for value in (row.get("left_ear"), row.get("right_ear")):
                if value is not None and (not math.isfinite(value) or not 0 <= value <= 1):
                    raise ValueError("EAR measurements must be finite and between zero and one")
            samples.append(
                AnnotatedSample(
                    Observation(
                        timestamp, row.get("left_ear"), row.get("right_ear"), row.get("faces", 1)
                    ),
                    LABELS[row["label"]],
                )
            )
            previous = timestamp
        except (ValueError, TypeError, KeyError) as error:
            raise ValueError(f"Invalid trace row {number}: {error}") from error
    if not samples:
        raise ValueError("The trace is empty.")
    return samples


def _distribution(values: list[float]) -> dict:
    ordered = sorted(values)
    return {
        "count": len(values),
        "median_ms": statistics.median(values) if values else None,
        "p95_ms": ordered[max(0, math.ceil(len(ordered) * 0.95) - 1)] if values else None,
    }


def evaluate(
    samples: list[AnnotatedSample],
    settings: Settings | None = None,
    *,
    mode: str = "trace",
    processing_seconds: float | None = None,
) -> dict:
    if not samples:
        raise ValueError("At least one annotated sample is required.")
    settings = settings or Settings()
    started = time.perf_counter()
    player = Session(
        "replay", "Simulated player", "Annotated replay", "replay", PlaybackStatus.PLAYING
    )
    policy = PlaybackPolicy(settings)
    policy.start(player.token)
    policy.update_session(player)
    actions = []
    correct = tp = fp = fn = 0
    segments = []
    for index, annotated in enumerate(samples):
        sample = annotated.observation
        if index and sample.timestamp <= samples[index - 1].observation.timestamp:
            raise ValueError("Observations must have strictly increasing timestamps.")
        # Gaps are unobserved time, not evidence of continuous eye closure.
        gap = index and sample.timestamp - samples[index - 1].observation.timestamp > 0.75
        if not segments or annotated.label != segments[-1]["label"] or gap:
            if segments:
                segments[-1]["end"] = (
                    samples[index - 1].observation.timestamp if gap else sample.timestamp
                )
            segments.append(
                {"label": annotated.label, "start": sample.timestamp, "end": sample.timestamp}
            )
        else:
            segments[-1]["end"] = sample.timestamp
        command = policy.observe(sample)
        predicted = policy.eye_state
        correct += predicted == annotated.label
        tp += predicted == EyeState.CLOSED and annotated.label == EyeState.CLOSED
        fp += predicted == EyeState.CLOSED and annotated.label != EyeState.CLOSED
        fn += predicted != EyeState.CLOSED and annotated.label == EyeState.CLOSED
        if command:
            actions.append({"action": command.action.value, "timestamp": sample.timestamp})
            player = replace(
                player,
                status=(
                    PlaybackStatus.PAUSED
                    if command.action == Action.PAUSE
                    else PlaybackStatus.PLAYING
                ),
                revision=player.revision + 1,
            )
            policy.complete(command, CommandResult(True, player))
    truth = [
        s
        for s in segments
        if s["label"] == EyeState.CLOSED and s["end"] - s["start"] + 1e-9 >= settings.pause_seconds
    ]
    predicted_pauses = [a for a in actions if a["action"] == "pause"]
    used = set()
    latencies = []
    resume_latencies = []
    for segment in truth:
        match = next(
            (
                i
                for i, a in enumerate(predicted_pauses)
                if i not in used
                and segment["start"] + settings.pause_seconds - 1e-9
                <= a["timestamp"]
                <= segment["end"]
            ),
            None,
        )
        if match is not None:
            used.add(match)
            latencies.append((predicted_pauses[match]["timestamp"] - segment["start"]) * 1000)
            following = next(
                (
                    s
                    for s in segments
                    if s["start"] >= segment["end"] and s["label"] == EyeState.OPEN
                ),
                None,
            )
            if following:
                resumed = next(
                    (
                        a
                        for a in actions
                        if a["action"] == "play"
                        and following["start"] <= a["timestamp"] <= following["end"]
                    ),
                    None,
                )
                if resumed:
                    resume_latencies.append((resumed["timestamp"] - following["start"]) * 1000)
    elapsed = (
        processing_seconds if processing_seconds is not None else time.perf_counter() - started
    )
    return {
        "mode": mode,
        "settings": asdict(settings),
        "samples": len(samples),
        "annotated_seconds": samples[-1].observation.timestamp - samples[0].observation.timestamp,
        "processing_seconds": elapsed,
        "processing_samples_per_second": len(samples) / elapsed if elapsed else None,
        "sample_state_accuracy": correct / len(samples),
        "closed_sample_precision": tp / (tp + fp) if tp + fp else None,
        "closed_sample_recall": tp / (tp + fn) if tp + fn else None,
        "sustained_closure_events": len(truth),
        "pause_events": len(predicted_pauses),
        "closure_event_precision": len(used) / len(predicted_pauses) if predicted_pauses else None,
        "closure_event_recall": len(used) / len(truth) if truth else None,
        "false_pauses": len(predicted_pauses) - len(used),
        "closure_to_pause": _distribution(latencies),
        "reopening_to_resume": _distribution(resume_latencies),
        "inference": _distribution([s.observation.processing_ms for s in samples])
        if mode == "clip"
        else None,
        "actions": actions,
        "note": "Latency includes configured dwell time; simulated playback acknowledges "
        "immediately. Trace throughput measures policy replay, not camera inference "
        "or media-service latency.",
    }


def read_segments(path: Path) -> list[dict]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("version") != 1 or not isinstance(data.get("segments"), list):
        raise ValueError("Annotations require version 1 and a segments list.")
    end = 0.0
    for segment in data["segments"]:
        start, next_end = segment["start"], segment["end"]
        if (
            not math.isfinite(start)
            or not math.isfinite(next_end)
            or abs(start - end) > 1e-6
            or next_end <= start
            or segment["label"] not in LABELS
        ):
            raise ValueError("Segments must be contiguous from zero, increasing, and labeled.")
        end = next_end
    return data["segments"]


def evaluate_clip(video: Path, annotations: Path, *, legacy=False) -> list[dict]:
    import cv2

    from eyepause.camera import FaceDetector

    segments = read_segments(annotations)
    capture = cv2.VideoCapture(str(video))
    detector = None
    try:
        if not capture.isOpened():
            raise ValueError(f"Cannot open video: {video}")
        fps = capture.get(cv2.CAP_PROP_FPS)
        if not math.isfinite(fps) or fps <= 0:
            raise ValueError("Video must report a positive frame rate.")
        detector = FaceDetector()
        cascade = cv2.CascadeClassifier(str(ASSETS / "haarcascade_eye.xml")) if legacy else None
        if cascade is not None and cascade.empty():
            raise ValueError("Bundled historical eye cascade could not be loaded.")
        samples, baseline = [], []
        index = segment_index = 0
        wall = time.perf_counter()
        legacy_seconds = 0.0
        while True:
            ok, frame = capture.read()
            if not ok:
                break
            timestamp = index / fps
            while segment_index < len(segments) and timestamp >= segments[segment_index]["end"]:
                segment_index += 1
            if segment_index == len(segments):
                raise ValueError("Every video frame must have an annotation segment.")
            truth = LABELS[segments[segment_index]["label"]]
            result = detector.detect(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB), timestamp)
            samples.append(AnnotatedSample(result.observation, truth))
            if cascade is not None:
                tick = time.perf_counter()
                gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
                eyes = cascade.detectMultiScale(gray, 1.1, 4)
                closed = len(eyes) > 0 and all(
                    gray[y : y + h, x : x + w].mean() < 120 for x, y, w, h in eyes[:2]
                )
                ear = 0.1 if closed else 0.3
                baseline.append(AnnotatedSample(Observation(timestamp, ear, ear, 1), truth))
                legacy_seconds += time.perf_counter() - tick
            index += 1
        elapsed = time.perf_counter() - wall
        output = [evaluate(samples, mode="clip", processing_seconds=elapsed - legacy_seconds)]
        if legacy:
            output.append(
                evaluate(baseline, mode="legacy-brightness", processing_seconds=legacy_seconds)
            )
        return output
    finally:
        capture.release()
        if detector:
            detector.close()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Evaluate EyePause without controlling real media")
    subparsers = parser.add_subparsers(dest="mode", required=True)
    trace = subparsers.add_parser("trace", help="Replay annotated EAR observations")
    trace.add_argument("trace", type=Path)
    clip = subparsers.add_parser("clip", help="Run Face Landmarker on every annotated clip frame")
    clip.add_argument("video", type=Path)
    clip.add_argument("annotations", type=Path)
    clip.add_argument("--compare-legacy", action="store_true")
    for child in (trace, clip):
        child.add_argument("--output", type=Path)
    args = parser.parse_args(argv)
    try:
        reports = (
            [evaluate(read_trace(args.trace))]
            if args.mode == "trace"
            else evaluate_clip(args.video, args.annotations, legacy=args.compare_legacy)
        )
        payload = json.dumps({"version": 1, "reports": reports}, indent=2, allow_nan=False)
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(payload + "\n", encoding="utf-8")
        print(payload)
        return 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.error(str(error))


if __name__ == "__main__":
    raise SystemExit(main())
