# Third-party notices

EyePause is an application in Samuel's VisionPlayground repository. Its eye-closure logic, playback state handling, evaluation tools, and UI integrate these upstream components:

| Component | Upstream | License / notices |
| --- | --- | --- |
| MediaPipe and Face Landmarker model, float16 version 1 | https://github.com/google-ai-edge/mediapipe | Apache License 2.0; Copyright The MediaPipe Authors |
| OpenCV contrib Python distribution | https://github.com/opencv/opencv-python | Distribution includes OpenCV, NumPy, and codec notices; see bundled license files |
| Qt for Python / PySide6 Essentials / Shiboken | https://www.qt.io/qt-for-python | LGPLv3/GPLv3 and commercial alternatives; see upstream and bundled license files |
| PyWinRT | https://github.com/pywinrt/pywinrt | MIT; Copyright David Lechner and contributors |
| NumPy | https://numpy.org | BSD-3-Clause plus bundled library notices |
| PyInstaller | https://pyinstaller.org | GPL with the PyInstaller bootloader exception; see bundled license files |
| Python | https://www.python.org | Python Software Foundation License and included notices |

The portable directory build retains separate dynamically loaded libraries. `THIRD_PARTY_LICENSES.txt` is generated from installed distribution license files and includes the Python license. Preserve that file, runtime files, and library notices when redistributing the portable folder.

The pinned model URL, version, and SHA-256 are recorded in `src/eyepause/assets/model.json`. Model documentation: https://developers.google.com/edge/mediapipe/solutions/vision/face_landmarker. The package contains upstream pretrained models; EyePause does not claim to have trained them.

The historical Haar cascade in `experiments/legacy` retains its original Intel/OpenCV license header. An identical copy is bundled in the package assets for repeatable baseline evaluation. The benchmark does not import or execute the historical media-control scripts.

