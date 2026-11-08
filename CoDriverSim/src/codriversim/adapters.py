"""Read-only adapter boundary. Simulator objects never enter the risk engine."""

from typing import Protocol

from codriversim.domain import WorldState


class ObservationSource(Protocol):
    def observe(self, timestamp: float) -> WorldState: ...


class MetaDriveTruthSource:
    """Exact simulator data, NOT camera perception.

    Only vehicles assigned to the ego's current lane segment are considered.
    No occlusion handling, adjacent-lane prediction, or junction coverage.
    """

    def __init__(self, env: object) -> None:
        self.env = env

    def observe(self, timestamp: float) -> WorldState:
        from metadrive.component.vehicle.base_vehicle import BaseVehicle

        ego = self.env.agent
        speed = float(ego.speed)
        lane = ego.lane
        if lane is None:
            return WorldState(timestamp, speed)
        ego_long, _ = lane.local_coordinates(ego.position)
        lead = None
        nearest = float("inf")
        for vehicle in self.env.engine.get_objects().values():
            if not isinstance(vehicle, BaseVehicle) or vehicle is ego or vehicle.lane is not lane:
                continue
            vehicle_long, _ = lane.local_coordinates(vehicle.position)
            ahead = float(vehicle_long - ego_long)
            if 0 < ahead < nearest:
                nearest, lead = ahead, vehicle
        if lead is None:
            return WorldState(timestamp, speed)
        gap = max(0.0, nearest - (float(ego.LENGTH) + float(lead.LENGTH)) / 2)
        # Project velocities on each vehicle's lane tangent, including reverse motion.
        import math

        ego_heading = lane.heading_theta_at(ego_long)
        lead_long, _ = lane.local_coordinates(lead.position)
        lead_heading = lane.heading_theta_at(lead_long)
        ego_speed = sum(
            float(v) * direction
            for v, direction in zip(
                ego.velocity, (math.cos(ego_heading), math.sin(ego_heading)), strict=True
            )
        )
        lead_speed = sum(
            float(v) * direction
            for v, direction in zip(
                lead.velocity, (math.cos(lead_heading), math.sin(lead_heading)), strict=True
            )
        )
        return WorldState(timestamp, speed, gap, ego_speed - lead_speed)
