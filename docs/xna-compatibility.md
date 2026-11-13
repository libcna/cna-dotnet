# XNA compatibility status

`CNA.XnaCompat` is intended to let XNA 4.0 game source recompile against CNA. Its public metadata is
now exact for the selected seven-assembly Windows runtime profile; behavioral parity, additional
XNA product profiles, native platform coverage, and release packaging remain incomplete.
Compatibility claims in this repository are evidence-based:

1. source compatibility;
2. exact public metadata compatibility;
3. behavioral compatibility;
4. selected FNA/MonoGame/Kni portability;
5. binary compatibility, which is not a primary goal.

The current finite remainder and its classifications are in
[`final-compatibility-audit.md`](final-compatibility-audit.md). The final bounded application sweep
has reached saturation condition B; broad application discovery is closed and this document must
not be read as an instruction to keep finding more games.

The CNA C/C++ API determines what the backend can do. It is not the authority for Microsoft's
managed type system.

## Authoritative measured profile

The current strict profile is XNA 4.0 Windows runtime and combines seven locally supplied XNA
reference assemblies listed in `tools/api-compat/profiles/xna40-windows-runtime.json`. Reference
assemblies are inspected as metadata only and are not redistributed.

Run the verifier with:

```bash
dotnet build CNA.sln -c Release
XNA_REFERENCE_PATH=/path/to/xna-reference-assemblies \
  dotnet run --project tools/api-compat -c Release --no-build -- --format json
```

As re-measured on 2026-10-04 (on 2026-08-23 the same profile counted 257 types; the one difference
is `GamerServicesComponent`, which XNA declares in `Game.dll` and which is now measured with the
rest of GamerServices -- see *Profile boundaries*):

- reference types: 256;
- target types: 256;
- unallowlisted diagnostics: 0;
- accidental `CNA.*` public/protected signature findings: 0;
- reviewed exceptions: 0;
- verifier exit code: 0.

The tool compares type kind/access/base/interfaces/modifiers/generics/layout/attributes, members,
parameters/modifiers/defaults, properties/indexers/accessors, events, fields/constants/enums,
delegates, and nested types. Its allowlist requires an exact diagnostic identity and rationale and
reports stale entries. The older regex/name counter remains useful only for discovery; it cannot
establish XNA parity.

## What has been repaired

The public XNA hierarchy now takes priority over implementation reuse for these coherent groups:

- `DrawableGameComponent : GameComponent` and compat-only `IGameComponent` collections/events;
- dynamic vertex/index buffers and dynamic sound instance inheritance;
- `ResourceContentManager : ContentManager`;
- `GraphicsResource` ancestry for textures, buffers, effects, render targets, vertex declarations,
  SpriteBatch, and graphics states;
- `ModelEffectCollection : ReadOnlyCollection<Effect>`;
- compat-generic `CurveKeyCollection`, including XNA's shallow clone behavior;
- exact generic `DrawUser*` overload families;
- the complete Game/device/window/service and managed content-reader groups;
- model collections and model resource facades;
- the complete strict Audio/XACT, Media, and Storage families;
- graphics exceptions, `BoundingFrustum`, `OcclusionQuery`, `SpriteFont`, `DisplayMode`, and the
  remaining strict graphics collections;
- public implementation helper bases removed from the XNA namespace;
- all public XNA-to-CNA conversion operators replaced by internal conversion helpers;
- CNA renderer diagnostics moved out of the strict namespace to `CNA.XnaCompat.Extensions`.

The final completion pass also added the 13 design converters, `GamerServicesComponent`, the exact
touch enumerator, complete math/geometry overload families, XNA input state/enums, graphics
effects/vertex/viewport contracts, and exact packed-vector metadata. Modifiers, attributes,
accessors, constants, parameter defaults and parameter names are all exact. These changes use
composition, internal adapters, and single-owner backends.

## Finite maintenance backlog

- preserve strict metadata, CNA-leak, base-hierarchy, interface, unexpected-member, and allowlist
  counts at zero as hard regression gates;
