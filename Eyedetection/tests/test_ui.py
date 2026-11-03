from dataclasses import replace

from PySide6.QtCore import Qt

from eyepause.camera import LatestValue
from eyepause.domain import PlaybackStatus, Session
from eyepause.engine import EngineStatus
from eyepause.settings import Settings
from eyepause.ui import STYLE, MainWindow


class FakeEngine:
    def __init__(self):
        self.status = LatestValue()
        self.status.put(EngineStatus(Settings()))
        self.requests = []
        self.closed = False

    def start(self):
        pass

    def request(self, action, value=None):
        self.requests.append((action, value))

    def close(self):
        self.closed = True
        return True


def test_player_selection_and_monitoring_controls(qtbot, qapp):
    qapp.setStyleSheet(STYLE)
    engine = FakeEngine()
    window = MainWindow(demo=True, engine=engine)
    qtbot.addWidget(window)
    window.show()
    window._tick()
    assert not window.start_button.isEnabled()
    session = Session("demo", "Demo", "Example film", "film", PlaybackStatus.PLAYING)
    engine.status.put(replace(engine.status.get(), sessions=(session,)))
    window._tick()
    assert window.start_button.isEnabled()
    qtbot.mouseClick(window.start_button, Qt.MouseButton.LeftButton)
    assert engine.requests[-1] == ("start", "demo")
    engine.status.put(replace(engine.status.get(), monitoring=True))
    window._tick()
    assert not window.players.isEnabled()
    assert not window.camera_index.isEnabled()
    assert window.stop_button.isEnabled()
    qtbot.mouseClick(window.stop_button, Qt.MouseButton.LeftButton)
    assert engine.requests[-1] == ("stop", None)
    window.shutdown()
    assert engine.closed


def test_demo_has_no_calibration_and_renders_without_camera(qtbot, qapp, tmp_path):
    qapp.setStyleSheet(STYLE)
    window = MainWindow(demo=True, engine=FakeEngine())
    qtbot.addWidget(window)
    window.show()
    assert not window.calibrate_button.isEnabled()
    qtbot.wait(50)
    assert window.grab().save(str(tmp_path / "ui.png"))
    window.shutdown()
