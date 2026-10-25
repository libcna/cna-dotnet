# HelloGame

The minimal end-to-end CNA.NET game: clear the screen, load a texture, draw
it with `SpriteBatch`, read the keyboard, exit on <kbd>Escape</kbd>.
Reproduces the reference first program in
`libcna/cna`'s `docs/binding.md` exactly.

## Status

This sample builds, and the stack underneath it now runs: `tests/CNA.Integration.Tests`
constructs a real `Game`, drives real frames, clears the screen and completes a
`SpriteBatch` pass against the real library.

**This section previously said the sample could not run because `cna-native`
"does not exist upstream yet". That was wrong, and wrong in an expensive way.**
The library exists and has for some time — it is simply called
`libcna_c_api.so`, while every `[LibraryImport]` asks for `cna-native`. The
names never matched, so the loader failed, and the failure was attributed to a
missing upstream rather than to a lookup that could not succeed.
`CNA.Interop.NativeLibraryResolver` now bridges the two names.

To run it, point at a build of `libcna/cna`:

```
CNA_NATIVE_LIBRARY=/path/to/libcna_c_api.so dotnet run
```

or `CNA_NATIVE_DIR=/path/to/build/modules/c-api`. Dropping the library next to
the build output works too, as will a `runtimes/<rid>/native/` layout once
`../../plan.md` Phase 6 (NuGet packaging) lands.

Note that this sample loads a texture through the content pipeline, which is a
step beyond what the integration tests cover — see `Content` below.

## Content

This sample expects a texture asset named `eggbert` under a `Content/`
directory (matching `Content.RootDirectory = "Content";` in `Game1`'s
constructor). No content pipeline exists yet in this repository (see
`plan.md` Phase 4) — for now, `ContentManager.Load<Texture2D>` simply asks
native CNA to load `Content/eggbert` by whatever asset-loading convention
`libcna/cna` implements first.
