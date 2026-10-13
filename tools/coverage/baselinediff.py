"""Upstream CNA ABI release-to-release diff, restricted to what this binding consumes.

`sweep.py` answers "does the current binding match the current headers". This answers the
different question the native ABI policy asks before a new CNA generation may enter the reviewed
matrix in `eng/cna-native-abi-policy.json`: **what changed upstream between the generation the
matrix already accepts and the one being proposed, and does any of it reach the surface this
binding actually consumes.**

Two evidence paths are compared, because neither alone is sufficient:

* **Header prototypes** are the C authority for signatures. A function can change its return type,
  parameter types, or arity without changing its exported name, and an exported-symbol list cannot
  see that.
* **CNA's own `tools/c-api/abi_baseline.json`** is the authority for struct sizes/alignments/field
  offsets, scalar widths, named integer constants, and string constants. Header text cannot
  cheaply answer what `sizeof(CNA_GameCreateInfo)` is on this platform; the upstream baseline was
  measured by a compiler.

Constants are also **evaluated from each side's header text**, because a baseline is frozen in
the commit that recorded it: until CNA CBIND-159 (ABI 0.47.0) the baseline skipped every value
written with a leading parenthesis -- all flag bits `(UINT32_C(1) << n)` and the masks built from
them -- and recorded floating constants truncated, so a removed or moved flag in an older
generation is visible nowhere else. The evaluator understands exactly the forms CNA's public
headers use (integer and floating literals, `[U]INTn_C(...)`, casts to a fixed-width or CNA scalar
type, other `CNA_` constants, parentheses, unary `-`, and binary `|`, `<<`, `+`, `-`, and `/` between
floating values) with C's types and unsigned wrap-around, never runs header text as code, and
reports any value it cannot evaluate as a finding rather than skipping it. Where a baseline does
record a value, the two must agree, so the evaluator is checked against the compiler on every run.

Struct, scalar, constant, and string changes are reported for the *whole* upstream surface rather
than filtered to a consumed subset. That is deliberate and conservative: this binding declares 80
interop structs and several enum-like identities whose native counterparts are named only in prose
and in the C probe, so a filter here would be a guess, and a guess that silently hides a layout
change is worse than a report that occasionally names something irrelevant. Function prototypes
*are* filtered, because the imported entry points are an exact, machine-readable list.

Usage:

    python3 tools/coverage/baselinediff.py --from <source> --to <source>

where each source is a CNA checkout directory, optionally suffixed with a git revision:

    python3 tools/coverage/baselinediff.py \\
      --from ../cna@599d14e54 --to ../cna \\
      --allowlist eng/cna-upstream-abi-allowlist.txt

A difference that has been reviewed and adjudicated -- CNA removing renderers this binding does not
name, say -- goes in an allowlist file passed with ``--allowlist``, one exact finding (or one ``*``
glob over a reviewed family of findings) per line with a ``#`` comment saying who decided and why. The allowlist is checked in both directions: an entry
that does not match anything is reported as stale and fails, because a reviewed exception that has
silently stopped applying is how an allowlist turns into a blindfold.

Exit codes: 0 when nothing the policy calls breaking changed, 1 when something did, 2 for a bad
invocation. A zero exit is evidence for a matrix entry; it is not the matrix entry itself, and it
says nothing about behavior that keeps its shape.
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import os
import re
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

from paths import REPO_ROOT

HEADER_SUBPATH = Path("modules/c-api/include/CNA/C")
BASELINE_SUBPATH = Path("tools/c-api/abi_baseline.json")
NATIVE_SOURCES = sorted((REPO_ROOT / "src/CNA.Interop").glob("Native*.cs"))

_BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)
_LINE_COMMENT = re.compile(r"//[^\n]*")
_DECLARATION = re.compile(
    r"CNA_C_API\s+([^;{]*?)\b(cna_[A-Za-z0-9_]+)\s*\(([^;]*?)\)\s*;", re.S
)
_IMPORT = re.compile(r"internal\s+static\s+(?:unsafe\s+)?partial\s+\S+\s+(cna_[a-z0-9_]+)\s*\(")
# A function-like macro has its `(` directly after the name, so the blank required here excludes it.
_DEFINE = re.compile(r"^[ \t]*#[ \t]*define[ \t]+(CNA_[A-Za-z0-9_]+)[ \t]+([^\n]*\S)", re.M)
_SCALAR_TYPEDEF = re.compile(r"^[ \t]*typedef[ \t]+([a-z0-9_]+)[ \t]+(CNA_[A-Za-z0-9_]+)[ \t]*;", re.M)
_TOKEN = re.compile(
    r"\s*(?:(?P<float>(?:\d+\.\d*|\.\d+)(?:[eE][+-]?\d+)?[fF]?|\d+[eE][+-]?\d+[fF]?|\d+[fF])"
    r"|(?P<int>0[xX][0-9A-Fa-f]+|\d+)(?P<suffix>[uU]?[lL]{0,2}[uU]?)(?![A-Za-z0-9_.])"
    r"|(?P<name>[A-Za-z_][A-Za-z0-9_]*)|(?P<op><<|[|+\-/()]))"
)
VERSION_CONSTANTS = (
    "CNA_ABI_VERSION",
    "CNA_ABI_VERSION_MAJOR",
    "CNA_ABI_VERSION_MINOR",
    "CNA_ABI_VERSION_PATCH",
)
# Value macros that are not numbers: the linkage attribute, and the version constants, which the
# diff excludes for its own reason below (CNA_ABI_VERSION is a function-like macro's expansion).
_NOT_NUMBERS = {"CNA_C_API", *VERSION_CONSTANTS}
# (bits, unsigned) of the integer kinds a CNA header can produce on the LP64 targets the baseline
# is measured on; float and double are the floating kinds.
_INTEGER_KINDS = {
    "int8_t": (32, False), "int16_t": (32, False), "int32_t": (32, False), "int64_t": (64, False),
    "uint8_t": (32, False), "uint16_t": (32, False), "uint32_t": (32, True), "uint64_t": (64, True),
}
_WIDTH_OF = {"int8_t": 8, "int16_t": 16, "uint8_t": 8, "uint16_t": 16}
_LITERAL_MACROS = {f"{'U' if unsigned else ''}INT{bits}_C": (bits, unsigned)
                   for bits in (8, 16, 32, 64) for unsigned in (False, True)}


class Unevaluable(Exception):
    """A header value outside the grammar this evaluator understands."""


def _float32(value: float) -> float:
    return struct.unpack("f", struct.pack("f", value))[0]


def _integer(value: int, bits: int, unsigned: bool) -> tuple:
    if unsigned:
        return (value % (1 << bits), (bits, True))
    if not -(1 << (bits - 1)) <= value < (1 << (bits - 1)):
        raise Unevaluable(f"signed {bits}-bit overflow")
    return (value, (bits, False))


def _common_kind(left, right):
    """C's usual arithmetic conversions over the kinds above."""
    if "double" in (left, right):
        return "double"
    if "float" in (left, right):
        return "float" if left == right else "double"
    bits = max(left[0], right[0])
    unsigned = (left[1] and left[0] == bits) or (right[1] and right[0] == bits)
    return (bits, unsigned)


