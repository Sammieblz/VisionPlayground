"""EyePause desktop: a local camera workspace with a device-free demonstration."""

import math
import os
import sys
from collections import deque
from dataclasses import replace
from pathlib import Path

from PySide6.QtCore import QPointF, QRectF, Qt, QTimer
from PySide6.QtGui import (
    QAction,
    QColor,
    QFont,
    QFontDatabase,
    QIcon,
    QImage,
    QLinearGradient,
    QPainter,
    QPainterPath,
    QPen,
    QPixmap,
)
from PySide6.QtWidgets import (
    QApplication,
    QCheckBox,
    QComboBox,
    QDoubleSpinBox,
    QFrame,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QMainWindow,
    QMenu,
    QMessageBox,
    QProgressBar,
    QPushButton,
    QScrollArea,
    QSizePolicy,
    QSpinBox,
    QSystemTrayIcon,
    QVBoxLayout,
    QWidget,
)

from eyepause import __version__
from eyepause.domain import EyeState
from eyepause.engine import Engine, EngineStatus
from eyepause.settings import Settings, load_settings, save_settings

ACCENT = "#71E6BE"
MUTED = "#8E9EA7"
STYLE = """
QMainWindow, QWidget#workspace { background: #101719; color: #EDF3F1; }
QWidget { font-family: 'Segoe UI'; font-size: 13px; color: #EDF3F1; }
QScrollArea { border: none; background: #101719; }
QFrame[card="true"] { background: #182225; border: 1px solid #2A373A; border-radius: 16px; }
QLabel { background: transparent; border: none; }
QLabel#eyebrow { color: #71E6BE; font-size: 11px; font-weight: 600; letter-spacing: 2px; }
QLabel#title { font-size: 40px; font-weight: 650; letter-spacing: -1px; }
QLabel#subtitle { color: #9AAEB3; font-size: 14px; }
QLabel#section { color: #9AAEB3; font-size: 10px; font-weight: 600; letter-spacing: 1.5px; }
QLabel#value { font-size: 24px; font-weight: 600; }
QLabel#small { color: #9AAEB3; font-size: 12px; }
QLabel#badge { color: #71E6BE; background: #203B33; border-radius: 12px; padding: 7px 12px; }
QPushButton { background: #263438; border: 1px solid #37464A; border-radius: 8px;
              padding: 11px 16px; font-weight: 600; }
QPushButton:hover { background: #32464A; border-color: #6B8C88; }
QPushButton:disabled { color: #7C8D92; background: #202B2F; border-color: #2B3A3D; }
QPushButton#primary { background: #71E6BE; color: #10291F; border: none; }
QPushButton#primary:hover { background: #A1F0D5; }
QPushButton#primary:disabled { background: #35564A; color: #9ABAAE; }
QComboBox, QSpinBox, QDoubleSpinBox { background: #111B1E; border: 1px solid #3A4A4E;
                                  border-radius: 7px; padding: 9px; min-height: 20px; }
QComboBox:disabled, QSpinBox:disabled, QDoubleSpinBox:disabled { color: #7F9298; }
QComboBox QAbstractItemView { background: #182225; selection-background-color: #35564A; }
QCheckBox { spacing: 9px; }
QCheckBox::indicator { width: 16px; height: 16px; border: 1px solid #6B8C88;
                      border-radius: 4px; background: #111B1E; }
QCheckBox::indicator:checked { background: #71E6BE; }
QProgressBar { background: #101A1D; border: none; border-radius: 3px; height: 6px; }
QProgressBar::chunk { background: #71E6BE; border-radius: 3px; }
QMenu { background: #182225; padding: 6px; border: 1px solid #3A4A4E; }
QMenu::item { padding: 9px 22px; }
QMenu::item:selected { background: #35564A; }
QToolTip { background: #203B33; color: #EDF3F1; border: 1px solid #48665B; padding: 6px; }
"""


