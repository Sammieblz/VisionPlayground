"""Evaluation-only truth consumes criteria, never feeds the observer."""

from dataclasses import asdict, dataclass, field

from codriversim.domain import number


@dataclass(frozen=True)
class Criterion:
    name: str
    passed: bool
    critical: bool = False
    applicable: bool = True
    unavoidable: bool = False
    evidence: str = ""

    def __post_init__(self):
        if not isinstance(self.name, str) or not self.name.strip():
            raise ValueError("Criterion needs a name")
        for key in ("passed", "critical", "applicable", "unavoidable"):
            if type(getattr(self, key)) is not bool:
                raise ValueError(f"{key} must be boolean")


@dataclass(frozen=True)
class ScenarioResult:
    """Versioned evaluation contract, kept outside the observer's sensor interface.

    Perception metrics are independent measurements: they never change the driver score.
    Timing keys name their units (for example duration_seconds or latency_p95_ms).
    Missing measurements remain absent or null, never synthesized from the driver score.
    """

    scenario: str
    criteria: tuple[Criterion, ...]
    seed: int = 21
    mode: str = "human_ai"
    perception_metrics: dict[str, float | None] = field(default_factory=dict)
    timing: dict[str, float] = field(default_factory=dict)
    schema_version: int = field(default=2, init=False)

    def __post_init__(self):
        if not isinstance(self.scenario, str) or not self.scenario.strip():
            raise ValueError("Scenario needs a name")
        if type(self.seed) is not int or self.seed < 0:
            raise ValueError("Scenario seed must be a nonnegative integer")
        if self.mode not in {"human_muted", "human_ai", "comma"}:
            raise ValueError("Unknown comparison mode")
        if not all(isinstance(item, Criterion) for item in self.criteria):
            raise ValueError("Scenario criteria must use the Criterion contract")
        object.__setattr__(self, "criteria", tuple(self.criteria))
        for name in ("perception_metrics", "timing"):
            values = getattr(self, name)
            if not isinstance(values, dict):
                raise ValueError(f"{name} must be a mapping")
            for key, value in values.items():
                if not isinstance(key, str) or not key.strip():
                    raise ValueError(f"{name} measurement needs a name")
                if value is None and name == "perception_metrics":
                    continue
                number(value, key, nonnegative=name == "timing")
                if name == "timing" and not key.endswith(("_seconds", "_ms")):
                    raise ValueError("Timing measurement names must declare seconds or ms")
            object.__setattr__(self, name, dict(values))

    def to_dict(self) -> dict:
        applicable = [c for c in self.criteria if c.applicable and not c.unavoidable]
        critical = [c.name for c in applicable if c.critical and not c.passed]
        score = 100 * sum(c.passed for c in applicable) / len(applicable) if applicable else None
        return {
            **asdict(self),
            "score": score,
            "passed": score == 100 and not critical if applicable else None,
            "critical_failures": critical,
            "criteria": [asdict(c) for c in self.criteria],
            "source": "evaluation_only",
            "physics_replay_deterministic": False,
        }


def score_scenario(
    name: str,
    criteria: list[Criterion],
    *,
    seed=21,
    mode="human_ai",
    perception_metrics=None,
    timing=None,
) -> dict:
    return ScenarioResult(
        name,
        tuple(criteria),
        seed,
        mode,
        perception_metrics if perception_metrics is not None else {},
        timing if timing is not None else {},
    ).to_dict()


def detection_metrics(tp: int, fp: int, fn: int, latencies_ms: list[float]) -> dict:
    for value in (tp, fp, fn):
        if type(value) is not int or value < 0:
            raise ValueError("Detection counts must be nonnegative integers")
    for latency in latencies_ms:
        number(latency, "latency")
    ordered = sorted(latencies_ms)
    return {
        "precision": tp / (tp + fp) if tp + fp else None,
        "recall": tp / (tp + fn) if tp + fn else None,
        "latency_p95_ms": ordered[min(len(ordered) - 1, int(len(ordered) * 0.95))]
        if ordered
        else None,
    }