class _Evaluator:
    """Precedence climbing over the token stream of one macro value; never `eval`."""

    PRECEDENCE = {"|": 1, "<<": 2, "+": 3, "-": 3, "/": 4}

    def __init__(self, text: str, resolve, typedefs: dict) -> None:
        self.tokens = []
        position = 0
        while position < len(text):
            match = _TOKEN.match(text, position)
            if match is None or match.end() == position:
                if text[position:].strip():
                    raise Unevaluable(f"unexpected text {text[position:].strip()!r}")
                break
            self.tokens.append(match)
            position = match.end()
        self.index = 0
        self.resolve = resolve
        self.typedefs = typedefs

    def peek(self, offset: int = 0):
        index = self.index + offset
        return self.tokens[index] if index < len(self.tokens) else None

    def text(self, token) -> str:
        return token.group(0).strip() if token is not None else ""

    def take(self, expected: str | None = None):
        token = self.peek()
        if token is None or (expected is not None and self.text(token) != expected):
            raise Unevaluable(f"expected {expected or 'a value'}")
        self.index += 1
        return token

    def evaluate(self):
        result = self.expression(1)
        if self.peek() is not None:
            raise Unevaluable(f"unexpected {self.text(self.peek())!r}")
        return result

    def expression(self, minimum: int):
        left = self.unary()
        while True:
            operator = self.text(self.peek())
            precedence = self.PRECEDENCE.get(operator)
            if precedence is None or precedence < minimum or self.peek().group("op") is None:
                return left
            self.take()
            left = self.binary(operator, left, self.expression(precedence + 1))

    def binary(self, operator: str, left, right):
        (a, left_kind), (b, right_kind) = left, right
        kind = _common_kind(left_kind, right_kind)
        if kind in ("float", "double"):
            if operator not in ("+", "-", "/"):
                raise Unevaluable(f"{operator} on a floating value")
            if operator == "/" and b == 0:
                raise Unevaluable("division by zero")
            value = a + b if operator == "+" else a - b if operator == "-" else a / b
            return (_float32(value) if kind == "float" else value, kind)
        if operator == "/":
            raise Unevaluable("integer division")
        if operator == "<<":
            # The result has the left operand's type, and C leaves an out-of-range count undefined.
            if not 0 <= b < left_kind[0]:
                raise Unevaluable(f"shift by {b}")
            return _integer(a << b, *left_kind)
        value = a | b if operator == "|" else a + b if operator == "+" else a - b
        return _integer(value, *kind)

    def unary(self):
        token = self.peek()
        if token is not None and self.text(token) == "-":
            self.take()
            value, kind = self.unary()
            return (-value, kind) if kind in ("float", "double") else _integer(-value, *kind)
        return self.primary()

    def primary(self):
        token = self.take()
        if token.group("float"):
            literal = token.group("float")
            if literal[-1] in "fF":
                return (_float32(float(literal[:-1])), "float")
            return (float(literal), "double")
        if token.group("int"):
            return self.literal(int(token.group("int"), 0), token.group("int"), token.group("suffix"))
        if token.group("name"):
            name = token.group("name")
            if name in _LITERAL_MACROS:
                bits, unsigned = _LITERAL_MACROS[name]
                self.take("(")
                value, kind = self.expression(1)
                self.take(")")
                if kind in ("float", "double"):
                    raise Unevaluable(f"{name} of a floating value")
                return _integer(value, *_INTEGER_KINDS[f"{'u' if unsigned else ''}int{bits}_t"])
            if name.startswith("CNA_"):
                return self.resolve(name)
            raise Unevaluable(f"unknown name {name}")
        # `(`: a cast when a type name follows, a parenthesized expression otherwise.
        name = self.peek().group("name") if self.peek() is not None else None
        target = self.typedefs.get(name, name)
        if target in _INTEGER_KINDS or target in ("float", "double"):
            if self.text(self.peek(1)) == ")":
                self.take()
                self.take(")")
                return self.cast(target, self.unary())
        value = self.expression(1)
        self.take(")")
        return value

    def literal(self, value: int, spelling: str, suffix: str):
        unsigned = "u" in suffix.lower()
        long = "l" in suffix.lower()
        decimal = not spelling.lower().startswith("0x")
        # C's literal typing: the first of these that holds the value.
        for bits, is_unsigned in ((32, False), (32, True), (64, False), (64, True)):
            if (long and bits == 32) or (unsigned and not is_unsigned) or (
                    decimal and not unsigned and is_unsigned):
                continue
            if value < (1 << (bits - (0 if is_unsigned else 1))):
                return (value, (bits, is_unsigned))
        raise Unevaluable(f"literal {spelling}{suffix} is too large")

    def cast(self, target: str, operand):
        value, kind = operand
        if target in ("float", "double"):
            value = float(value)
            return (_float32(value) if target == "float" else value, target)
        if kind in ("float", "double"):
            if value != int(value):
                raise Unevaluable("cast truncates a floating value")
            value = int(value)
        width = _WIDTH_OF.get(target)
        if width is not None:
            # The narrow types are converted, then promoted back to int.
            value %= 1 << width
            if not target.startswith("u") and value >= 1 << (width - 1):
                value -= 1 << width
        bits, unsigned = _INTEGER_KINDS[target]
        return _integer(value, bits, unsigned)


