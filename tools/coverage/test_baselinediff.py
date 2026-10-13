"""Regressions for baselinediff.py's constant comparison (CNA CBIND-159).

Each case writes two small CNA "checkouts" -- public headers, and optionally the baseline CNA would
have recorded for them -- and runs the comparison the gate runs. No CNA checkout or compiler needed:

    python3 tools/coverage/test_baselinediff.py
"""

from __future__ import annotations

import json
from pathlib import Path
import tempfile
import unittest

import baselinediff

FLAGS = """
#include <stdint.h>
typedef uint8_t CNA_Bool;
#define CNA_FALSE ((CNA_Bool)0)
#define CNA_PROBE_FLAG_A (UINT32_C(1) << 0)
#define CNA_PROBE_FLAG_B (UINT32_C(1) << 1)
#define CNA_PROBE_FLAG_C (UINT64_C(1) << 40)
#define CNA_PROBE_ALL                       \\
    (CNA_PROBE_FLAG_A | CNA_PROBE_FLAG_B)
#define CNA_PROBE_COUNT UINT32_C(7)
#define CNA_PROBE_NONE INT32_C(-1)
#define CNA_PROBE_ALIAS CNA_PROBE_COUNT
#define CNA_PROBE_PI 3.14159274F
#define CNA_PROBE_ZONE (7849.0f / 32768.0f)
#define CNA_PROBE_NAME "Probe"
#define CNA_PROBE_GUARD_H
"""


class Checkout:
    def __init__(self, root: Path, headers: str, baseline: dict | None = None) -> None:
        header_dir = root / baselinediff.HEADER_SUBPATH
        header_dir.mkdir(parents=True)
        (header_dir / "cna.h").write_text(headers, encoding="utf-8")
        if baseline is not None:
            path = root / baselinediff.BASELINE_SUBPATH
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps(baseline), encoding="utf-8")
        self.source = baselinediff.Source(str(root), [])


class ConstantDiffTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        self.count = 0

    def tearDown(self) -> None:
        self.directory.cleanup()

    def checkout(self, headers: str, baseline: dict | None = None):
        self.count += 1
        return Checkout(self.root / str(self.count), headers, baseline).source

    def diff(self, old_headers: str, new_headers: str, old_baseline=None, new_baseline=None):
        findings: list[str] = []
        baselinediff._diff_constants(self.checkout(old_headers, old_baseline),
                                     self.checkout(new_headers, new_baseline), findings, [])
        return findings

    def test_header_values_follow_c(self):
        integers, floats, unevaluable = baselinediff._header_constants(
            self.checkout(FLAGS).header_dir)
        self.assertEqual(unevaluable, {})
        self.assertEqual(integers, {
            "CNA_FALSE": 0, "CNA_PROBE_FLAG_A": 1, "CNA_PROBE_FLAG_B": 2, "CNA_PROBE_FLAG_C": 1 << 40,
            "CNA_PROBE_ALL": 3, "CNA_PROBE_COUNT": 7, "CNA_PROBE_NONE": -1, "CNA_PROBE_ALIAS": 7,
        })
        self.assertEqual(floats, {"CNA_PROBE_PI": 3.1415927410125732, "CNA_PROBE_ZONE": 7849.0 / 32768.0})
        wrapped, _, _ = baselinediff._header_constants(self.checkout(
            "#define CNA_PROBE_WRAP (UINT32_C(0) - UINT32_C(1))\n"
            "#define CNA_PROBE_BYTE ((CNA_Byte)257)\ntypedef uint8_t CNA_Byte;\n").header_dir)
        self.assertEqual(wrapped, {"CNA_PROBE_WRAP": 0xFFFFFFFF, "CNA_PROBE_BYTE": 1})

    def test_an_unchanged_header_has_no_finding(self):
        self.assertEqual(self.diff(FLAGS, FLAGS), [])

    def test_a_flag_that_moves_to_another_bit_is_found(self):
        moved = FLAGS.replace("CNA_PROBE_FLAG_B (UINT32_C(1) << 1)", "CNA_PROBE_FLAG_B (UINT32_C(1) << 2)")
        self.assertEqual(self.diff(FLAGS, moved), [
            "integer constants: CNA_PROBE_ALL changed 3 -> 5",
            "integer constants: CNA_PROBE_FLAG_B changed 2 -> 4",
        ])

    def test_a_removed_flag_is_found(self):
        removed = FLAGS.replace("#define CNA_PROBE_FLAG_C (UINT64_C(1) << 40)\n", "")
        self.assertEqual(self.diff(FLAGS, removed), ["integer constants: CNA_PROBE_FLAG_C was removed"])

    def test_a_changed_mask_is_found(self):
        narrowed = FLAGS.replace("(CNA_PROBE_FLAG_A | CNA_PROBE_FLAG_B)", "(CNA_PROBE_FLAG_A)")
        self.assertEqual(self.diff(FLAGS, narrowed), ["integer constants: CNA_PROBE_ALL changed 3 -> 1"])

    def test_ordinary_and_floating_constants_still_compare(self):
        changed = FLAGS.replace("UINT32_C(7)", "UINT32_C(8)").replace("3.14159274F", "3.0F")
        self.assertEqual(self.diff(FLAGS, changed), [
            "integer constants: CNA_PROBE_ALIAS changed 7 -> 8",
            "integer constants: CNA_PROBE_COUNT changed 7 -> 8",
            "floating constants: CNA_PROBE_PI changed 3.1415927410125732 -> 3.0",
        ])

    def test_a_flag_an_old_baseline_never_recorded_is_found(self):
        # A baseline from before CBIND-159: no flag bit, no mask, the float truncated.
        old_baseline = {"integers": {"CNA_PROBE_COUNT": 7, "CNA_PROBE_NONE": -1, "CNA_PROBE_ALIAS": 7,
                                     "CNA_PROBE_PI": 3}}
        new_headers = FLAGS.replace("#define CNA_PROBE_FLAG_C (UINT64_C(1) << 40)\n", "")
        new_baseline = {"integers": {"CNA_FALSE": 0, "CNA_PROBE_FLAG_A": 1, "CNA_PROBE_FLAG_B": 2,
                                     "CNA_PROBE_ALL": 3, "CNA_PROBE_COUNT": 7, "CNA_PROBE_NONE": -1,
                                     "CNA_PROBE_ALIAS": 7},
                        "floats": {"CNA_PROBE_PI": 3.1415927410125732, "CNA_PROBE_ZONE": 7849.0 / 32768.0}}
        self.assertEqual(self.diff(FLAGS, new_headers, old_baseline, new_baseline),
                         ["integer constants: CNA_PROBE_FLAG_C was removed"])

    def test_a_baseline_the_headers_disagree_with_is_a_finding(self):
        findings = self.diff(FLAGS, FLAGS, None, {"integers": {"CNA_PROBE_FLAG_B": 4}})
        self.assertEqual(len(findings), 2)
        self.assertIn("CNA_PROBE_FLAG_B evaluates to 2 from the headers but CNA's baseline measured "
                      "integer 4", findings[0])
        self.assertEqual(findings[1], "integer constants: CNA_PROBE_FLAG_B changed 2 -> 4")

    def test_a_value_outside_the_grammar_is_reported_not_skipped(self):
        for value in ("(~UINT32_C(0))", "(UINT32_C(1) << 32)", "CNA_PROBE_SELF", "SOME_MACRO(1)",
                      "(UINT32_C(6) / UINT32_C(2))"):
            with self.subTest(value=value):
                findings = self.diff(FLAGS, FLAGS + f"#define CNA_PROBE_SELF {value}\n")
                self.assertEqual(len(findings), 1, findings)
                self.assertIn("constant CNA_PROBE_SELF could not be evaluated from the headers", findings[0])

    def test_a_value_the_baseline_records_is_used_when_the_headers_cannot_say(self):
        headers = FLAGS + "#define CNA_PROBE_ODD (~UINT32_C(0))\n"
        self.assertEqual(self.diff(headers, headers, {"integers": {"CNA_PROBE_ODD": 0xFFFFFFFF}},
                                   {"integers": {"CNA_PROBE_ODD": 0xFFFFFFFE}}),
                         ["integer constants: CNA_PROBE_ODD changed 4294967295 -> 4294967294"])


if __name__ == "__main__":
    unittest.main()
