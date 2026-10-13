# CNA native ABI compatibility contract

This document is the normative CNA.NET consumer policy for the CNA C ABI. The machine-readable
form is [`eng/cna-native-abi-policy.json`](../eng/cna-native-abi-policy.json); the loader implements
policy `cna-cs-native-abi/1` in `CNA.Interop`. Upstream CNA remains the authority for its C ABI and
is not modified by this policy.

## Version meaning

CNA encodes `major.minor.patch` into a `uint32_t` with 16 bits for major and 8 bits each for minor
and patch. The fields do not imply ordinary stable SemVer while the major is zero:

| Field | CNA C ABI meaning | CNA.NET admission consequence |
| --- | --- | --- |
| Major | At ABI 1.x and later, an incompatible change requires a new major. Major 0 identifies the experimental line and provides no same-major compatibility guarantee. | A different major is rejected. Sharing major 0 is necessary but not sufficient. |
| Minor before 1.0 | A reviewed ABI generation. It may be additive or incompatible; CNA 0.3, 0.6, and 0.8 contain documented contract changes. | Each exact minor must have a reviewed matrix entry. There is no `nativeMinor >= 6` rule. |
| Minor at/after 1.0 | CNA's published contract permits additive, backward-compatible changes within the major. | Future consumers may use a major/minimum-minor rule only after the 1.0 contract and evidence are reviewed into a new CNA.NET policy. |
| Patch | ABI-neutral fixes only: no change to exports, prototypes, layouts, values, callbacks, ownership, errors, or documented behavior. | Exact patch releases still require a matrix entry because today's runtime metadata has a version but no ABI-shape fingerprint. |

The upstream installed CMake package's `SameMajorVersion` selection is not used as runtime proof.
It cannot express CNA's documented experimental-0.x exception by itself.

## Reviewed compatibility matrix

| CNA.NET consumer | Native library | CNA.NET result | Evidence |
| --- | --- | --- | --- |
| 0.47.0 | 0.47.0 | Accept | Consumer baseline at CNA `next` `fbd8e64a3`. |
| 0.47.0 | Any other 0.x | Reject | No audited matrix entry. |
| 0.47.0 | Any 1.x+ | Reject | Different ABI major. |

One accepted entry is not a simplification of the policy; it is what the policy produces when the
consumer moves. There is no `>= 0.47` rule and there never was a `>= 0.6` one.

0.47.0 replaced 0.46.0 on 2026-10-13 (CNA `plans/plan_modern_retirement.md` MRT-012). CNA 0.47.0
removes its modern GPU layer: compute, storage buffers and images, indirect draws and GPU timers.
`tools/coverage/baselinediff.py` measured 1419 consumed exports with 0 absent and 0 changed
prototypes. Five exports and two structures were removed, none of them imported or mirrored here.
Twenty-eight integer-constant differences were reviewed in `eng/cna-upstream-abi-allowlist.txt`: two
graphics capabilities and their flag bits 17-18, four renderer features, fourteen renderer limits,
the storage format-usage bits 5-7, the `CNA_RENDERER_FORMAT_USAGE_ALL` mask (8191 -> 7967) and two
`MAXIMUM` sentinels. The six flag findings were first reviewed by hand, because CNA's baseline did not
record a value written `(UINT32_C(1) << n)`; since CNA CBIND-159 the diff evaluates both generations'
headers and reports them itself (`tools/coverage/test_baselinediff.py`). CNA.NET removes `GraphicsCapability.ComputeShaders`
and `IndirectDraw`, their `CnaGraphicsCapability` twins, and the three storage members of both
format-usage enums in the same change; `tools/abi-verify` confirms the rest against the 0.47.0
headers.