def _header_constants(header_dir: Path) -> tuple[dict, dict, dict]:
    """(integers, floats, unevaluable name -> reason) of the number-valued macros of the headers."""
    definitions: dict[str, set[str]] = {}
    typedefs: dict[str, str] = {}
    for path in sorted(header_dir.rglob("*.h")):
        text = path.read_text(encoding="utf-8", errors="replace")
        text = _LINE_COMMENT.sub("", _BLOCK_COMMENT.sub("", text))
        text = re.sub(r"\\\n[ \t]*", " ", text)
        for name, value in _DEFINE.findall(text):
            definitions.setdefault(name, set()).add(" ".join(value.split()))
        for base, name in _SCALAR_TYPEDEF.findall(text):
            typedefs[name] = base

    values: dict[str, tuple] = {}
    unevaluable: dict[str, str] = {}
    resolving: set[str] = set()

    def resolve(name: str):
        if name in values:
            return values[name]
        if name in unevaluable:
            raise Unevaluable(f"{name} could not be evaluated")
        spellings = definitions.get(name)
        if not spellings:
            raise Unevaluable(f"{name} is not a constant of these headers")
        if len(spellings) > 1:
            raise Unevaluable(f"{name} has {len(spellings)} different definitions")
        if name in resolving:
            raise Unevaluable(f"{name} refers to itself")
        resolving.add(name)
        try:
            values[name] = _Evaluator(next(iter(spellings)), resolve, typedefs).evaluate()
        finally:
            resolving.discard(name)
        return values[name]

    for name, spellings in sorted(definitions.items()):
        value = next(iter(spellings))
        if name in _NOT_NUMBERS or value.startswith('"') or value.startswith("CNA_COLOR_RGBA("):
            continue
        try:
            resolve(name)
        except Unevaluable as reason:
            unevaluable[name] = str(reason)
    integers = {name: value for name, (value, kind) in values.items()
                if kind not in ("float", "double") and name not in _NOT_NUMBERS}
    floats = {name: value for name, (value, kind) in values.items() if kind in ("float", "double")}
    return integers, floats, unevaluable


