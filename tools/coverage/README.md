# Coverage sweeps

> `tools/content-survey` is the other measurement tool and lives outside this directory because it
> asks a different question: not "does the binding match the headers" but "how much of a real
> game's compiled content can it read". Point it at a `Content` folder:
>
> ```
> dotnet run --project tools/content-survey -c Release -- /path/to/a/game/Content
> ```
>
> It reads headers in place, vendors nothing, and asks the loader's own reader resolution rather
> than reimplementing it. Against the compiled content of the XNA 4.0 sample collection on
> 2026-08-30: 517 assets, 498 loaded through CNA's own content loader, 18 naming a type only the
> game's own assembly supplies, **0 needing a built-in reader this binding does not have**, 0
> unreadable. The number that moved was the last one: it was 4 before the nested model pipeline
> landed, and 3 before the song reader did.

These legacy sweeps answer native-binding questions mechanically against the
`libcna/cna` headers. CNA headers are authoritative for native capability and the C ABI;
they are **not** authoritative for the Microsoft XNA managed contract. Use
`tools/api-compat` with XNA reference assemblies for strict XNA metadata comparison. They exist
because prose could not be trusted: a header audit
found **ten** doc comments in this repository asserting the C API could not do
something it could do perfectly well, and three P/Invoke declarations naming
functions that exist in no header at all.

Run them from anywhere; they locate both trees themselves.

```
python3 tools/coverage/sweep.py            # P/Invoke declarations vs headers
python3 tools/coverage/unbound.py          # header functions with no binding
python3 tools/coverage/typesweep.py        # C++ types with no C# counterpart
python3 tools/coverage/md2run.py           # member-level diff, both layers
python3 tools/coverage/runtimecoverage.py  # which compat types have actually executed
python3 tools/coverage/baselinediff.py --from <checkout[@rev]> --to <checkout[@rev]>
```

`baselinediff.py` is the odd one out: it compares two *upstream generations* rather than this
repository against one of them. It exists because the native ABI policy requires an
upstream release-to-release diff before a new CNA generation enters the reviewed matrix in
`eng/cna-native-abi-policy.json`, and that evidence used to be produced by hand. Exit 1 means
something breaking changed; exit 0 is evidence for a matrix entry, not the entry itself.

Constants are compared from CNA's measured `abi_baseline.json` and, for every value an older
baseline did not record (flag bits written `(UINT32_C(1) << n)`, the masks built from them, floating
constants it truncated), from each generation's header text through a small evaluator of exactly the
forms CNA's headers use. Where both exist they must agree, and a value the evaluator cannot read is a
finding, never a skip. `python3 tools/coverage/test_baselinediff.py` holds those rules; the
`upstream-abi-diff` CI job runs it before the diff.

Reviewed differences go in `eng/cna-upstream-abi-allowlist.txt`, one exact finding per line with a
`#` comment recording the decision -- today, the modern GPU layer CNA retired in 0.47.0, none of
which this binding consumes. The allowlist is checked in
both directions: an entry that matches nothing is reported as stale and fails, because a reviewed
exception that has quietly stopped applying is how an allowlist becomes a blindfold. That was
verified by planting an entry naming a constant that never existed.

`runtimecoverage.py` is the only one that measures *running* rather than *presence*, and it
deliberately refuses to report a single number. A flat "N of 223 types have run" was steering the
work badly: most of the compat surface has no native side to exercise -- math and packed-vector
types are managed by design invariant 3, enums are verified by parity tests, an interface runs only
through its implementors -- so counting them as uncovered both understated the position and implied
it could be improved by writing tests that would prove nothing.

The number that means something is native-backed coverage: types whose source names a native call
or holds a handle, and which therefore have an ABI contract no managed test can check.

## Locating the headers

The scripts first inspect repository-relative sibling locations for a checkout containing
`modules/c-api/include/CNA/C/media_library.h`. Override discovery explicitly with:

```
CNA_ROOT=/path/to/a/cna/checkout python3 tools/coverage/sweep.py
```

The optional exported-symbol check uses `CNA_NATIVE_LIBRARY` for one exact file or
`CNA_NATIVE_DIR` for a directory. ELF uses `nm -D`, Mach-O uses `nm -gU`, and PE uses `dumpbin`
or `llvm-nm`; an unavailable platform tool is reported as a skip rather than presented as
cross-platform proof.

