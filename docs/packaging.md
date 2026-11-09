# Packaging and native distribution qualification

No CNA.NET NuGet package has been published. Production builds of all three projects still have
`IsPackable=false`; only the dedicated `CnaPackageAcceptance=true` path enables local preview
packing. The results below prove mechanics for one selected local native build. They do not approve
package IDs/versioning, native redistribution, signing, or an officially supported RID.

## Validated managed graph

| Local package | Purpose | Package dependency |
| --- | --- | --- |
| `CNA.Interop` | Internal ABI assembly; may carry the explicitly selected experimental RID asset | none |
| `CNA.Framework` | Idiomatic `CNA.*` API | aligned local `CNA.Interop` preview version in the isolated feed |
| `CNA.XnaCompat` | Strict XNA facade plus explicit CNA extensions | aligned local `CNA.Framework` preview version in the isolated feed |

The proposed `CNA.Xna` convenience metapackage was not needed to prove this graph and is not
created by the harness. Start-at-`0.x` SemVer and final package-ID policy still require release
approval. Package dependency ranges never replace the explicit
[`cna-cs-native-abi/1`](native-abi-compatibility.md) loader contract.

## Reproducible local acceptance

On the measured Linux x64 host, this command completed the complete acceptance path without
publishing anything:

```bash
# The native input is an INSTALLED CNA C API component, not a build tree:
cmake --install ../cna/build-probe --component CNACApi \
  --prefix build-consumer/cna-native-linux-x64
scripts/Package-Acceptance.sh --native-directory build-consumer/cna-native-linux-x64/lib
```

The native input is mandatory and explicit, and it is a directory: `libcna_c_api.so` with RUNPATH
`$ORIGIN` plus the SDL libraries CNA builds and, since CNA `89fb1493f`, the shared runtime
`libcna.so` that a Linux C API links. The script checks that every library in that directory which
the C API needs, directly or through another one there, is in the `CNA.Interop` package. The script
refuses a library whose RUNPATH is anything but `$ORIGIN` -- a build-tree library names that tree,
so a package made from it loaded only on the machine that built it, which the earlier single-file
input did. It runs on a linux-x64 or an osx-arm64 host and refuses any other (on macOS the library
is `libcna_c_api.dylib`, its run path `@loader_path`, read with `otool -l`), creates an isolated
feed and package cache, performs a clean Release solution build, packs, inspects, installs, builds
and runs a newly generated consumer, and writes `acceptance-report.json`. Omitting `--output` writes
to `build-consumer/package-acceptance` (replaced on the next run; never `/tmp`). It never invokes
`nuget push`. FFmpeg, curl and opus stay system dependencies of this native configuration, as CNA's
`docs/c-api/CONSUMING.md` records.

The measured preview version was `0.1.0-local.1`, producing:

- `CNA.Interop.0.1.0-local.1.nupkg` and `.snupkg`;
- `CNA.Framework.0.1.0-local.1.nupkg` and `.snupkg`;
- `CNA.XnaCompat.0.1.0-local.1.nupkg` and `.snupkg`.

Every main package contained its managed DLL, XML documentation, `LICENSE`, `NOTICE.md`, and
`README.md`. Every symbol package contained its portable PDB. Nuspec repository metadata recorded
the source URL and base Git commit. Because this acceptance checkpoint has uncommitted changes,
that commit is not claimed to identify the package contents exactly; clean/reproducible Source Link
policy remains a release decision. The local `CNA.Interop` package additionally contained:

```text
runtimes/linux-x64/native/libcna_c_api.so
runtimes/linux-x64/native/libSDL3.so.0
runtimes/linux-x64/native/libSDL3_mixer.so.0
runtimes/linux-x64/native/libSDL3_image.so.0
```

Re-measured 2026-10-01 against CNA `402c1aaa9` (C ABI 0.36.0, OPENGLES3, compiled effects): the
consumer's packaged `libcna_c_api.so` resolves SDL from its own `runtimes/linux-x64/native`, with no
path into the CNA tree.

Re-measured 2026-11-09 against CNA `30142a14d` (C ABI 0.60.0, Release OPENGLES3, compiled effects):
the package carries `libcna_c_api.so`, `libcna.so`, `libSDL3.so.0` and `libSDL3_mixer.so.0` (this
configuration builds no SDL_image), and the consumer draws 60 and 600 frames from them. Without
`libcna.so` it failed to load.

## The XNA graphics profile travels with the package

XNA's build embedded a project's `<XnaProfile>` (Reach or HiDef) in the game assembly as the
`Microsoft.Xna.Framework.RuntimeProfile` resource, and `GraphicsDeviceManager` reads it for its
default `GraphicsProfile` (Reach without it). `CNA.XnaCompat` ships the build half as
`build/CNA.XnaCompat.targets` and `buildTransitive/CNA.XnaCompat.targets`, so a package consumer
keeps its original project property and gets the same default. A project that references
`CNA.XnaCompat` as a project imports `src/CNA.XnaCompat/build/CNA.XnaCompat.targets` itself.
Package acceptance builds the isolated consumer a second time with `-p:XnaProfile=HiDef` and
refuses a package whose consumer does not carry `Windows.v4.0.HiDef`
(`PACKAGE_XNA_PROFILE_RESOURCE`).

## Measured linux-x64 experiment

The isolated generated `dotnet new cna-game` project restored from the local feed and built with no
`CnaDotnetRoot`, `CNA_DOTNET_ROOT`, sibling `ProjectReference`, developer absolute path, or source-checkout
reference. Its package-native library resolved from the RID asset with both `CNA_NATIVE_LIBRARY`
and `CNA_NATIVE_DIR` unset. It completed 60 frames and 600 frames with the OPENGLES3 renderer.