def _constants(source, findings: list[str], notes: list[str]) -> tuple[dict, dict]:
    """A side's integer and floating constants: CNA's measurement, completed from its headers."""
    integers, floats, unevaluable = _header_constants(source.header_dir)
    baseline = source.baseline or {}
    measured_integers = {name: value for name, value in (baseline.get("integers") or {}).items()
                         if name not in VERSION_CONSTANTS}
    measured_floats = baseline.get("floats") or {}
    truncated = sorted(name for name in measured_integers if name in floats)
    checked = 0
    for measured, evaluated, label in ((measured_integers, integers, "integer"),
                                       (measured_floats, floats, "floating")):
        for name, value in sorted(measured.items()):
            if name in truncated or name not in evaluated:
                continue
            checked += 1
            if evaluated[name] != value:
                findings.append(
                    f"{source.spec}: {name} evaluates to {evaluated[name]!r} from the headers but "
                    f"CNA's baseline measured {label} {value!r}")
    for name, reason in sorted(unevaluable.items()):
        if name not in measured_integers and name not in measured_floats:
            findings.append(
                f"{source.spec}: constant {name} could not be evaluated from the headers ({reason}), "
                "and the baseline does not record it")
    merged_integers = {**integers, **{name: value for name, value in measured_integers.items()
                                      if name not in truncated}}
    merged_floats = {**floats, **measured_floats}
    notes.append(
        f"constants of {source.spec}: {len(merged_integers)} integer, {len(merged_floats)} floating; "
        f"{checked} cross-checked against the baseline, "
        f"{len([n for n in integers if n not in measured_integers])} integer and "
        f"{len([n for n in floats if n not in measured_floats])} floating known only from the "
        f"headers, {len(truncated)} floating recorded truncated by the baseline")
    return merged_integers, merged_floats