**Pick a checkout whose working tree is clean.** A checkout with uncommitted
header edits will happily validate a binding written against a signature that is
still being designed, which is exactly how a fabricated P/Invoke gets in.

## What each one can and cannot see

This matters more than the scripts do. Each is blind to something the next one
catches, which is why there are five rather than one:

| Script | Finds | Cannot see |
| --- | --- | --- |
| `sweep.py` | fabricated declarations, arity drift, versioned structs passed `out` instead of `ref` | anything about XNA coverage |
| `unbound.py` | native routes nothing binds | members with no native counterpart; members onto an already-bound route |
| `typesweep.py` | CNA C++ types with no C# counterpart | strict XNA contract or anything below type level |
| `md2run.py` | CNA/C# name differences on types present in both trees | strict XNA signatures and **missing types** |
| `baselinediff.py` | upstream removals and changes to exports, consumed prototypes, struct layouts, scalar widths, and constant values between two generations | behaviour that keeps its shape -- a route whose implementation reverses its answer diffs clean |

`baselinediff.py` filters *functions* to the 841-odd names this binding imports, because that list
is exact and machine-readable. It deliberately does **not** filter structs, scalars or constants: the
binding's 80 interop structs and its enum-like identities name their native counterparts only in
prose and in the C probe, so a filter there would be a guess, and a guess that hides a layout change
is worse than a report that occasionally names something irrelevant.

`md2.py` is the shared parser (`cpp_public`, `normalise`, `cs_file_members`), not
a script to run on its own.

## These gates have been tested against a planted failure

A gate that has never failed has not been tested. All three checks in `sweep.py` were verified on
2026-08-19 by planting a defect and confirming each fired, then removing it and confirming clean:

| Planted | Reported |
| --- | --- |
| A declaration naming a symbol in no header | `NOT IN HEADERS (1): ['cna_planted_gate_probe_that_does_not_exist']` |
| The same, against the built libraries | `2855 exports, 1 declaration(s) absent` |
| A real symbol declared with one parameter too many | `ARITY MISMATCH (1): [('cna_shader_effect_has_renderer', 3, 2)]` |

`baselinediff.py` was tested the same way on 2026-08-30, against a copy of the 0.19.0 headers with
two defects planted in a consumed route:

| Planted | Reported |
| --- | --- |
| An extra parameter on `cna_game_run` | `consumed export cna_game_run changed prototype` with both signatures, exit 1 |
| `cna_game_run_one_frame` deleted | `consumed export cna_game_run_one_frame is absent from the proposed headers`, exit 1 |

Running it backwards (0.19.0 as `--from`, 0.8.0 as `--to`) also exits 1 and reports 1,643
differences, which is the same check from the other side: an additive-looking diff has to stop
looking additive when the two sides are swapped.

Worth repeating whenever one of them is changed. A green run proves the script executed, not that
it can still see anything.

## Why the parser keys on `CNA_C_API` and not on the name

A doc comment that mentions a route in prose is indistinguishable from a declaration to any pattern
built from the *name*. That is not hypothetical: a route was once bound into this repository under
a name that exists in no header, taken from the sentence above the real declaration — and the grep
that "confirmed" it had the invented name in its own pattern, so it matched the prose. A search
containing its own answer confirms nothing.

The trap is in the material rather than in any one binding, and upstream keeps that prose
deliberately (rewriting every doc sentence that names a route is the wrong trade). So the defence
belongs here: match the declaration syntax, never the identifier.

## Triaging the output

Neither `unbound.py` nor `md2run.py` returns zero, and neither should — most of
the C++ surface is engine-internal. `plan.md`'s "Coverage: how it is measured"
section lists the noise categories with examples.

One warning from experience: **"not XNA 4.0" is the category to distrust.** Every
other category is self-evident from the name. That one is a judgement call, and
when it is wrong it is silently wrong — `GraphicsAdapter.MonitorHandle` sat in it
for a full pass and is a real XNA 4.0 property. Check the actual XNA surface
before filing anything there.

The `CNAEXT` marker in the C++ headers is the filter that makes the member diff
tractable at all: it cut one run's candidate list from 107 to 45.