- execute the combined 470-observation snapshot on a Windows XNA runtime only when a Windows
  qualification campaign is deliberately opened. CNA executes all 470 on Linux. FNA completes
  the 199-line pure snapshot before its own `SoundEffect` finalizer aborts; MonoGame builds the pure
  source but aborts during audio initialization and omits the Storage/XACT runtime surface. Direct
  XNA source/IL resolves implemented strict behavior, but the installed XNA C++/CLI assemblies
  cannot run on Linux;
- obtain legal authored XACT, Song, and Video fixtures for success/lifetime/event observations;
  continue device-loss work only when the C ABI exposes a deterministic route;
- adjudicate the current 46 malformed/error XNB observations on Windows XNA only as a focused
  differential task, and add legal fixture breadth only for a distinct reader/graph/ownership route;
- keep the measured GamerServices/Avatar, Net, and Content Pipeline inventories separate from the
  runtime baseline; Xbox and Phone remain pending authoritative legal reference packs.

This is a maintenance list, not a discovery queue. Reopen an item only for a reproducible defect,
an explicit owner request, or a deliberate subsystem/platform qualification.

## Source compatibility corpus

`tests/CNA.XnaCompat.CompileProbe` builds with the solution and locks in assignments for the
repaired component, dynamic-buffer, dynamic-audio, content-manager, graphics-resource, curve,
model-effect, state, vertex-declaration, and SpriteBatch relationships. It contains 83
math/geometry, 23 input, 47 pure Audio, and 46 content-error observations. The device-backed
`CNA.XnaCompat.GraphicsProbe` adds 153 graphics and 13 resource/lifetime observations. Output
records IEEE-754 bits, exact hash/string results, state flags, exception kinds, identity, and
lifecycle/collection behavior. `CNA.XnaCompat.RuntimeProbe` adds 36 native Audio, 7 XACT, 20 Media,
17 Video, 20 Storage, and 5 DeviceLifecycle observations. The resulting total is 470. CNA runs all
470. FNA emits all 199 pure observations before its finalizer abort, and MonoGame emits the old 106
pure observations before its audio subsystem aborts. Identical source builds against XNA through
the Windows snapshot workflow, whose native C++/CLI runtime still requires Windows. Direct XNA
source/IL adjudicates implemented strict values. This remains a focused corpus, not proof that
arbitrary games compile or behave identically.

The identical compile-probe source passes against the local Microsoft XNA reference assemblies,
CNA.XnaCompat, FNA, and MonoGame. It fails against Kni at one deliberate XNA assertion:
Kni's `VertexDeclaration` is not assignable to `GraphicsResource`. This is recorded as an
implementation portability difference; the less demanding template source still builds and runs
on Kni.

The same representative template source currently compiles against:

| Target | Compile status | Runtime status in this audit |
| --- | --- | --- |
| CNA.XnaCompat | Pass | Pass: 60 and 600 frames, OPENGLES3, Linux x64 |
| FNA | Pass with explicitly configured FNA.dll | Unavailable: configured assembly could not load; clean exit 2, no frame claim |
| MonoGame DesktopGL 3.8.1.303 | Pass | Pass: 60 frames, llvmpipe, Linux x64 |
| Kni 4.2.9001 + SDL2.GL 4.2.9001.1 | Template pass; strict corpus has one hierarchy failure | Pass: 60 frames, llvmpipe, Linux x64 |
| Microsoft XNA reference assemblies | Strict corpus passes with 0 warnings/errors | Runtime corpus still requires Windows XNA 4.0 |

A build is not a runtime claim.

Beyond the games that run (cna-dotnet-samples `games/`), ten real XNA 4.0 codebases that cannot run on
Linux were compiled from their own project files: none uses an XNA type or member CNA.NET lacks.
What stops them is Windows Forms, Silverlight, a library their repositories do not ship, or one
change in .NET itself: since .NET 7, `BitConverter.GetBytes(sbyte)` is ambiguous, because `sbyte`
converts implicitly to `Half` (`CAMPAIGN.md`, 2026-10-02).

The runtime differs from the one XNA games were written for in four ways CNA.NET repairs at
compile time, each with an opt-out property in CNA.XnaCompat.targets:

- .NET Framework 4.0's `List<T>.ForEach` keeps walking when the action changes the list, where
  .NET throws: a game's own `ForEach` calls are intercepted with the 4.0 loop (CSX-110,
  `CnaNetFx40ListForEach`).