class Source:
    """One side of the comparison: a checkout, optionally at a git revision."""

    def __init__(self, spec: str, stack: list) -> None:
        self.spec = spec
        checkout, _, revision = spec.partition("@")
        self.root = Path(checkout).expanduser().resolve()
        self.revision = revision or None
        if not self.root.is_dir():
            raise SystemExit(f"{spec}: {self.root} is not a directory.")

        if self.revision is None:
            self.header_dir = self.root / HEADER_SUBPATH
            baseline_path = self.root / BASELINE_SUBPATH
            self.baseline = _read_json(baseline_path) if baseline_path.is_file() else None
        else:
            extracted = Path(tempfile.mkdtemp(prefix="cna-baselinediff-"))
            stack.append(extracted)
            _extract(self.root, self.revision, HEADER_SUBPATH, extracted)
            self.header_dir = extracted / HEADER_SUBPATH
            self.baseline = _read_git_json(self.root, self.revision, BASELINE_SUBPATH)

        if not self.header_dir.is_dir():
            raise SystemExit(f"{spec}: no {HEADER_SUBPATH} below {self.root}.")
        self.prototypes = _prototypes(self.header_dir)

    @property
    def abi_version(self) -> str:
        if self.baseline and "abi_version" in self.baseline:
            version = self.baseline["abi_version"]
            return f"{version['major']}.{version['minor']}.{version['patch']}"
        return _header_abi_version(self.header_dir)

    def __str__(self) -> str:
        return f"{self.spec} (ABI {self.abi_version}, {len(self.prototypes)} exports)"


def _read_json(path: Path):
    with path.open(encoding="utf-8") as handle:
        return json.load(handle)


def _read_git_json(root: Path, revision: str, subpath: Path):
    completed = subprocess.run(
        ["git", "-C", str(root), "show", f"{revision}:{subpath.as_posix()}"],
        capture_output=True,
        text=True,
        check=False,
    )
    return json.loads(completed.stdout) if completed.returncode == 0 else None


def _extract(root: Path, revision: str, subpath: Path, destination: Path) -> None:
    archive = subprocess.run(
        ["git", "-C", str(root), "archive", revision, subpath.as_posix()],
        capture_output=True,
        check=False,
    )
    if archive.returncode != 0:
        raise SystemExit(
            f"git archive {revision}:{subpath} failed in {root}: "
            f"{archive.stderr.decode(errors='replace').strip()}"
        )
    extract = subprocess.run(
        ["tar", "-x", "-C", str(destination)], input=archive.stdout, capture_output=True, check=False
    )
    if extract.returncode != 0:
        raise SystemExit(f"Extracting {revision}:{subpath} failed: {extract.stderr.decode()}")


def _prototypes(header_dir: Path) -> dict[str, str]:
    """name -> normalised `return-type|parameter-list`, parsed from the declaration syntax.

    Keying on `CNA_C_API` rather than on the identifier is the same rule `sweep.py` documents: a
    doc comment that names a route in prose is indistinguishable from a declaration to any pattern
    built from the name.
    """
    found: dict[str, str] = {}
    for path in sorted(header_dir.rglob("*.h")):
        text = path.read_text(encoding="utf-8", errors="replace")
        text = _LINE_COMMENT.sub("", _BLOCK_COMMENT.sub("", text))
        for match in _DECLARATION.finditer(text):
            returns, name, parameters = match.groups()
            found[name] = " ".join(f"{returns}|{parameters}".split())
    return found


