import json
import pytest

from propgen_painter.params import defaults, load, validate


def max_of(key):
    return next(p["max"] for p in load() if p["key"] == key)


def test_every_param_has_a_real_tooltip():
    for p in load():
        assert len(p["tooltip"]) >= 40 and p["tooltip"].strip().lower() != p["label"].strip().lower()


def test_entries_are_well_formed():
    keys = [p["key"] for p in load()]
    assert len(keys) == len(set(keys)) == 18
    for p in load():
        assert p["kind"] in ("tone", "paint") and p["type"] in ("colour", "float", "int", "bool")
        assert p["label"].strip()
        if p["type"] in ("float", "int"):
            assert p["min"] <= p["default"] <= p["max"] and p["step"] > 0
    assert {p["key"] for p in load() if p["kind"] == "tone"} == {"colour", "contrast", "drift"}
    assert "sym" not in keys


def test_defaults_are_v5():
    d = defaults()
    assert (d["colour"], d["seed"], d["carve_strength"], d["carve_open"], d["gap_scale"]) == ([0.272, 0.188, 0.170], 1, 0.9, 0.6, 0.45)
    assert (d["contrast"], d["drift"], d["carve_density"], d["creases"], d["crease_min"], d["crease_max"]) == (0.65, 0.6, 1.0, 4, 0.5, 1.1)
    assert (d["lit"], d["crest"], d["edge_light"], d["broken_edge"], d["edge_line"], d["side_wobble"], d["meander"]) == (0.08, 0.15, 0.6, True, 0.08, 0.8, 2.0)


def test_defaults_returns_a_fresh_copy():
    defaults()["colour"][0] = 0.9
    assert defaults()["colour"][0] == 0.272


def test_validate_rejects_unknown_and_clamps():
    with pytest.raises(ValueError):
        validate({"nope": 1})
    with pytest.raises(ValueError):
        validate({"sym": True})
    assert validate({"gap_scale": 99})["gap_scale"] == max_of("gap_scale")


def test_validate_fills_defaults_and_coerces():
    v = validate({})
    assert v == defaults()
    v = validate({"creases": 3.6, "broken_edge": 0, "seed": -5})
    assert v["creases"] == 4 and v["broken_edge"] is False and v["seed"] == 0


def test_validate_colour():
    assert validate({"colour": [2, -1, 0.5]})["colour"] == [1.0, 0.0, 0.5]
    with pytest.raises(ValueError):
        validate({"colour": [0.1, 0.2]})


def test_crease_min_never_above_max():
    v = validate({"crease_min": 1.4, "crease_max": 0.3})
    assert v["crease_min"] <= v["crease_max"]


def test_json_is_plain_list():
    from propgen_painter import params
    assert isinstance(json.loads(params.PATH.read_text()), list)


def test_every_param_sits_in_one_of_the_panel_groups():
    groups = [p["group"] for p in load()]
    assert set(groups) == {"Wood", "Carving", "Planks", "Grain", "Edges"}
    # The groups come in this order, the first time each appears.
    assert list(dict.fromkeys(groups)) == ["Wood", "Carving", "Planks", "Grain", "Edges"]
    assert [p["key"] for p in load() if p["group"] == "Wood"] == ["colour", "contrast", "drift", "seed"]
