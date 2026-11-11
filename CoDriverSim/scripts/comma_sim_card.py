"""Load stock card with the agreed virtual vehicle's physical parameter adapter.

The CAN transport remains the official Honda simulator's transport. This is not
a Mercedes CAN port. No model, controller, event, or engagement check is replaced.
"""

import json
import os
from pathlib import Path


def install_adapter(config):
    from comma_vehicle_config import configure_params
    from openpilot.selfdrive.car import scale_rot_inertia, scale_tire_stiffness
    from openpilot.selfdrive.car.honda.interface import CarInterface

    original = CarInterface.get_params

    def get_params(cls, *args, **kwargs):
        return configure_params(
            original(*args, **kwargs), config, scale_rot_inertia, scale_tire_stiffness
        )

    CarInterface.get_params = classmethod(get_params)
    return CarInterface


def main():
    if os.environ.get("SIMULATION") != "1" or not os.environ.get("OPENPILOT_PREFIX", "").startswith(
        "codriver_unity_"
    ):
        raise RuntimeError("This adapter requires an isolated CoDriverSim session")
    config = json.loads(Path(os.environ["CODRIVER_VEHICLE_CONFIG"]).read_text())
    install_adapter(config)
    from openpilot.selfdrive.car.card import main as stock_main

    stock_main()