Negative/precedence cases also executed in fresh processes:

- removing the packaged native asset produced the actionable no-library/RID diagnostic;
- a dependency-free ELF32 fixture on the x64 host produced the wrong-architecture/binary-format
  diagnostic with platform/RID context;
- the complete ABI fixture matrix accepted exact 0.6.0, additive 0.7.0, additive 0.7.0 with an
  unrelated export, and the reviewed CNA.NET subset of 0.8.0;
- the same matrix rejected a missing required export, testable changed signature, incompatible
  major, structurally incompatible same-major library, malformed 0.0.0 metadata, and unreadable
  metadata;
- a major-version-1 fixture reported detected ABI 1.0.0 versus consumer ABI 0.6.0 and the selected path;
- a loadable fixture missing one of all 861 required imports named the missing symbol;
- an invalid explicit path failed without package fallback;
- two recognized files in `CNA_NATIVE_DIR` produced a conflict diagnostic;
- a valid `CNA_NATIVE_LIBRARY` override took precedence over both the packaged asset and
  `CNA_NATIVE_DIR` and completed 60 frames.

This is `linux-x64` local acceptance evidence only. It does not make the RID officially shipped.
No empty Windows, macOS, or ARM package placeholders exist.

## Native loader policy proven by the harness

Resolution precedence is:

1. absolute `CNA_NATIVE_LIBRARY` (fail fast, no fallback);
2. absolute `CNA_NATIVE_DIR` containing exactly one recognized library (fail fast, no fallback);
3. application/package-native locations beside the managed application, including
   `runtimes/<rid>/native`.

The resolver does not search the source tree, bare system-library names, or accidental process-wide
loader paths. It admits only exact versions in the reviewed matrix, resolves all 861 imported
symbols, and executes core signature/shape canaries before returning the handle. Additional
unrelated exports are allowed. Ordinary diagnostics identify the selected configuration,
detected/consumer ABI when readable, policy, platform/RID and remediation. Set
`CNA_NATIVE_DIAGNOSTICS=1` only when low-level loader details are needed.

## Remaining release decisions

Before public packaging, approve native redistribution/licensing, package IDs and version policy,
signing, Source Link/reproducible-build policy, the qualified CNA revision/configuration for every
RID, and clean CI evidence on every claimed OS and architecture. The candidate matrix is
`eng/platform-matrix.json`; cross-compilation alone is never qualification.

## Measured osx-arm64 experiment

Measured 2026-10-09 on an Apple-silicon Mac (CNA `plans/plan_apple_m4.md` AM4-227) against CNA
`a3da0a5bb` (C ABI 0.46.0), SOFTWARE with `CNA_DEVICES=ON` and `CNA_SOFTWARE_COMPILED_EFFECTS=ON`,
installed with `cmake --install <tree> --component CNACApi --prefix <dir>`:

```bash
scripts/Package-Acceptance.sh --native-directory <dir>/lib --output <out>
```

reported `PACKAGE_ACCEPTANCE_STATUS=passed`. The local `CNA.Interop` package carried

```text
runtimes/osx-arm64/native/libcna_c_api.dylib
runtimes/osx-arm64/native/libSDL3.0.dylib
runtimes/osx-arm64/native/libSDL3_image.0.dylib
runtimes/osx-arm64/native/libSDL3_mixer.0.dylib
```

-- the names `libcna_c_api.dylib`'s load commands use; the versioned targets they point at are not
packed. The isolated consumer restored from the local feed, built with no source reference, found
the packaged library with neither override set and completed 60 and 600 frames; the missing-native,
wrong-architecture (an x86_64 dylib: dyld says "incompatible architecture", the loader reports
"the wrong architecture or binary format"), wrong-ABI,
missing-symbol, invalid-path, conflicting-library and override-precedence cases behaved as on
Linux, and the ABI fixture matrix accepted 2 and rejected 11. Homebrew's FFmpeg, curl and Opus
stay system dependencies there, as CNA's `docs/c-api/CONSUMING.md` records for macOS.

## The acceptance harness forces `SDL_VIDEODRIVER=offscreen`

`scripts/Package-Acceptance.sh` runs the isolated consumer with `SDL_VIDEODRIVER=offscreen` and
`SDL_AUDIODRIVER=dummy`, so the gate needs no display. That is right for the gate and worth knowing
before pointing it at an arbitrary renderer: **it passes with an `OPENGLES3` native asset and aborts
with a `SDL_RENDERER` one.**

Measured, not inferred. Under a real X display the `SDL_RENDERER` build reports the display's name
and takes the template's 2D fallback; under `offscreen` the same library reports a renderer called
`1` and claims a 3D pipeline it does not have, and the packaged consumer then dies with a core dump.
So the offscreen SDL video driver and CNA's `SDL_RENDERER` backend do not combine, and the harness's
own default is the configuration to keep using. Pass `--native-directory` a renderer that works
headless.

On macOS (CNA `plans/plan_apple_m4.md` `AM4-230`, `AM4-231`) the same symptoms -- a renderer
named after the display, a claimed 3D pipeline, then an abort -- traced to two defects rather than
to the video driver. `dotnet new` removed the template's `#if ENGINE_CNA` branch, so every
generated game reported `Adapter.Description` and claimed 3D and depth; and `Clear(Color)` asked
SDL_RENDERER, which has no depth plane, to clear one. With both fixed, the generated game reports
`SDL_RENDERER`, "2D only", and draws its 60 frames under `dummy`. Whether the Linux `offscreen`
abort had the same two causes has not been re-measured on Linux.

On macOS it uses `dummy` instead: SDL's `offscreen` driver creates its contexts through EGL, which
macOS does not have, and a windowless SOFTWARE library draws under `dummy`.
