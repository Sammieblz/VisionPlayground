"""Local input validation; this package has no shared runtime dependency."""

import math
from numbers import Real


def mapping(value, name: str) -> dict:
    if not isinstance(value, dict):
        raise ValueError(f"{name} must be an object.")
    return value


def sequence(value, name: str) -> list:
    if isinstance(value, (str, bytes, dict)) or value is None:
        raise ValueError(f"{name} must be a sequence.")
    try:
        return list(value)
    except TypeError as error:
        raise ValueError(f"{name} must be a sequence.") from error


def number(value, name: str) -> float:
    if isinstance(value, bool) or not isinstance(value, Real):
        raise ValueError(f"{name} must be a finite number.")
    try:
        result = float(value)
    except (OverflowError, ValueError) as error:
        raise ValueError(f"{name} must be a finite number.") from error
    if not math.isfinite(result):
        raise ValueError(f"{name} must be a finite number.")
    return result


def integer(value, name: str, *, minimum: int = 0) -> int:
    if type(value) is not int or value < minimum:
        raise ValueError(f"{name} must be an integer >= {minimum}.")
    return value


def text(value, name: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{name} must be a nonempty string.")
    return value.strip()


def point(value, name: str) -> tuple[float, float]:
    values = sequence(value, name)
    if len(values) != 2:
        raise ValueError(f"{name} must have two coordinates.")
    return tuple(number(v, name) for v in values)


def box(value, name: str) -> tuple[float, float, float, float]:
    values = sequence(value, name)
    if len(values) != 4:
        raise ValueError(f"{name} must be [left, top, right, bottom].")
    left, top, right, bottom = (number(v, name) for v in values)
    if left >= right or top >= bottom:
        raise ValueError(f"{name} must have positive width and height.")
    return left, top, right, bottom