def _header_abi_version(header_dir: Path) -> str:
    abi = header_dir / "abi.h"
    if not abi.is_file():
        return "unknown"
    text = abi.read_text(encoding="utf-8", errors="replace")
    parts = []
    for field in ("MAJOR", "MINOR", "PATCH"):
        match = re.search(rf"#define\s+CNA_ABI_VERSION_{field}\s+UINT32_C\((\d+)\)", text)
        parts.append(match.group(1) if match else "?")
    return ".".join(parts)


def _consumed_symbols() -> set[str]:
    return {name for path in NATIVE_SOURCES for name in _IMPORT.findall(path.read_text(encoding="utf-8"))}


def _diff_mapping(old, new, label: str, findings: list[str], notes: list[str]) -> None:
    """Report removals and value changes in a name -> value mapping; count additions only."""
    if old is None or new is None:
        notes.append(f"{label}: not measured on both sides; no evidence either way")
        return
    removed = sorted(key for key in old if key not in new)
    changed = sorted(key for key in old if key in new and old[key] != new[key])
    added = sum(1 for key in new if key not in old)
    for key in removed:
        findings.append(f"{label}: {key} was removed")
    for key in changed:
        findings.append(f"{label}: {key} changed {old[key]!r} -> {new[key]!r}")
    notes.append(
        f"{label}: {len(old)} -> {len(new)}; {len(removed)} removed, {len(changed)} changed, "
        f"{added} added"
    )


def _diff_constants(old, new, findings: list[str], notes: list[str]) -> None:
    old_integers, old_floats = _constants(old, findings, notes)
    new_integers, new_floats = _constants(new, findings, notes)
    _diff_mapping(old_integers or None, new_integers or None, "integer constants", findings, notes)
    _diff_mapping(old_floats or None, new_floats or None, "floating constants", findings, notes)
    notes.append(
        "integer constants: the four CNA_ABI_VERSION_* values are excluded; a version that "
        "did not change would mean the two sides are the same generation"
    )


