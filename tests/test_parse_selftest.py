"""Tests for scripts/parse_selftest.py — selftest.json parser."""
import sys, json
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent / "scripts"))
from parse_selftest import (
    load_report, failed_mods, classify_failures,
    Report, ModResult, PropertyResult, VisibilityEntry, DuplicateDllEntry,
    KNOWN_CONSUMER_QUIRKS,
)

FIXTURES = Path(__file__).parent / "fixtures"
ALL_PASS = FIXTURES / "selftest_all_pass.json"
WITH_FAILURES = FIXTURES / "selftest_with_failures.json"


# ---------- all-pass fixture ----------

def test_load_all_pass():
    r = load_report(ALL_PASS)
    assert isinstance(r, Report)
    assert r.schema_version == 1
    assert r.started_at == "2026-06-18T02:00:00Z"
    assert len(r.mods) == 1
    assert len(r.visibility) == 1
    assert r.duplicate_dlls == []


def test_all_pass_mod():
    r = load_report(ALL_PASS)
    m = r.mods[0]
    assert m.mod_id == "ExampleMod"
    assert m.done_passed and m.cancel_passed and m.ui_layer_passed and m.preset_passed
    assert m.fatal_error is None
    assert m.all_passed
    assert m.failed_properties == []


def test_all_pass_properties():
    r = load_report(ALL_PASS)
    props = r.mods[0].properties
    assert len(props) == 2
    assert props[0].property_name == "EnableFeatureX"
    assert props[0].kind == "bool"
    assert props[0].round_trip_passed
    assert props[0].failure_reason is None


def test_all_pass_stats():
    r = load_report(ALL_PASS)
    assert r.total_properties == 2
    assert r.passed_properties == 2


def test_all_pass_no_failures():
    r = load_report(ALL_PASS)
    assert failed_mods(r) == []
    assert classify_failures(r) == []


# ---------- with-failures fixture ----------

def test_load_failures():
    r = load_report(WITH_FAILURES)
    assert len(r.mods) == 3
    assert len(r.visibility) == 2
    assert len(r.duplicate_dlls) == 1


def test_fatal_mod():
    r = load_report(WITH_FAILURES)
    fatal = next(m for m in r.mods if m.mod_id == "FatalMod")
    assert fatal.fatal_error is not None
    assert "NullReferenceException" in fatal.fatal_error
    assert not fatal.all_passed


def test_broken_mod_property():
    r = load_report(WITH_FAILURES)
    broken = next(m for m in r.mods if m.mod_id == "BrokenMod")
    assert len(broken.failed_properties) == 1
    fp = broken.failed_properties[0]
    assert fp.property_name == "Volume"
    assert fp.kind == "float"
    assert "fractional" in fp.failure_reason


def test_known_quirk_excluded_from_stats():
    r = load_report(WITH_FAILURES)
    # DismembermentPlus is a known quirk — excluded from non_quirk_mods
    assert "DismembermentPlus" in KNOWN_CONSUMER_QUIRKS
    non_quirk_ids = [m.mod_id for m in r.non_quirk_mods]
    assert "DismembermentPlus" not in non_quirk_ids
    # Only BrokenMod (1 prop) + FatalMod (0 props) counted
    assert r.total_properties == 1
    assert r.passed_properties == 0


def test_failed_mods_excludes_quirks():
    r = load_report(WITH_FAILURES)
    ids = [m.mod_id for m in failed_mods(r)]
    assert "BrokenMod" in ids
    assert "FatalMod" in ids
    assert "DismembermentPlus" not in ids


def test_classify_failures_types():
    r = load_report(WITH_FAILURES)
    items = classify_failures(r)
    types = {i["failure_type"] for i in items}
    assert "fatal" in types
    assert "round_trip" in types
    assert "done" in types
    assert "duplicate_dll" in types


def test_classify_quirk_flagged():
    r = load_report(WITH_FAILURES)
    items = classify_failures(r)
    quirk_items = [i for i in items if i["mod_id"] == "DismembermentPlus"]
    assert all(i["is_quirk"] for i in quirk_items)


def test_classify_dup_dll():
    r = load_report(WITH_FAILURES)
    items = classify_failures(r)
    dll_items = [i for i in items if i["failure_type"] == "duplicate_dll"]
    assert len(dll_items) == 1
    assert "Newtonsoft.Json.dll" in dll_items[0]["display_name"]
    assert "2 copies" in dll_items[0]["detail"]


def test_visibility_entry():
    r = load_report(WITH_FAILURES)
    orphan = next(v for v in r.visibility if v.mod_folder == "OrphanFolder")
    assert not orphan.has_code
    assert not orphan.registered
    assert orphan.registered_ids == []


def test_duplicate_dll_copies():
    r = load_report(WITH_FAILURES)
    dll = r.duplicate_dlls[0]
    assert dll.dll_name == "Newtonsoft.Json.dll"
    assert len(dll.copies) == 2
    assert dll.copies[0].size == 696320
    assert dll.copies[0].version == "13.0.1"


# ---------- round-trip: serialise & reload ----------

def test_round_trip(tmp_path):
    r = load_report(ALL_PASS)
    raw = json.loads(ALL_PASS.read_text(encoding="utf-8"))
    out = tmp_path / "rt.json"
    out.write_text(json.dumps(raw), encoding="utf-8")
    r2 = load_report(out)
    assert r2.mods[0].mod_id == r.mods[0].mod_id
    assert r2.total_properties == r.total_properties


if __name__ == "__main__":
    import pytest
    pytest.main([__file__, "-v"])
