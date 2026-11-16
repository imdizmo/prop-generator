"""The floor settings: floor_params.json is the single source. The painter takes its defaults from it, and the app
builds its sliders and hover tooltips from the same file."""
import copy
import json
from pathlib import Path

PATH = Path(__file__).with_name("floor_params.json")
_CACHE = []


def load():
    """The schema entries, in display order (a fresh copy each call)."""
    if not _CACHE:
        _CACHE.append(json.loads(PATH.read_text(encoding="utf-8")))
    return copy.deepcopy(_CACHE[0])


def defaults():
    return {p["key"]: p["default"] for p in load()}


def _num(key, v):
    if isinstance(v, bool) or not isinstance(v, (int, float)):
        raise ValueError(f"{key}: expected a number, got {v!r}")
    return v


def validate(settings):
    """Clamp every value to its range and fill in what is missing. Unknown keys raise ValueError."""
    schema = {p["key"]: p for p in load()}
    for k in settings:
        if k not in schema:
            raise ValueError(f"unknown setting: {k}")
    out = defaults()
    for k, v in settings.items():
        p = schema[k]
        if p["type"] == "bool":
            out[k] = bool(v)
        elif p["type"] == "colour":
            if not isinstance(v, (list, tuple)) or len(v) != 3:
                raise ValueError(f"{k}: expected three numbers")
            out[k] = [min(max(float(_num(k, c)), p["min"]), p["max"]) for c in v]
        else:
            n = min(max(_num(k, v), p["min"]), p["max"])
            out[k] = int(round(n)) if p["type"] == "int" else float(n)
    if out["crease_min"] > out["crease_max"]:
        out["crease_max"] = out["crease_min"]
    return out