def _diff_structs(old, new, findings: list[str], notes: list[str]) -> None:
    if old is None or new is None:
        notes.append("structs: not measured on both sides; no evidence either way")
        return
    changed = 0
    for name, before in sorted(old.items()):
        after = new.get(name)
        if after is None:
            findings.append(f"struct {name} was removed")
            changed += 1
            continue
        if (before["size"], before["align"]) != (after["size"], after["align"]):
            findings.append(
                f"struct {name} size/align {before['size']}/{before['align']} -> "
                f"{after['size']}/{after['align']}"
            )
            changed += 1
            continue
        for field, offset in sorted(before["fields"].items()):
            moved = after["fields"].get(field)
            if moved is None:
                findings.append(f"struct {name}: field {field} was removed")
                changed += 1
            elif moved != offset:
                findings.append(
                    f"struct {name}: field {field} offset/size {offset} -> {moved}"
                )
                changed += 1
    added = sum(1 for name in new if name not in old)
    notes.append(
        f"structs: {len(old)} -> {len(new)}; {changed} breaking difference(s), {added} added"
    )


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Diff two upstream CNA ABI generations across the surface this binding consumes."
    )
    parser.add_argument("--from", dest="old", required=True, help="checkout[@revision] already in the matrix")
    parser.add_argument("--to", dest="new", required=True, help="checkout[@revision] being proposed")
    parser.add_argument("--json", dest="json_output", help="write the machine-readable report here")
    parser.add_argument(
        "--allowlist",
        help="file of reviewed, adjudicated findings to accept; one exact finding per line, "
        "'#' comments allowed",
    )
    arguments = parser.parse_args(argv)

    stack: list[Path] = []
    try:
        old = Source(arguments.old, stack)
        new = Source(arguments.new, stack)

        consumed = _consumed_symbols()
        findings: list[str] = []
        notes: list[str] = []

        missing = sorted(name for name in consumed if name not in new.prototypes)
        for name in missing:
            findings.append(f"consumed export {name} is absent from the proposed headers")

        signature_changes = sorted(
            name
            for name in consumed
            if name in old.prototypes
            and name in new.prototypes
            and old.prototypes[name] != new.prototypes[name]
        )
        for name in signature_changes:
            findings.append(
                f"consumed export {name} changed prototype:\n"
                f"      was: {old.prototypes[name]}\n"
                f"      now: {new.prototypes[name]}"
            )

        unknown = sorted(name for name in consumed if name not in old.prototypes)
        notes.append(
            f"consumed exports: {len(consumed)}; {len(missing)} absent, "
            f"{len(signature_changes)} changed prototype, {len(unknown)} not present in the "
            f"accepted generation"
        )
        notes.append(
            f"all exports: {len(old.prototypes)} -> {len(new.prototypes)}; "
            f"{len([n for n in old.prototypes if n not in new.prototypes])} removed, "
            f"{len([n for n in new.prototypes if n not in old.prototypes])} added"
        )

        # A consumed removal is already reported above with the reason that matters; repeating it
        # here under "not consumed here" would contradict itself.
        removed_any = sorted(
            name
            for name in old.prototypes
            if name not in new.prototypes and name not in consumed
        )
        for name in removed_any:
            findings.append(f"export {name} was removed upstream (not consumed here)")

        old_baseline = old.baseline or {}
        new_baseline = new.baseline or {}
        _diff_structs(old_baseline.get("structs"), new_baseline.get("structs"), findings, notes)
        _diff_mapping(old_baseline.get("scalars"), new_baseline.get("scalars"), "scalars", findings, notes)
        _diff_mapping(old_baseline.get("strings"), new_baseline.get("strings"), "strings", findings, notes)

        _diff_constants(old, new, findings, notes)

        allowed: list[str] = []
        if arguments.allowlist:
            allowlist_path = Path(arguments.allowlist)
            if not allowlist_path.is_file():
                raise SystemExit(f"--allowlist {allowlist_path} is not a file.")
            allowed = [
                line.strip()
                for line in allowlist_path.read_text(encoding="utf-8").splitlines()
                if line.strip() and not line.lstrip().startswith("#")
            ]

        # An entry containing `*` is a glob, so a reviewed family -- "every export removed upstream
        # that this binding does not import" -- is one decision rather than eight hundred lines. A
        # glob is held to the same rule as an exact entry: it must match at least one finding.
        def matches(entry: str, finding: str) -> bool:
            return fnmatch.fnmatchcase(finding, entry) if "*" in entry else finding == entry

        accepted = [f for f in findings if any(matches(entry, f) for entry in allowed)]
        findings = [f for f in findings if f not in accepted]
        stale = [entry for entry in allowed if not any(matches(entry, f) for f in accepted)]
        for entry in stale:
            findings.append(
                f"allowlist entry matched nothing and is stale: {entry}"
            )
        if allowed:
            notes.append(
                f"allowlist: {len(allowed)} reviewed entr(y/ies), {len(accepted)} applied, "
                f"{len(stale)} stale"
            )

        print(f"# from: {old}")
        print(f"# to:   {new}")
        print()
        for note in notes:
            print(f"  {note}")
        print()
        if accepted:
            print(f"REVIEWED AND ALLOWED ({len(accepted)}):")
            for finding in accepted:
                print(f"  - {finding}")
            print()
        if findings:
            print(f"BREAKING DIFFERENCES ({len(findings)}):")
            for finding in findings:
                print(f"  - {finding}")
        else:
            print("BREAKING DIFFERENCES (0): none across exports, prototypes, structs, scalars,")
            print("  integer, floating, and string constants.")

        if arguments.json_output:
            report = {
                "schemaVersion": 1,
                "status": "failed" if findings else "passed",
                "from": {"spec": old.spec, "abiVersion": old.abi_version},
                "to": {"spec": new.spec, "abiVersion": new.abi_version},
                "consumedExportCount": len(consumed),
                "notes": notes,
                "allowed": accepted,
                "findings": findings,
            }
            output = Path(arguments.json_output)
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")

        return 1 if findings else 0
    finally:
        for path in stack:
            subprocess.run(["rm", "-rf", str(path)], check=False)


if __name__ == "__main__":
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    raise SystemExit(main(sys.argv[1:]))
