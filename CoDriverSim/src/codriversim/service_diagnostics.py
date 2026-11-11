"""Bounded, out-of-band stack evidence for a stalled local camera service."""

import faulthandler
import json
import threading
import time


class ProgressWatchdog:
    def __init__(self, path, *, threshold=2.0, interval=0.5, limit=8):
        self.path = path
        self.threshold, self.interval, self.limit = threshold, interval, limit
        self.state = (time.monotonic(), "starting", -1)
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self._watch, daemon=True)
        self.thread.start()

    def mark(self, stage, frame_id=-1):
        # One immutable assignment; the transport thread never waits on storage
        # or a diagnostic lock. No images, tokens or hidden world data are logged.
        self.state = (time.monotonic(), stage, frame_id)

    def _watch(self):
        last_reported = None
        count = 0
        while not self.stop.wait(self.interval) and count < self.limit:
            state = self.state
            elapsed = time.monotonic() - state[0]
            if elapsed < self.threshold or state is last_reported:
                continue
            last_reported = state
            count += 1
            try:
                self.path.parent.mkdir(parents=True, exist_ok=True)
                with self.path.open("a", encoding="utf-8") as output:
                    output.write(
                        json.dumps(
                            {
                                "event": "service_stage_stalled",
                                "stage": state[1],
                                "frame_id": state[2],
                                "elapsed_seconds": elapsed,
                                "unix_timestamp": time.time(),
                            }
                        )
                        + "\n"
                    )
                    output.flush()
                    faulthandler.dump_traceback(file=output, all_threads=True)
            except OSError:
                # Diagnostic storage failure must never stop the camera stream.
                pass

    def close(self):
        self.stop.set()
        self.thread.join(timeout=0.1)

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()
