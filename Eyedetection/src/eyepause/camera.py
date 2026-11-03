"""Capture and inference workers exchange only their newest frame/result."""

import threading
import time
from dataclasses import dataclass

from eyepause.domain import Observation
from eyepause.resources import MODEL_PATH, verify_model
from eyepause.vision import LEFT_EYE, RIGHT_EYE, eye_aspect_ratio


class LatestValue[T]:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._value: T | None = None

    def put(self, value: T) -> None:
        with self._lock:
            self._value = value

    def get(self) -> T | None:
        with self._lock:
            return self._value


@dataclass(frozen=True)
class VisionResult:
    observation: Observation
    frame_rgb: object = None
    eyes: tuple = ()


class FaceDetector:
    def __init__(self) -> None:
        import mediapipe as mp

        verify_model()
        self.mp = mp
        options = mp.tasks.vision.FaceLandmarkerOptions(
            base_options=mp.tasks.BaseOptions(model_asset_path=str(MODEL_PATH)),
            running_mode=mp.tasks.vision.RunningMode.VIDEO,
            num_faces=2,
            min_face_detection_confidence=0.6,
            min_face_presence_confidence=0.6,
            min_tracking_confidence=0.6,
        )
        self.landmarker = mp.tasks.vision.FaceLandmarker.create_from_options(options)
        self.last_ms = -1

    def detect(self, rgb, timestamp: float) -> VisionResult:
        start = time.perf_counter()
        milliseconds = max(self.last_ms + 1, int(timestamp * 1000))
        self.last_ms = milliseconds
        result = self.landmarker.detect_for_video(
            self.mp.Image(image_format=self.mp.ImageFormat.SRGB, data=rgb), milliseconds
        )
        count = len(result.face_landmarks)
        if count != 1:
            reason = "No face in view" if count == 0 else "Keep one viewer in view"
            return VisionResult(
                Observation(
                    timestamp,
                    face_count=count,
                    reason=reason,
                    processing_ms=(time.perf_counter() - start) * 1000,
                ),
                rgb,
            )
        height, width = rgb.shape[:2]
        landmarks = result.face_landmarks[0]
        eyes = tuple(
            tuple((landmarks[i].x * width, landmarks[i].y * height) for i in indices)
            for indices in (LEFT_EYE, RIGHT_EYE)
        )
        try:
            left, right = (eye_aspect_ratio(list(points)) for points in eyes)
            # Off-frame eye geometry cannot establish a valid bilateral measurement.
            valid = all(0 <= x < width and 0 <= y < height for eye in eyes for x, y in eye)
            observation = Observation(
                timestamp,
                left if valid else None,
                right if valid else None,
                1,
                "" if valid else "Face the camera",
                (time.perf_counter() - start) * 1000,
            )
        except ValueError:
            observation = Observation(timestamp, face_count=1, reason="Eye landmarks unavailable")
        return VisionResult(observation, rgb, eyes)

    def close(self) -> None:
        self.landmarker.close()


class CameraPipeline:
    """At most one captured frame and one result are retained. Frames are never saved."""

    def __init__(self, camera_index: int, *, capture_factory=None, detector_factory=None):
        self.camera_index = camera_index
        self.capture_factory = capture_factory
        self.detector_factory = detector_factory or FaceDetector
        self.frames = LatestValue()
        self.results: LatestValue[VisionResult] = LatestValue()
        self.errors: LatestValue[str] = LatestValue()
        self.stop_event = threading.Event()
        self.threads: list[threading.Thread] = []

    def start(self) -> None:
        if self.threads:
            raise RuntimeError("Camera pipeline has already been started.")
        for function, name in (
            (self._capture, "EyePause capture"),
            (self._infer, "EyePause inference"),
        ):
            thread = threading.Thread(target=function, name=name, daemon=True)
            self.threads.append(thread)
            thread.start()

    def _capture(self) -> None:
        import cv2

        camera = None
        try:
            camera = (
                self.capture_factory(self.camera_index)
                if self.capture_factory
                else cv2.VideoCapture(self.camera_index, cv2.CAP_DSHOW)
            )
            if not camera.isOpened():
                raise RuntimeError(
                    "Cannot open camera. Check its index, Windows camera permissions, "
                    "and whether another app is using it."
                )
            camera.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
            camera.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
            camera.set(cv2.CAP_PROP_FPS, 30)
            camera.set(cv2.CAP_PROP_BUFFERSIZE, 1)
            while not self.stop_event.is_set():
                ok, frame = camera.read()
                if not ok or frame is None:
                    raise RuntimeError("Camera disconnected or stopped returning frames.")
                self.frames.put((time.monotonic(), frame))
        except Exception as error:
            self.errors.put(str(error))
            self.stop_event.set()
        finally:
            if camera is not None:
                camera.release()

    def _infer(self) -> None:
        detector = None
        try:
            import cv2

            detector = self.detector_factory()
            previous = -1.0
            while not self.stop_event.is_set():
                frame = self.frames.get()
                if frame is None or frame[0] <= previous:
                    self.stop_event.wait(0.005)
                    continue
                timestamp, bgr = frame
                previous = timestamp
                rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
                result = detector.detect(rgb, timestamp)
                if time.monotonic() - timestamp <= 0.75:
                    self.results.put(result)
                else:
                    self.results.put(
                        VisionResult(
                            Observation(
                                time.monotonic(),
                                reason="Processing is too slow; tracking suspended",
                            )
                        )
                    )
        except Exception as error:
            self.errors.put(f"Vision unavailable: {error}")
            self.stop_event.set()
        finally:
            if detector is not None:
                detector.close()

    def stop(self) -> bool:
        self.stop_event.set()
        for thread in self.threads:
            thread.join(timeout=2)
        return not any(thread.is_alive() for thread in self.threads)