- `Assembly.LoadFile` of an assembly the application already loaded returned that assembly, where
  .NET loads a second copy (CSX-122, `CnaNetFxAssemblyLoadFile`).
- `System.Range`, `System.Index` and `PriorityQueue<,>` did not exist, so a game's own type of that
  name was never shadowed or ambiguous: a project naming one compiles against reference copies in
  which it is internal (CSX-126, `CnaNetFxTypeNames`).
- A `using` of a namespace .NET Framework 4 had and .NET does not (`System.Runtime.Remoting.Messaging`)
  binds to an internal placeholder instead of failing the build (CSX-131, `CnaNetFxNamespaces`).

XNA games also ran as 32-bit processes: a struct laid out explicitly with a reference at a 4-byte
offset after its other fields (BEPUphysics' Windows build) gets that reference moved to an 8-byte slot
in the compiled assembly (CSX-134, `CnaSixtyFourBitLayouts`).

And one at run time: the .NET Framework of XNA's Windows printed infinity as `Infinity` and a minus
sign as `-`, where .NET's ICU data prints `∞` and, for some cultures, U+2212, characters a default
SpriteFont cannot draw. A game's culture keeps the ASCII forms (CSX-128, `CnaNetFxNumberSymbols`).

## Behavior and content

The final 2026-10-04 stability matrix passes in both Debug and Release: 653 Framework, 319
XnaCompat, 5 BrowserCompat, 260 native Integration, and 24 GamerServices tests (1,261 per
configuration). Against C ABI 0.44.0, the selected native matrix passes 107 C API and 710
compiled-effect tests; the Runtime/Game binary passes 208 tests and skips the two Headless/Terminal
variants that cannot back this OPENGLES3 build. The isolated ownership runner also passes 100 game
teardown/recreation cycles, with 3,000 successful releases and no failure, retry, refused destroy,
or native crash.
The optional Release deep mode passes 1000 cycles: 500 explicit, 500 finalizer, and 100
throwing-handler cycles, with 15,000 queued releases, 29,000 successful release attempts, and zero
release failures/retries/pending releases, refused game destroys, or native crashes. That proves
the exercised routes, not allocator-level leak freedom or all XNA behavior.

The XNA reader type system and ordinary custom `Content.Load<MyType>()` path are implemented,
including reader activation/versioning, shared resources, existing instances, disposable
tracking, and LZX handling. Forty-six malformed/error observations now cover compact invalid
headers, built-in readers, legal/truncated/malformed LZX blocks, nested/missing/normalized external
references, successful and failing stream disposal, reader tables/indices, partial/shared-cycle
failures, wrong types, duplicate and multiple throwing disposables, and deterministic post-failure
state. Remaining content work is differential fixture breadth and the Windows XNA exception
snapshot.

Behavioral differential coverage now includes graphics state/collection identity, transfer
limitations, dynamic buffer options, SpriteBatch ordering/failure recovery, draw validation,
resource/device disposal, Present arguments, audio validation/instances/pumping, initial Media and
collection identity, isolated Storage CRUD, Video stopped-state behavior, and deterministic device
reset order. Coverage remains asset-blocked for authored XACT/Song/Video success, ABI-blocked for
exact Video frame identity and true cross-device/lost routes, and hook-blocked for deterministic
native input transitions.
A song plays from the `.ogg`, `.oga` or `.qoa` file beside the `.wma` its `.xnb` names, as in CNA
and FNA: XNA's pipeline writes every song as Windows Media Audio, which CNA does not decode, so a
game's music is converted once and its `.xnb` kept. A song left as `.wma` alone loads and plays
nothing.
Unsupported exceptions are assessed case by case; their presence alone neither proves a bug nor
compatibility. Exact native capability blockers live in `docs/native-behavior-blockers.md`.

The expanded corpus corrected XNA's fixed-adjugate `Matrix.Invert` behavior (including NaNs for a
singular zero matrix), reciprocal-once scalar vector division, XNA-local `Viewport.Project` and
`Unproject`, exact frustum/GJK edge behavior, packed-vector conversion/formatting, curves, and
exact-boundary `BoundingSphere.Contains(Vector3)` behavior. It also confirmed from XNA packing IL
that `Color(0.5f, NaN, +Infinity, -Infinity)` produces `00FF0080`, despite different FNA and
MonoGame results. Direct XNA IL fixes the arithmetic grouping for the observed quaternion/vector
result; the Windows run remains necessary as an independent runtime snapshot, not as a reason to
copy either alternate engine.