def label(text: str, role: str = "", *, wrap=False) -> QLabel:
    widget = QLabel(text)
    widget.setTextFormat(Qt.TextFormat.PlainText)
    widget.setWordWrap(wrap)
    if role:
        widget.setObjectName(role)
    return widget


def card() -> tuple[QFrame, QVBoxLayout]:
    frame = QFrame()
    frame.setProperty("card", True)
    layout = QVBoxLayout(frame)
    layout.setContentsMargins(22, 20, 22, 20)
    layout.setSpacing(14)
    return frame, layout


def app_icon() -> QIcon:
    pixmap = QPixmap(64, 64)
    pixmap.fill(Qt.GlobalColor.transparent)
    painter = QPainter(pixmap)
    painter.setRenderHint(QPainter.RenderHint.Antialiasing)
    painter.setBrush(QColor("#203B33"))
    painter.setPen(Qt.PenStyle.NoPen)
    painter.drawRoundedRect(QRectF(0, 0, 64, 64), 16, 16)
    painter.setPen(QPen(QColor(ACCENT), 3))
    path = QPainterPath(QPointF(10, 32))
    path.cubicTo(22, 12, 42, 12, 54, 32)
    path.cubicTo(42, 52, 22, 52, 10, 32)
    painter.drawPath(path)
    painter.setBrush(QColor(ACCENT))
    painter.drawEllipse(QPointF(32, 32), 7, 7)
    painter.end()
    return QIcon(pixmap)


def configure_application(app: QApplication) -> None:
    # Qt's offscreen plugin does not enumerate Windows system fonts. Load the
    # installed fonts explicitly for CI/screenshots; no font files are bundled.
    if not QFontDatabase.families() and sys.platform == "win32":
        fonts = Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts"
        for name in ("segoeui.ttf", "seguisb.ttf", "segoeuib.ttf"):
            QFontDatabase.addApplicationFont(str(fonts / name))
    app.setFont(QFont("Segoe UI", 10))
    app.setStyle("Fusion")
    app.setStyleSheet(STYLE)