0.46.0 replaced 0.44.0 on 2026-10-09 (CNA `plans/plan_apple_m4.md` AM4-217), crossing 0.45.0, which
was never admitted. `tools/coverage/baselinediff.py` measured 1419 consumed exports with 0 absent
and 0 changed prototypes, no struct, scalar or string difference, and five integer-constant
differences, each reviewed in `eng/cna-upstream-abi-allowlist.txt`: RRC-018 retired four renderer
identities (their values stay reserved; a library never reports them and CNA.NET names none) and
AM4-037 added `CNA_NATIVE_WINDOW_SYSTEM_UIKIT`, which moves that family's maximum from 8 to 9 (CNA.NET
does not consume native window handles). The generation is the one the macOS runtime qualification
runs against.

`ed885a8ae` is 0.44.0 as CNA first published it, and carries everything this binding needs from
the earlier lines: CBIND-128/129 (component callbacks), CBIND-130 (packet-reader copy), CBIND-131
(exit-time teardown), CBIND-132 (the canonical exception behind a failure), CBIND-133 (a header
libc++ and so every Emscripten build can compile), CBIND-134 (the host-driven run), CBIND-136..140
(Android, a title-relative content root), CBIND-141 (another thread's calls on the game thread),
CBIND-142..144 (a video frame for SpriteBatch, avatars in a browser, NoSuitableGraphicsDeviceException)
CBIND-145 (the graphics adapters answer the game's own handle), CBIND-146..151, CBIND-152 (the
game thread runs other threads' queued calls while it waits for them) and CBIND-153 (a game lends
its device before its run, as XNA's `ApplyChanges` in a constructor created one) CBIND-154
(sound effects play from any thread) and CBIND-155 (activation handlers can use the device).

## Which CNA this binding pairs with

`CNA_UPSTREAM_COMMIT` in `.github/workflows/build.yml` is the one CNA commit this branch is written
against: today CNA `next` at `fbd8e64a3`, C ABI 0.47.0. CNA's `main` and `develop` still carry 0.46.0
(its v0.1.0 preparation), which this consumer refuses; `next` is CNA's integration branch, and the
pin moves only with a reviewed matrix change. Every CI job that reads CNA reads that commit: the
header gates (`tools/abi-verify`), the upstream diff, and `native-qualified-linux-x64`, which builds
CNA's C API from it (OPENGLES3 with compiled effects, CNAEXT and devices on, siblings pinned beside
it), installs the `CNACApi` component, admits that library with
`scripts/Verify-NativeAbiCompatibility.sh --native-library` and only then runs the integration,
GamerServices, ownership, corpus and package suites against it. No repository variable or secret is
involved, so a run that did not load the library fails rather than being skipped.

### Retired entries

0.6.0, 0.7.0, 0.8.0, 0.19.0, 0.20.0, 0.21.0, 0.35.0, 0.36.0, 0.37.0, 0.38.0, 0.39.0, 0.40.0, 0.41.0, 0.42.0, 0.43.0, 0.44.0 and 0.46.0 were accepted once. 0.6.0-0.8.0
were retired when this binding began importing routes they do not export; 0.19.0, 0.20.0 and 0.21.0
when the next reviewed generation superseded them; 0.35.0 when this binding began importing 0.36.0's
`cna_packet_reader_copy_data_ext`, 0.36.0 when it began importing 0.37.0's exception queries,
0.37.0 when it began importing 0.38.0's `cna_game_run_frame_ext`, 0.38.0 when it began
importing 0.39.0's `cna_game_set_foreign_thread_calls_ext`, 0.39.0 when it began handing the
game's own handle to the graphics-adapter routes, which 0.39.0 refuses, 0.40.0 when it began
importing 0.41.0's `cna_game_run_foreign_thread_calls_ext`, and 0.41.0 when it began reading a
game's device in its constructor once `ApplyChanges` has created it, which 0.41.0 refuses, and
0.42.0 when its game threads' sound effects began playing directly, which 0.42.0 queues, and 0.43.0
when its activation handlers began using the device, which 0.43.0 refuses there. 0.44.0 and 0.46.0
were retired when the next reviewed generation superseded them without changing an imported route.
The retirements are enforced: `retired-0.8.0`, `retired-0.21.0`, `retired-0.44.0` and
`retired-0.46.0` are refused.

Each retired fixture exports every route this binding imports and passes every runtime shape probe,
so the version rule alone refuses it. The sharpest was `retired-0.39.0`: a real 0.39.0 library
exports exactly what 0.40.0 does -- that admission changed a rule, not a route -- so a policy
written as "whatever exports the names we call" would have taken it and then failed every adapter
query made outside a callback. Package acceptance met exactly that case on
2026-10-01: an installed 0.36.0 library left in the package's native directory was refused by name.

### What the 0.44.0 admission measured

0.43.0 (CNA `23d7bad1e`) to 0.44.0 (CNA `ed885a8ae`), 2026-10-03: no route, constant or structure
changed; a game's activated and deactivated handlers run inside a callback scope.

| Measurement | Result |
| --- | --- |
| Consumed entry points | 1419; CSX-147 newly consumes three message-box routes already present in 0.44.0 |
| Consumed entry points absent / changed prototype | 0 / 0 |
| Upstream exports | 3210 -> 3210; 0 struct, scalar, constant or string differences; the upstream allowlist holds no entries |
| `tools/abi-verify` | 1137 native and 1137 managed layout/type values, 0 mismatches; 1419 of 1419 prototypes compiled; 23 callbacks; 604 constants |
| Fixtures | 2 accepted, 10 rejected (`retired-0.43.0` and `unreviewed-0.45.0` on either side) |
| Native integration (Release, OPENGLES3 with compiled effects) | 239 of 239, GamerServices/Avatar/Net 24 of 24, framework 653 of 653, XnaCompat 304 of 304; package acceptance not rerun |

### What the 0.43.0 admission measured (retired)

0.42.0 (CNA `938a22069`) to 0.43.0 (CNA `23d7bad1e`), 2026-10-03: no route, constant or structure
changed; `cna_sound_effect_play` and `cna_sound_effect_play_with_settings` answer on any thread.

| Measurement | Result |
| --- | --- |
| Consumed entry points | 1416, as 0.42.0 |
| Consumed entry points absent / changed prototype | 0 / 0 |
| Upstream exports | 3210 -> 3210; 0 struct, scalar, constant or string differences; the upstream allowlist holds no entries |
| `tools/abi-verify` | 1137 native and 1137 managed layout/type values, 0 mismatches; 1416 of 1416 prototypes compiled; 23 callbacks; 604 constants |
| Fixtures | 2 accepted, 10 rejected (`retired-0.42.0` and `unreviewed-0.44.0` on either side) |
| Native integration (Release, OPENGLES3 with compiled effects) | 237 of 237, GamerServices/Avatar/Net 24 of 24, framework 652 of 652, XnaCompat 304 of 304; package acceptance not rerun |

### What the 0.42.0 admission measured (retired)

0.41.0 (CNA `c77f983b1`) to 0.42.0 (CNA `938a22069`), 2026-10-02: no route, constant or structure
changed; `cna_game_get_graphics_device` also lends the device before the game's run begins.

| Measurement | Result |
| --- | --- |
| Consumed entry points | 1416, as 0.41.0 |
| Consumed entry points absent / changed prototype | 0 / 0 |
| Upstream exports | 3210 -> 3210; 0 struct, scalar, constant or string differences; the upstream allowlist holds no entries |
| `tools/abi-verify` | 1137 native and 1137 managed layout/type values, 0 mismatches; 1416 of 1416 prototypes compiled; 23 callbacks; 604 constants |
| Fixtures | 2 accepted, 10 rejected (`retired-0.41.0` and `unreviewed-0.43.0` on either side) |
| Native integration (Release, OPENGLES3 with compiled effects) | 235 of 235, GamerServices/Avatar/Net 24 of 24, framework 651 of 651, XnaCompat 299 of 299; package acceptance not rerun |

### What the 0.41.0 admission measured (retired)

0.40.0 (CNA `22b30b30e`) to 0.41.0 (CNA `c77f983b1`), 2026-10-02: one route added, nothing else
(`tools/coverage/baselinediff.py`).

| Measurement | Result |
| --- | --- |
| Consumed entry points | 1416: the 1415 of 0.40.0 plus `cna_game_run_foreign_thread_calls_ext` |
| Consumed entry points absent / changed prototype | 0 / 0 |
| Upstream exports | 3209 -> 3210, 0 removed; 0 struct, scalar, constant or string differences; the upstream allowlist holds no entries |
| `tools/abi-verify` | 1137 native and 1137 managed layout/type values, 0 mismatches; 1416 of 1416 prototypes compiled; 23 callbacks; 604 constants |
| Fixtures | 2 accepted, 10 rejected (`retired-0.40.0` and `unreviewed-0.42.0` on either side) |
| Native integration (Release, OPENGLES3 with compiled effects) | 234 of 234, GamerServices/Avatar/Net 24 of 24, framework 650 of 650, XnaCompat 297 of 297; package acceptance not rerun |

### What the 0.40.0 admission measured (retired)

0.39.0 (CNA `0a57ccad2`) to 0.40.0 (CNA `22b30b30e`), 2026-10-02: no route, constant or structure
changed; the twelve `cna_graphics_adapter_*` routes also take the active game's own handle.

| Measurement | Result |
| --- | --- |
| Consumed entry points | 1413, as 0.39.0; 1415 since CSX-114 bound `cna_game_window_get/set_is_borderless_ext` (present in 0.40.0; `Verify-Abi.sh`: 1415 prototypes compiled, 0 mismatches) |
| Consumed entry points absent / changed prototype | 0 / 0 |
| Upstream exports | 3209 -> 3209; 0 struct, scalar, constant or string differences; the upstream allowlist holds no entries |
| `tools/abi-verify` | 1137 native and 1137 managed layout/type values, 0 mismatches; 23 callbacks; 604 constants (its phone-enum scope now excludes the managed-only `StartupMode`, which had stopped the run since CSX-103) |
| Fixtures | 2 accepted, 10 rejected (`retired-0.39.0` and `unreviewed-0.41.0` on either side) |
| Native integration (Release, OPENGLES3 with compiled effects) | 231 of 231, GamerServices/Avatar/Net 24 of 24, framework 645 of 645, XnaCompat 290 of 290; package acceptance passed |

### What the 0.39.0 admission measured (retired)

0.38.0 (CNA `f445fef8c`) to 0.39.0 (CNA `0a57ccad2`), 2026-10-01: one route added, nothing else.

| Measurement | Result |
| --- | --- |
| Consumed entry points | 1413: the 1412 of 0.38.0 (1410 plus CSX-098's touch bridge) plus `cna_game_set_foreign_thread_calls_ext` |
| Consumed entry points absent / changed prototype | 0 / 0 |
| Upstream exports | 3208 -> 3209, 0 removed; 0 struct, scalar, constant or string differences; the upstream allowlist holds no entries |
| `tools/abi-verify` | 1137 native and 1137 managed layout/type values, 0 mismatches; 1413 of 1413 prototypes compiled; 23 callbacks; 604 constants |
| Fixtures | 2 accepted, 10 rejected (`retired-0.38.0` and `unreviewed-0.40.0` on either side) |
| Native integration (Release, OPENGLES3 with compiled effects) | 224 of 224, GamerServices/Avatar/Net 24 of 24, framework 644 of 644, XnaCompat 285 of 285; package acceptance not rerun |

### What the 0.38.0 admission measured (retired)

0.37.0 (CNA `e9dd5d879`) to 0.38.0 (CNA `f445fef8c`), 2026-10-01: one route added, nothing else.

| Measurement | Result |
| --- | --- |
| Consumed entry points | 1410: the 1409 of 0.37.0 plus `cna_game_run_frame_ext` |
| Consumed entry points absent / changed prototype | 0 / 0 |
| Upstream exports | 3207 -> 3208, 0 removed; 0 struct, scalar, constant or string differences; the upstream allowlist holds no entries |
| `tools/abi-verify` | 1137 native and 1137 managed layout/type values, 0 mismatches; 1410 of 1410 prototypes compiled; 23 callbacks; 604 constants |
| Fixtures | 2 accepted, 10 rejected (`retired-0.37.0` and `unreviewed-0.39.0` on either side) |
| Native integration (Release, OPENGLES3 with compiled effects) | 211 of 211, GamerServices/Avatar/Net 24 of 24, framework 635 of 635, XnaCompat 272 of 272; package acceptance passed |

### What the 0.37.0 admission measured (retired)

0.36.0 (CNA `402c1aaa9`) to 0.37.0 (CNA `e9dd5d879`), 2026-10-01: four routes added, nothing else.

| Measurement | Result |
| --- | --- |
| Consumed entry points | 1409: the 1405 of 0.36.0 plus the four `cna_error_*_last_exception_*` queries |
| Consumed entry points absent / changed prototype | 0 / 0 |
| Upstream exports | 3203 -> 3207, 0 removed; 0 struct, scalar, constant or string differences; the upstream allowlist holds no entries |
| `tools/abi-verify` | 1137 native and 1137 managed layout/type values, 0 mismatches; 1409 of 1409 prototypes compiled; 23 callbacks; 604 constants; 12 of 12 negative controls rejected |
| Native integration (Release, OPENGLES3 with compiled effects) | 206 of 206, GamerServices/Avatar/Net 24 of 24; package acceptance passed |

### What the 0.36.0 admission measured (retired)

0.35.0 (CNA `6e0de68e8`) to 0.36.0 (CNA `402c1aaa9`), 2026-10-01: one route added, nothing else.

| Measurement | Result |
| --- | --- |
| Consumed entry points | 1384: the 1383 of 0.35.0 plus `cna_packet_reader_copy_data_ext` |
| Consumed entry points absent / changed prototype | 0 / 0 |
| Upstream exports | 3202 -> 3203, 0 removed; 0 struct, scalar, constant or string differences; [`eng/cna-upstream-abi-allowlist.txt`](../eng/cna-upstream-abi-allowlist.txt) holds no entries |
| `tools/abi-verify` | 1119 native and 1119 managed layout/type values, 0 mismatches; 1384 of 1384 prototypes compiled; 21 callbacks (the GamerServices and Net ones CNA.XnaCompat passes as `nint` included); 596 constants; 12 of 12 negative controls rejected |
| Later in 0.36.0 (CSX-045, no new generation) | 21 devices.h/sensors.h imports for CNA.PhoneCompat: 1405 consumed entry points; 1137 layout/type values, 1405 prototypes, 23 callbacks, 604 constants, all matching |
| Native integration (Release, OPENGLES3 with compiled effects) | 203 of 203, GamerServices/Avatar 12 of 12 |

### What the 0.35.0 admission measured (retired)

0.21.0 (CNA `599d14e54`) to 0.35.0 (CNA `bdf239360`), 2026-10-01:

| Measurement | Result |
| --- | --- |
| Consumed entry points | 957, after dropping 45 (below) |
| Consumed entry points absent / changed prototype | 0 / 0 |
| Upstream findings outside the consumed surface | 1048 removals and changes, reviewed in [`eng/cna-upstream-abi-allowlist.txt`](../eng/cna-upstream-abi-allowlist.txt): 36 entries, 0 stale |
| `tools/abi-verify` | 895 native and 895 managed layout/type values, 0 mismatches; 957 of 957 prototypes compiled; 6 callbacks; 350 constants; 12 of 12 negative controls rejected |
| Documented contracts of the 957 routes, header text diffed | 14 changed |
| Native integration, Debug and Release, OPENGLES3 with compiled effects | 200 of 200 |

The table is the admission as it was made. The same day CSX-040 bound the GamerServices, Guide,
Avatar and Net routes of the same 0.35.0 headers (426 imports, 239 + 187); the consumed surface is
now 1383 entry points, and `tools/abi-verify` measures 1119 native and 1119 managed layout/type
values with 0 mismatches, compiles 1383 of 1383 prototypes and asserts 596 constants.

**The 45 dropped imports.** CNA removed `engine_layer.h` in 0.30.0 (`MOD-RETIRE-1`): the
post-process chain/pass, bloom, tonemap, render-target pool and blit routes and
`cna_engine_layer_get_version`, 44 routes this binding imported for its `CNA.Graphics.Experimental`
layer (`PostProcessChain`, `RenderTargetPool`, the HDR pass settings). That layer was CNA surface
with no XNA counterpart and was removed rather than rebuilt over a retired native layer;
`RenderTarget2D` and effects remain the XNA way to post-process. `cna_graphics_ext_is_available`
went with it, because its only caller was `GraphicsDevice.IsCnaEngineLayerAvailable`, whose meaning
the retirement removed.

**The 14 changed contracts.** Three needed managed changes:

- `cna_graphics_device_clear_options` now refuses to clear a depth or stencil plane the target does
  not have, as XNA's explicit `Clear` does. `Clear(Color)` had passed all three planes and
  `Viewport.MaxDepth` (FNA's form, which masks missing planes); it is now XNA's own IL,
  `Clear(DefaultClearOptions, color, 1f, 0)`.
- `cna_graphics_device_get_shader_dialect_ext` can answer HLSL, MSL, WGSL and SPIR-V;
  `ShaderDialect` named only the first four identities.
- `cna_graphics_device_supports_capability`, occlusion queries and `cna_effect_set_current_technique`
  now refuse what XNA refuses (Reach-profile limits, query sequencing, a null technique); the
  managed code either already refused first or surfaces the native refusal.

The rest are wording. Four integration tests encoded behaviour XNA does not have and were
corrected rather than the binding: a quad behind the camera that only GL's depth range had drawn,
a separate-alpha blend state in Reach, an occlusion query in Reach, and an audio buffer that was not
a whole number of frames.

## Compatible evolution operations

An operation is compatible only when it preserves every contract an existing consumer can use:

- add an export under a new name without removing or changing an existing export;
- add an unrelated fixed-width constant without renumbering or changing an existing constant;
- append an optional field to a caller-sized/versioned descriptor while retaining the old
  mandatory prefix, accepting the old `struct_size`, and never reading or writing beyond the size
  the caller supplied;
- add a new callback table or append an optional callback to a genuinely caller-sized table while
  honoring the old table size;
- add a fixed-width enum-like identity when existing values and sentinels remain fixed and an old
  consumer cannot be forced to interpret the new value;
- clarify documentation without changing ownership, error, threading, lifetime, or behavior.

Additional exports are deliberately allowed by the loader. They cannot collide with or substitute
for the 1409 names imported by this build.

## Breaking operations

The following require a new stable major, or a new reviewed experimental minor plus an explicit
consumer-matrix decision:

- remove, rename, hide, or stop exporting an existing entry point;
- change an export's return type, parameter count/order/type/width/pointer depth, calling
  convention, ownership, errors, threading, lifetime, or behavior;
- change any existing scalar width, constant value, flag bit, handle representation, or string /
  buffer convention;
- reorder, resize, retype, remove, or change the meaning/alignment/packing of an existing struct
  field, or append to a fixed/non-size-aware output struct;
- require a newly appended descriptor field from a caller that supplied the older valid prefix;
- renumber an enum-like identity, change a sentinel such as `MAXIMUM`, or return a new identity to
  a route whose old consumer cannot tolerate unknown values;
- change an existing callback signature, calling convention, invocation order/thread, error
  propagation, context meaning, or lifetime.

Deprecation is compatible only while the old export and its contract remain available. A changed
signature uses a new export name. A changed fixed struct uses a new type and normally a new route.

### Struct-size and version rules

`struct_size`/`struct_version` are useful only when the callee actually reads the caller's header
before accessing the body. Input and in/out descriptors may grow by appending optional fields when
the old prefix remains valid. An output initializer that receives only `T*` and overwrites the
header has no independent capacity argument; CNA.NET therefore treats that output shape as fixed
despite the header. It may grow only through a size-aware/new export or a new type. The loader's
guarded `CNA_TouchCapabilities` initializer canary exists specifically to enforce one foundational
instance of this distinction.

### Enum-like values

CNA exposes fixed-width `uint32_t` typedefs and named constants, not compiler-sized C enums.
Appending a value is compatible only if all old values and sentinels remain stable and old callers
either cannot receive the new value or explicitly tolerate unknown values. Appending a flag bit is
subject to the same rule for masks. Moving `MAXIMUM` is a constant-value change and is breaking for
callers that use it, which is why 0.8 is not labeled generally additive.

### Callbacks

Existing callback prototypes are immutable. New callback capability uses a new subscription or an
optional appended slot in a size-aware table. A callback's thread, ordering, reentrancy, context,
error, and lifetime rules are ABI contract just as much as its machine-level prototype.

## Loader proof and its boundary

Before returning a library handle, the managed resolver now requires:

1. readable `cna_get_abi_version` metadata and an exact reviewed matrix entry;
2. every one of the 1416 `LibraryImport` entry points declared by `CNA.Interop.Native`;
3. a successful `cna_error_get_last_message_size` result/out-parameter signature canary;
4. a successful guarded `cna_touch_capabilities_init` canary proving the 16-byte version-1 shape,
   canonical body, and write bounds.

Version numbers and symbol names cannot describe every native prototype or POD layout. The runtime
checks therefore complement, rather than replace, `tools/abi-verify`: the platform C compiler
checks 1137 native/managed size, alignment, offset, width and type values, compiles every import's
prototype against the headers, checks the callbacks and asserts the enum-like constants, and rejects
headers outside this same version matrix. CNA's own ABI baseline supplies the reviewed
release-to-release shape/export/value diff, which `tools/coverage/baselinediff.py` computes and which
must report zero unreviewed breaking differences. A new ABI version must pass both evidence paths
before it is added to the matrix.

## Automated fixtures

`scripts/Verify-NativeAbiCompatibility.sh` builds dependency-free shared libraries and runs each in
a fresh managed process. The exact matrix is:

| Fixture | Expected | Property proved |
| --- | --- | --- |
| `exact-0.44.0` | Accept | Exact expected ABI. |
| `exact-0.44.0-extra-symbol` | Accept | Unrelated added exports do not break a consumer. |
| `retired-0.8.0` | Reject | A generation retired because this consumer outgrew it. |
| `retired-0.21.0` | Reject | A generation retired because a newer one superseded it -- being previously audited is not admission. It exports every required symbol and passes every shape probe, so the version rule alone refuses it. |
| `retired-0.43.0` | Reject | The generation this admission retired, one below the accepted entry. |
| `unreviewed-0.45.0` | Reject | Nor is being newer: the matrix is a point list, not a floor. |
| `missing-required-symbol` | Reject | Any missing managed import fails at load, not at first use. |
| `changed-required-signature` | Reject | A testable core signature/out-parameter change fails its canary. |
| `incompatible-major-1.0.0` | Reject | Major mismatch. |
| `structurally-incompatible-0.44.0` | Reject | An accepted version cannot override guarded shape evidence. |
| `malformed-metadata-0.0.0` | Reject | An incomplete/unrecognized encoded generation. |
| `unreadable-metadata` | Reject | Missing version export. |

The fixture inventory is generated from and checked against the managed import declarations. CI
runs this gate without a protected CNA binary; package acceptance reruns it and additionally sends
the selected real library through the same probe.