The input corpus covers keyboard masking/order, mouse and game-pad state strings/hashes, analog
clamping and virtual-button thresholds, physical-button filtering, and touch equality/clone/
enumerator edge cases. Constructor-supplied virtual or undefined `Buttons` bits are deliberately
filtered like XNA rather than retained as physical button state. Native polling, packet/disconnect,
dead-zone provenance, and multi-player behavior still need fixture-backed coverage.

## Extension boundaries

| Contract | Current status |
| --- | --- |
| Strict XNA 4.0 | Exact public metadata for the selected Windows runtime profile; behavior and additional profiles remain incomplete |
| FNA extensions | No deliberate extension subset promised yet; template baseline compiles |
| MonoGame extensions | No deliberate extension subset promised yet; template baseline compiles |
| Kni portability | Template baseline compiles; no extension or runtime claim |
| CNA extensions | Renderer diagnostics are explicit in `CNA.XnaCompat.Extensions`; strict-profile CNA signature leaks are zero |

New extensions require an authority, a source-portability use case, an explicit status
(`implemented`, `unsupported`, `upstream blocker`, `not applicable`, or `planned`), and a home that
does not alter the strict XNA contract.

## Exceptions

A failure native raised from an exception reaches XNA code as that exception: since C ABI 0.37.0
CNA names the canonical type behind a failure and an argument exception's parameter name
(`CnaException.CanonicalExceptionType`/`CanonicalParamName` carry them in the CNA layer). The facade
re-raises them where it owns the call to native -- all of GamerServices, Guide, Avatar and Net,
the phone assembly -- and at the boundaries XNA code catches around: `AudioEngine`, `WaveBank` and
`SoundBank` construction (`NoAudioHardwareException`), `SoundEffect.CreateInstance`/`Play` and
`SoundEffectInstance.Play` (`InstancePlayLimitException`), `Microphone.Start`
(`NoMicrophoneConnectedException`), `MediaPlayer.Play` (`InvalidOperationException`), and
`ContentManager.Load` (`ContentLoadException` for default-stream open failures and reader I/O
failures; texture and SpriteFont constructor/upload failures can propagate as
`NotSupportedException`, `InvalidOperationException`, `ArgumentException` or
`ArgumentOutOfRangeException`, including assets supplied by an overridden `OpenStream`), and the
`Game` constructor, which creates the device (`NoSuitableGraphicsDeviceException` when it cannot
be created). Elsewhere a native refusal still
surfaces as `CNA.CnaException` (carrying the canonical type), which XNA code catching a specific
type would miss: the remaining facade calls are converted as they are met (CSX-080).
XNA opens the stream before invoking readers; filesystem existence is not its exception criterion.
One upstream difference remains: a fresh native texture load with a zero-byte first mip reports
`ArgumentOutOfRangeException("elementCount")`; XNA and a fresh managed-stream load report
`ArgumentNullException("data")`.
Open (2026-11-13, found by the CNA next d1c936225 impact review, present since before it):
`AudioEngine.GetGlobalVariable`/`SetGlobalVariable`, `Cue.Apply3D` and the spatial
`SoundBank.PlayCue` overload forward native failures without `XnaExceptions.Guard`, so an invalid or
inaccessible XACT variable reaches XNA code as `CnaException` (canonical
`System.IndexOutOfRangeException`) rather than as `IndexOutOfRangeException`.

## The default graphics profile

`GraphicsDeviceManager` chooses its default `GraphicsProfile` as XNA's does: from the game
assembly's `Microsoft.Xna.Framework.RuntimeProfile` resource, whose first line ends in `Reach` or
`HiDef`, and Reach when there is none. The resource comes from the project's own `<XnaProfile>`
through `build/CNA.XnaCompat.targets` (docs/packaging.md). Until 2026-10-01 every game started on
Reach whatever its project said (CSX-081).