class Preview(QWidget):
    def __init__(self, demo: bool):
        super().__init__()
        self.demo = demo
        self.status = EngineStatus(Settings())
        self.setMinimumHeight(290)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

    def paintEvent(self, _event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        rect = QRectF(self.rect())
        gradient = QLinearGradient(rect.topLeft(), rect.bottomRight())
        gradient.setColorAt(0, QColor("#101E22"))
        gradient.setColorAt(1, QColor("#0C1518"))
        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(gradient)
        p.drawRoundedRect(rect, 10, 10)
        status = self.status
        result = status.vision
        if status.settings.preview and result and result.frame_rgb is not None:
            rgb = result.frame_rgb
            height, width = rgb.shape[:2]
            image = QImage(rgb.data, width, height, rgb.strides[0], QImage.Format.Format_RGB888)
            scale = min(self.width() / width, self.height() / height)
            destination = QRectF(
                (self.width() - width * scale) / 2,
                (self.height() - height * scale) / 2,
                width * scale,
                height * scale,
            )
            p.save()
            p.translate(self.width(), 0)
            p.scale(-1, 1)
            p.drawImage(destination, image)
            p.setPen(QPen(QColor(ACCENT), 2))
            for eye in result.eyes:
                for x, y in eye:
                    p.drawEllipse(
                        QPointF(destination.x() + x * scale, destination.y() + y * scale), 2.5, 2.5
                    )
            p.restore()
        elif self.demo and status.monitoring and status.settings.preview:
            cx, cy = rect.center().x(), rect.center().y() - 5
            p.setPen(QPen(QColor("#1D353B"), 1))
            for x in range(20, self.width(), 32):
                for y in range(20, self.height(), 32):
                    p.drawPoint(x, y)
            p.setBrush(QColor("#142B30"))
            p.setPen(QPen(QColor("#34565A"), 1.5))
            p.drawRoundedRect(QRectF(cx - 92, cy - 108, 184, 214), 85, 85)
            visible = status.eye_state != EyeState.UNKNOWN
            closed = status.eye_state == EyeState.CLOSED
            color = QColor(ACCENT if visible else "#648086")
            p.setPen(QPen(color, 2))
            for ex in (cx - 43, cx + 43):
                if closed:
                    p.setBrush(Qt.BrushStyle.NoBrush)
                    path = QPainterPath(QPointF(ex - 20, cy - 16))
                    path.quadTo(ex, cy - 5, ex + 20, cy - 16)
                    p.drawPath(path)
                else:
                    p.setBrush(QColor("#0D2025"))
                    p.drawEllipse(QPointF(ex, cy - 18), 23, 11)
                    p.setBrush(color)
                    p.drawEllipse(QPointF(ex, cy - 18), 5, 6)
                for angle in (0, 60, 120, 180, 240, 300):
                    a = math.radians(angle)
                    p.setBrush(color)
                    p.drawEllipse(QPointF(ex + 29 * math.cos(a), cy - 18 + 17 * math.sin(a)), 2, 2)
            p.setBrush(Qt.BrushStyle.NoBrush)
            p.setPen(QPen(QColor("#34565A"), 1.5))
            p.drawLine(QPointF(cx, cy), QPointF(cx - 5, cy + 20))
            p.drawArc(QRectF(cx - 22, cy + 33, 44, 16), 180 * 16, 180 * 16)
        else:
            p.setPen(QColor("#6B888D"))
            p.setFont(QFont("Segoe UI", 13))
            title = (
                "Preview hidden · detection stays local"
                if not status.settings.preview
                else "Your camera, your space"
            )
            p.drawText(rect.adjusted(20, -12, -20, -12), Qt.AlignmentFlag.AlignCenter, title)
            p.setFont(QFont("Segoe UI", 10))
            p.drawText(
                rect.adjusted(20, 42, -20, 42),
                Qt.AlignmentFlag.AlignCenter,
                "Start monitoring or calibration to enable your camera.",
            )
        p.setPen(QColor("#A6BDBF"))
        p.setFont(QFont("Segoe UI", 9))
        text = "SIMULATED VIEW · NO CAMERA" if self.demo else "LOCAL CAMERA · NEVER RECORDED"
        p.drawText(16, 25, text)
        if result and result.observation.reason:
            p.setBrush(QColor("#142529"))
            p.setPen(Qt.PenStyle.NoPen)
            p.drawRoundedRect(QRectF(12, self.height() - 42, self.width() - 24, 30), 6, 6)
            p.setPen(QColor("#CEDCD9"))
            p.drawText(
                QRectF(20, self.height() - 42, self.width() - 40, 30),
                Qt.AlignmentFlag.AlignVCenter,
                result.observation.reason,
            )


class EyeTrace(QWidget):
    def __init__(self):
        super().__init__()
        self.values = deque(maxlen=180)
        self.threshold = 0.20
        self.setMinimumHeight(95)
        self.setToolTip(
            "Average eye aspect ratio over recent observations. Gaps mean unknown tracking."
        )

    def paintEvent(self, _event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        w, h = self.width(), self.height()
        p.setPen(QPen(QColor("#2B3B3F"), 1))
        for fraction in (0.25, 0.5, 0.75):
            p.drawLine(0, int(h * fraction), w, int(h * fraction))
        p.setPen(QPen(QColor("#627B73"), 1, Qt.PenStyle.DashLine))
        y = h - self.threshold / 0.45 * (h - 10)
        p.drawLine(QPointF(0, y), QPointF(w, y))
        p.setPen(QPen(QColor(ACCENT), 2))
        path = QPainterPath()
        drawing = False
        for index, value in enumerate(self.values):
            if value is None:
                drawing = False
                continue
            point = QPointF(index * w / 179, h - min(value, 0.45) / 0.45 * (h - 10))
            if drawing:
                path.lineTo(point)
            else:
                path.moveTo(point)
                drawing = True
        p.drawPath(path)


class MainWindow(QMainWindow):
    def __init__(self, *, demo=False, engine=None):
        super().__init__()
        self.demo = demo
        settings, warning = (Settings(), "") if demo else load_settings()
        self.settings = settings
        self.engine = engine or Engine(settings, demo=demo)
        self.current = EngineStatus(settings)
        self.last_trace_timestamp = -1.0
        self.exiting = False
        self.seen_tray_notice = False
        self.setWindowTitle(f"EyePause · VisionPlayground{' · Demo' if demo else ''}")
        self.setWindowIcon(app_icon())
        self.resize(1210, 920)
        self.setMinimumSize(1020, 760)
        self._build()
        self._tray()
        self.engine.start()
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._tick)
        self.timer.start(80)
        if warning:
            self.message.setText(warning)

    def _build(self):
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        workspace = QWidget()
        workspace.setObjectName("workspace")
        outer = QVBoxLayout(workspace)
        outer.setContentsMargins(34, 28, 34, 22)
        outer.setSpacing(22)
        top = QHBoxLayout()
        heading = QVBoxLayout()
        heading.setSpacing(3)
        heading.addWidget(label("VISIONPLAYGROUND     /     PROJECT 01", "eyebrow"))
        heading.addWidget(label("EyePause", "title"))
        heading.addWidget(label("Stay in the story. Rest when you need to.", "subtitle"))
        top.addLayout(heading)
        top.addStretch()
        self.badge = label("DEMO MODE" if self.demo else "LOCAL PROCESSING", "badge")
        top.addWidget(self.badge, alignment=Qt.AlignmentFlag.AlignTop)
        outer.addLayout(top)

        body = QHBoxLayout()
        body.setSpacing(20)
        left = QVBoxLayout()
        left.setSpacing(18)
        monitor, layout = card()
        row = QHBoxLayout()
        row.addWidget(label("VIEWER MONITOR", "section"))
        row.addStretch()
        self.camera_badge = label("CAMERA OFF", "small")
        row.addWidget(self.camera_badge)
        layout.addLayout(row)
        self.preview = Preview(self.demo)
        layout.addWidget(self.preview, 1)
        stats = QHBoxLayout()
        self.eye_value = label("Ready", "value")
        self.ear_value = label("EAR  —", "small")
        stats.addWidget(self.eye_value)
        stats.addStretch()
        stats.addWidget(self.ear_value)
        layout.addLayout(stats)
        self.progress = QProgressBar()
        self.progress.setRange(0, 1000)
        self.progress.setTextVisible(False)
        self.progress.setFixedHeight(6)
        layout.addWidget(self.progress)
        self.countdown = label(
            "Normal blinks are ignored. Sustained closure pauses your player.", "small", wrap=True
        )
        layout.addWidget(self.countdown)
        left.addWidget(monitor, 1)

        trace_card, trace_layout = card()
        trace_header = QHBoxLayout()
        trace_header.addWidget(label("EYE OPENNESS", "section"))
        trace_header.addStretch()
        trace_header.addWidget(label("RECENT OBSERVATIONS", "small"))
        trace_layout.addLayout(trace_header)
        self.trace = EyeTrace()
        trace_layout.addWidget(self.trace)
        trace_layout.addWidget(
            label("Higher = open eyes     ·     Dashed line = closure threshold", "small")
        )
        left.addWidget(trace_card)
        body.addLayout(left, 3)

        right = QVBoxLayout()
        right.setSpacing(18)
        player_card, player_layout = card()
        player_layout.addWidget(label("PLAYBACK CONNECTION", "section"))
        player_layout.addWidget(label("Choose your player", "value"))
        player_layout.addWidget(
            label("EyePause stays attached to one player while you watch.", "small", wrap=True)
        )
        self.players = QComboBox()
        self.players.setMinimumWidth(300)
        self.players.setSizeAdjustPolicy(
            QComboBox.SizeAdjustPolicy.AdjustToMinimumContentsLengthWithIcon
        )
        self.players.addItem("Select a media session…", None)
        self.players.currentIndexChanged.connect(self._selection_changed)
        player_layout.addWidget(self.players)
        self.media_title = label(
            "Play a video to make its player available here.", "small", wrap=True
        )
        player_layout.addWidget(self.media_title)
        self.playback = label("Waiting for a player", "small")
        player_layout.addWidget(self.playback)
        self.start_button = QPushButton("Start monitoring")
        self.start_button.setObjectName("primary")
        self.start_button.setEnabled(False)
        self.start_button.clicked.connect(self._start)
        self.stop_button = QPushButton("Stop monitoring")
        self.stop_button.setEnabled(False)
        self.stop_button.clicked.connect(lambda: self.engine.request("stop"))
        player_layout.addWidget(self.start_button)
        player_layout.addWidget(self.stop_button)
        if self.demo:
            player_layout.addWidget(
                label(
                    "Eyes and playback are simulated. No real media is controlled.",
                    "small",
                    wrap=True,
                )
            )
        else:
            player_layout.addWidget(
                label(
                    "Chrome / Edge and Windows players that expose media sessions.",
                    "small",
                    wrap=True,
                )
            )
        right.addWidget(player_card)

        options, options_layout = card()
        options_layout.addWidget(label("WATCHING PREFERENCES", "section"))
        grid = QGridLayout()
        grid.setHorizontalSpacing(18)
        grid.setVerticalSpacing(12)
        self.pause_delay = QDoubleSpinBox()
        self.pause_delay.setRange(0.5, 10)
        self.pause_delay.setSingleStep(0.5)
        self.pause_delay.setSuffix(" s")
        self.pause_delay.setValue(self.settings.pause_seconds)
        self.resume_delay = QDoubleSpinBox()
        self.resume_delay.setRange(0.2, 3)
        self.resume_delay.setSingleStep(0.1)
        self.resume_delay.setSuffix(" s")
        self.resume_delay.setValue(self.settings.resume_seconds)
        self.camera_index = QSpinBox()
        self.camera_index.setRange(0, 20)
        self.camera_index.setValue(self.settings.camera_index)
        self.camera_index.setToolTip(
            "0 is usually the default webcam. Stop monitoring to change devices."
        )
        for index, (text, field) in enumerate(
            (
                ("Pause after closure", self.pause_delay),
                ("Resume after opening", self.resume_delay),
                ("Camera index", self.camera_index),
            )
        ):
            grid.addWidget(label(text, "small"), index, 0)
            grid.addWidget(field, index, 1)
            field.valueChanged.connect(self._save_preferences)
        options_layout.addLayout(grid)
        self.preview_toggle = QCheckBox("Show camera preview")
        self.preview_toggle.setChecked(self.settings.preview)
        self.preview_toggle.toggled.connect(self._save_preferences)
        options_layout.addWidget(self.preview_toggle)
        self.calibrate_button = QPushButton("Calibrate for my eyes")
        self.calibrate_button.setEnabled(not self.demo)
        self.calibrate_button.clicked.connect(self._calibrate)
        options_layout.addWidget(self.calibrate_button)
        self.calibration_note = label(
            "Optional · 3 seconds open, then 2 seconds closed.", "small", wrap=True
        )
        options_layout.addWidget(self.calibration_note)
        right.addWidget(options)
        right.addStretch()
        body.addLayout(right, 2)
        outer.addLayout(body, 1)

        self.message = label("Choose a player, then start monitoring.", wrap=True)
        self.message.setMinimumHeight(32)
        outer.addWidget(self.message)
        footer = QHBoxLayout()
        footer.addWidget(label("ON YOUR DEVICE    ·    NO RECORDING    ·    NO ACCOUNT", "small"))
        footer.addStretch()
        footer.addWidget(label(f"EyePause {__version__}  /  VisionPlayground", "small"))
        outer.addLayout(footer)
        scroll.setWidget(workspace)
        self.setCentralWidget(scroll)

    def _tray(self):
        self.tray = QSystemTrayIcon(self.windowIcon(), self)
        menu = QMenu()
        for text, callback in (
            ("Show EyePause", self._show),
            ("Stop monitoring", lambda: self.engine.request("stop")),
            ("Exit", self.shutdown),
        ):
            action = QAction(text, self)
            action.triggered.connect(callback)
            menu.addAction(action)
        self.tray.setContextMenu(menu)
        self.tray.activated.connect(
            lambda reason: (
                self._show() if reason == (QSystemTrayIcon.ActivationReason.Trigger) else None
            )
        )
        if QSystemTrayIcon.isSystemTrayAvailable():
            self.tray.show()

    def _show(self):
        self.showNormal()
        self.raise_()
        self.activateWindow()

    def _start(self):
        token = self.players.currentData()
        if token:
            self.engine.request("start", token)

    def _calibrate(self):
        if self.current.calibrating:
            self.engine.request("calibration_next")
        else:
            self.engine.request("calibrate")

    def _selection_changed(self):
        selected = self.players.currentData()
        session = next((s for s in self.current.sessions if s.token == selected), None)
        self.start_button.setEnabled(
            bool(
                session
                and (session.can_play or session.can_pause)
                and not self.current.monitoring
                and not self.current.calibrating
                and not self.current.busy
                and not self.current.camera_active
            )
        )
        self.media_title.setText(
            session.title if session else "Play a video to find its media session."
        )
        self.playback.setText(session.status.value if session else "Waiting for a player")

    def _save_preferences(self):
        self.settings = replace(
            self.settings,
            camera_index=self.camera_index.value(),
            preview=self.preview_toggle.isChecked(),
            pause_seconds=self.pause_delay.value(),
            resume_seconds=self.resume_delay.value(),
        )
        self.engine.request("settings", self.settings)
        if not self.demo:
            try:
                save_settings(self.settings)
            except OSError as error:
                self.message.setText(
                    f"Preferences apply this session but could not be saved: {error}"
                )

    def _tick(self):
        status = self.engine.status.get()
        if status is None:
            return
        self.current = status
        self.settings = status.settings
        selected = self.players.currentData()
        contents = [
            (self.players.itemData(i), self.players.itemText(i))
            for i in range(1, self.players.count())
        ]
        desired = [(s.token, f"{s.app} · {s.status.value}") for s in status.sessions]
        if desired != contents:
            self.players.blockSignals(True)
            self.players.clear()
            self.players.addItem("Select a media session…", None)
            for token, text in desired:
                self.players.addItem(text, token)
            index = self.players.findData(selected)
            self.players.setCurrentIndex(max(0, index))
            if self.demo and self.players.count() == 2:
                self.players.setCurrentIndex(1)
            self.players.blockSignals(False)
        active = status.monitoring or status.calibrating or status.busy or status.camera_active
        self.players.setEnabled(not active)
        for widget in (self.camera_index, self.pause_delay, self.resume_delay, self.preview_toggle):
            widget.setEnabled(not active)
        self._selection_changed()
        self.stop_button.setEnabled(active)
        self.stop_button.setText("Cancel calibration" if status.calibrating else "Stop monitoring")
        self.calibrate_button.setEnabled(
            not self.demo
            and not status.busy
            and (not status.camera_active or status.calibrating)
            and (not status.monitoring and (not status.calibrating or status.calibration_ready))
        )
        self.calibrate_button.setText(
            ("Use calibration" if status.calibration_phase == "closed" else "Next: close my eyes")
            if status.calibrating
            else "Calibrate for my eyes"
        )
        self.calibration_note.setText(
            (
                f"Step {'1' if status.calibration_phase == 'open' else '2'} of 2 · "
                f"Keep eyes {status.calibration_phase} · {int(status.calibration_progress * 100)}%"
            )
            if status.calibrating
            else "Optional · 3 seconds open, then 2 seconds closed."
        )
        self.eye_value.setText(
            status.eye_state.value
            if status.monitoring
            else ("Calibrating" if status.calibrating else "Ready")
        )
        color = "#E9C888" if status.eye_state == EyeState.CLOSED else ACCENT
        self.eye_value.setStyleSheet(f"color: {color};")
        self.progress.setValue(
            int(
                1000
                * (
                    status.calibration_progress
                    if status.calibrating
                    else status.progress
                    if active
                    else 0
                )
            )
        )
        self.camera_badge.setText(
            "SIMULATED" if self.demo else "CAMERA ON" if status.camera_active else "CAMERA OFF"
        )
        self.message.setText(status.message)
        self.preview.status = status
        self.preview.update()
        if status.monitoring:
            if status.owned_pause:
                detail = "Paused by EyePause · waiting for stable open eyes."
            elif status.eye_state == EyeState.CLOSED:
                remaining = status.settings.pause_seconds * (1 - status.progress)
                detail = f"Sustained closure · pause threshold in {remaining:.1f} s"
            elif status.eye_state == EyeState.OPEN:
                detail = "Eyes open · normal blinks are ignored."
            else:
                detail = "Tracking unavailable · playback stays as it is."
            self.countdown.setText(detail)
        else:
            self.countdown.setText(
                "Normal blinks are ignored. Sustained closure pauses your player."
            )
        tray_state = (
            "Monitoring"
            if status.monitoring
            else "Calibrating"
            if status.calibrating
            else "Camera stopping"
            if status.camera_active
            else "Camera off"
        )
        self.tray.setToolTip(f"EyePause · {tray_state}")
        if status.vision:
            sample = status.vision.observation
            if sample.timestamp > self.last_trace_timestamp:
                self.last_trace_timestamp = sample.timestamp
                valid = (
                    sample.face_count == 1
                    and sample.left_ear is not None
                    and sample.right_ear is not None
                )
                self.trace.values.append(
                    (sample.left_ear + sample.right_ear) / 2 if valid else None
                )
                self.trace.threshold = (
                    status.settings.left_closed + status.settings.right_closed
                ) / 2
                self.trace.update()
                self.ear_value.setText(
                    (
                        f"L {sample.left_ear:.2f}   R {sample.right_ear:.2f}"
                        f"  ·  {sample.processing_ms:.0f} ms"
                    )
                    if valid
                    else "EAR  —"
                )
        else:
            self.ear_value.setText("EAR  —")

    def closeEvent(self, event):
        if self.exiting:
            event.accept()
        elif self.tray.isVisible():
            self.hide()
            event.ignore()
            if not self.seen_tray_notice:
                self.tray.showMessage(
                    "EyePause is in the tray", "Use the tray menu to stop monitoring or exit."
                )
                self.seen_tray_notice = True
        else:
            self.shutdown()
            event.accept()

    def shutdown(self):
        if self.exiting:
            return
        self.exiting = True
        self.timer.stop()
        self.tray.hide()
        self.engine.close()
        QApplication.instance().quit()


def run(*, demo=False, screenshot: Path | None = None, quit_after: float | None = None) -> int:
    app = QApplication.instance() or QApplication(sys.argv[:1])
    app.setApplicationName("EyePause")
    app.setOrganizationName("VisionPlayground")
    configure_application(app)
    app.setQuitOnLastWindowClosed(False)
    window = MainWindow(demo=demo)
    window.show()
    app.aboutToQuit.connect(window.shutdown)
    if screenshot:

        def capture():
            screenshot.parent.mkdir(parents=True, exist_ok=True)
            if not window.grab().save(str(screenshot)):
                QMessageBox.warning(
                    window, "Screenshot unavailable", "Could not save the screenshot."
                )
            window.shutdown()

        QTimer.singleShot(500, window._start)
        QTimer.singleShot(9500, capture)
    if quit_after is not None:
        QTimer.singleShot(max(1, int(quit_after * 1000)), window.shutdown)
    return app.exec()