## Profile boundaries

The current 256-type measurement is specifically the selected Windows runtime assemblies. It is
not the entire historical XNA product:

- XACT runtime is included;
- GamerServices, Avatar and Net are measured by their own profile,
  `tools/api-compat/profiles/xna40-gamerservices-net.json` (75/75 types, 0 differences), and the
  runtime profile excludes their namespaces. They are backed by CNA's native gamer services,
  Guide, avatars and network sessions (Local, SystemLink over ENet, and the CNA service for
  PlayerMatch/Ranked). Known boundaries: `LeaderboardWriter` throws `NotSupportedException`, as
  Windows XNA's does (CNA's C++ writes leaderboards but no C route exposes it); a non-empty
  `PropertyDictionary` stream value's contents do not cross the C ABI; a Local session does not
  loop packets back to its own gamers (CNA's choice; XNA's managed IL hands packets to its native
  kernel, so XNA's behaviour there is not measured); and the C ABI cannot tell
  `GamerPrivilegeException` or `NetworkNotAvailableException` apart from other state failures, so
  those arrive as `InvalidOperationException` (CSX-080);
- Windows Phone device APIs live in a separate, opt-in assembly, `CNA.PhoneCompat`, so the strict
  facade carries none of them: `Microsoft.Devices` (`DeviceType`, `Environment`,
  `VibrateController`) and `Microsoft.Devices.Sensors` (`Accelerometer` with both its 7.1
  `CurrentValueChanged` and 7.0 `ReadingChanged` events, `SensorBase<T>`, the readings, event args,
  `SensorState` and the failure exceptions), over CNA's devices module. Its scope is what the
  gallery's phone samples use; their unchanged sources (AccelerometerSample, Bounce, CameraShake,
  MarbleMaze, Platformer, SoundAndMusic) compile against it with `WINDOWS_PHONE` defined. No
  Windows Phone reference assemblies are available here, so its metadata is unmeasured: its
  authority is CNA's C++ phone headers (themselves checked against archived MSDN pages) and those
  samples. `Microsoft.Phone.Shell`/`Notification` (Yacht) are not provided yet. A desktop reports
  `DeviceType.Emulator` and an unsupported accelerometer, as CNA's C++ does;
- Xbox-only APIs need a separate platform profile;
- Content Pipeline/build-time assemblies are a separate product surface and roadmap.

An extinct historical service can still have a useful compile-time facade with deterministic
unsupported/platform behavior. Omission is not justified merely because FNA omitted it.

## Binary compatibility

A game is compiled from source against `CNA.XnaCompat`. A library it uses may instead be a binary
compiled against XNA 4.0 -- physics, animation or UI middleware shipped as a `.dll` -- and that
binary runs unchanged:

- `src/XnaAssemblies` holds XNA's ten runtime assemblies by their simple names and version 4.0.0.0,
  each containing only type forwards to `CNA.XnaCompat` (every public XNA 4.0 type; generated and
  checked by `tools/xna-assemblies` against XNA's reference assemblies), as FNA's `abi/` assemblies
  are for FNA. They carry no public key: CNA.NET does not take Microsoft's identity. The runtime
  resolves the binary's XNA references through them by name.
- The C# compiler, unlike the runtime, compares public key tokens. `CNA.XnaCompat.targets` therefore
  hands it a copy of such a library (`CnaRetargetXnaReferences`) whose XNA references carry no
  token, and the build ships that copy. An x86-only library -- the XNA Game Library default -- is
  also marked loadable by a 64-bit process there, which a pure-IL assembly allows. Nothing else in
  the library changes.

A game references the XNA-named projects its libraries name. Evidence: `XnaBinaryLibraryTests` (an
x86 library compiled with XNA Game Studio 4.0's own reference assemblies) and resonance-game, which
plays with its own prebuilt BEPUphysics v1.0.0. A library that is 32-bit by construction stays so:
that BEPUphysics' x86 build lays out a struct with an object field at offset 20, which no 64-bit
runtime loads; the game's Windows Phone build of the same release is what runs. An XNA game's own
`.exe` is not run as a binary.
