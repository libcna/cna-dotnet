# CNA.NET admission of C ABI 0.60.0

Reviewed 2026-11-05 from CNA `fbd8e64a3fde18beeb2861d546a279d90d0ed5bc` (0.47.0)
to `1fd90048730cf7ec97676ae14d4322a9c3a1f0b9` (0.60.0). Sources are CNA's
`docs/c-api/ABI_VERSIONING.md` (including “Behaviour corrections within ABI 0.60”),
`CLOSURE_059.md`, `CLOSURE_060.md`, the intervening C header/implementation history,
and `docs/downstream-migrations.md`. This is an exact consumer admission, not a claim
that experimental ABI 0.x generations are mutually compatible.

## Shape and identity review

All **1419** `LibraryImport` entry points remain present with identical prototypes. No
interop declaration or existing managed struct needs a shape change. The baseline measures
195 -> 217 native structs with **zero existing layout differences**. Native exports grow
3205 -> 3397 (8 removed, 200 added). Two scalar typedef names disappear and 17 are added;
15 integer constant names disappear, one changes and 84 are added. All 14 string constants
and 11 floating constants are unchanged.

The 26 findings are individually recorded in `eng/cna-upstream-abi-allowlist.txt`:

| Generation | Change | Consumer decision |
| --- | --- | --- |
| 0.48 | SDL_RENDERER (1), VULKAN (8), DIRECTX11 (14), METAL (42) retire; ordinals stay reserved | No managed renderer identity enum names or emits them. No alias is added. |
| 0.49 | Eight `cna_graphics_backend_*` exports, two `CNA_GraphicsBackend*` typedefs and ten classification constants become `renderer` names, preserving values | None is imported/mirrored; no declaration rename is necessary. |
| 0.50 | DisplayColorSpace uint32 typedef restored; five renderer features appended, MAXIMUM 31 -> 36 | No display-color-space route or renderer-feature sentinel is imported. Existing consumer identities are unchanged. |
| 0.51 | SDL_GPU -> SDLGPU, still 31; old textual selector rejected | Binding emits neither textual selector nor constant. |
| 0.52-0.60 | New content, profile, graphics/shader, CNB, service, cursor, declaration and shared-machine/provider routes | Additions do not alter this import set; no speculative imports. In particular live vertex-declaration views are new, not a changed route this consumer calls. |

## Contract review over the consumed surface

Shape equality is insufficient. The following groups classify both implementation corrections
and clarifications of already-existing behavior. Existing wrappers continue to forward native
results, output availability flags and canonical exception diagnostics unless a change is named.

| Review ID | Consumed family | Contract and action |
| --- | --- | --- |
| B-core | `cna_error_*`, all failing routes | NullReferenceException now maps to INVALID_STATE, not INTERNAL. Result ordinals and exception-diagnostic prototypes stay fixed. CnaException records the actual result and canonical type; GamerServicesInterop already maps canonical errors. No success is substituted for a refusal. |
| B-callback | Game/component callbacks; native event subscriptions | Signatures and calling conventions are unchanged. CallbackError bytes must survive the dispatching C route, not just the callback return. Game already uses a lifetime-pinned instance buffer; its comment is corrected. Event contexts are released after unsubscription. New logical-size-provider callbacks are not imported. |
| B-runtime | Game run/tick, manager and device access | RunOneFrame/Tick no longer initialize; public XNA methods keep tick-only behavior. The preceding migration changed initialization-dependent tools/tests/BrowserGameLoop to RunFrameEXT and added a direct regression. Manager disposal retains IGraphicsDeviceManager but removes IGraphicsDeviceService: the facade now does the same. ApplyChanges on profile change and repeated CreateDevice replace the device; manager events now retire the old managed device/facade and create a new one. Old wrappers report disposed and cannot resolve a loan against the replacement. Inactive applied fullscreen BeginDraw forwards CNA_FALSE. |
| B-input | Touch/gamepad/keyboard/mouse/device/sensor routes | Touch mutators and pure dead-zone helpers are not imported. Both facade/native touch snapshots are already read-only and FromNative preserves IsConnected, including connected empty snapshots. Gamepad polling forwards the requested native dead-zone mode; quantization remains native. Touch orientation is one identity, not a combination. Keyboard snapshots need no forced poll; relative mouse reads may consume displacement. Sensor disposal refusals propagate. Cursor description/view ownership is reviewed in B-storage. No new width/value/callback identity. |
| B-query | Occlusion create/begin/end/complete/pixel-count | Native resource disposal preserves query state: complete=false; begin/end/pixel-count refuse. The binding uses typed destruction, which also releases the C token. IsComplete now returns false after its owned token closes without querying a released token; other operations still refuse. XNA Effect and OcclusionQuery suppress Disposing; facade disposal now marks the base disposed without raising that event. No native token is retained after managed disposal. |
| B-target | Render-target info, bind/draw/clear/SetData, ContentLost callbacks | Binding/unbinding preserves ContentLost; draw/flush, successful Clear and completed SetData clear it. Native info is read on every query; no managed cached flag or clearing is added. Regression explicitly checks notification -> binding still lost -> successful clear not lost. Destroy still refuses a bound/retained target; tests unbind before cleanup. |
| B-effects | Effect/material clone, reflection, apply and SpriteBatch | Material/base stock-effect copying retains techniques, passes, parameter values and shared texture references; no extra C texture retention is promised. Framework EffectMaterial.Clone now wraps the native base Effect result as Effect. Regression checks copied tables/values and independent value storage. Stock parameter/pass names and SpriteEffect MatrixTransform are native reflection data, not reconstructed tables. SpriteEffect creation itself is not imported; SpriteBatch uses it internally. Wrong annotation type/shape and non-current pass failures propagate. Semantic lookup follows native case-insensitive matching. |
| B-texture | Encoded Texture2D, texture/cube/volume transfers | Non-DDS zero-alpha texels now have zero RGB after resize. No managed decode overrides native; regression reads a PNG containing nonzero RGB at alpha zero. Typed transfers/profile legality preserve identities; new typed-volume/package routes are not imported. Color-only base volume/cube constructors are unchanged. |
| B-audio | SoundEffect encoded/PCM, pitch/play settings/Apply3D; XACT | ABI 0.59 uses strict PCM WAV (8/16-bit, mono/stereo, 8-48 kHz, RIFF/chunk/loop validation, Int32 length bound, whole-ms duration). FromStream already delegates; documentation drops permissive-decoder claims. Regression checks 100ms WAV and non-RIFF refusal. Pitch and Play settings now refuse out-of-range values instead of clamping; regression checks refusal and unchanged pitch. Apply3D must precede first play and the choice survives Stop. XACT category equality/hash use engine/index, bank creation validates files and retains an opened stream, and released-engine/cue failures propagate through existing mappings. |
| B-net | Packet reader/writer, NetworkSession/Gamer/Machine and events | SendDataOptions is a flag set; recipient routes refuse null, broadcasts use recipient-free routes; empty writers refuse. Capacity now allocates bytes; zero-capacity empty ReceiveData refuses. The preceding migration checks writer parameter names, allocates a positive scratch reader, preserves null Host/OldHost, accounts for deferred AddLocalGamer, per-gamer host bits, local GamerLeft and queued HostChanged. Session/gamer/machine pins require views to release first. Machine copies remain value copies: facade reads fresh copies and destroys their temporary gamer views/copies in finally, without adopting the new unimported shared-identity routes. Session teardown releases gamer views before typed destruction. |
| B-gamer | Gamer collections/cursors, achievements, dictionaries | ABI 0.59 Current returns null before start/after exhaustion, caches Current across mutation and refuses MoveNext/Reset on version change, including same-count mutation. These imports currently have no managed facade caller (the facade enumerates managed lists). Headless native probe exercises null Current, mutation refusal, output preservation and cached Current. Existing nullable output consumers use availability flags. Achievement collection equality/validation, award provenance and dictionary stream inspection corrections remain native. No callback typedef changes. |
| B-buffer | Index/vertex buffer creation, metadata, transfers and ContentLost subscriptions | Owned or valid pre-run/callback devices are accepted; capacity/profile validation remains native. Ordinary index None writes preserve untouched indices, dynamic Discard clears the remainder, and positive WriteOnly readback refuses atomically. Windowed index writes accept None only; the existing wrapper already selects that documented route and drops streaming hints at nonzero offsets as an explicit cost-only tradeoff. Metadata reads native content-loss state (index info currently false; vertex dynamic info now reports the flag). Subscriptions do not retain buffers and must release even after source destruction; existing dynamic-buffer wrappers unsubscribe before typed release. The new mutable declaration-view/assignment/disposal routes are not imported: managed vertex declarations are copied at buffer creation. No lifetime pin or mutable view is silently adopted. |
| B-content | Content manager/readers/registries, models and CNB | Game-owned or standalone device handles are accepted according to source lifetime; wrappers distinguish owned managers from game loans. Root changes refuse cached assets. Unload/destroy disposes loaded effects/models while independently transferred textures/fonts/sounds/videos survive; owned C handles still need typed release. Managed XNB/CNJ model construction uses its existing managed ownership path. Foreign loaders now recognize XNB/CNB/CNJ identities; wrappers do not guess a type. Resource constructor supplies an empty native service provider but cannot load embedded resources by itself; the managed ResourceContentManager supplies its own streams. New content-value/schema-2 routes are not consumed, so no schema-2 layout is mirrored. |
| B-media | Video/player, songs, libraries/pictures/collections/queue | Video association renewal is borrowed and expires at the next player call; wrappers neither adopt it nor destroy it. Frame textures remain borrowed, generation-monotonic; Stop retains association/frame. Failed Play clears association, pre-Play texture queries refuse, volume refuses out of range. Queue views are owned and already typed-destroyed. Song/library disposal now refuses ordinary metadata reads, and wrappers propagate native errors; URI metadata defaults use native values. MediaPlayer song-range playback refuses invalid index before mutating queue. Media collection membership, snapshot and view ownership are documented per route; wrappers already use typed releases. |
| B-storage | Storage container/streams/cursors | Disposed-state publication precedes callbacks; file defaults are exclusive sharing, Append write-only, otherwise read/write. Explicit sharing follows Windows masks/POSIX locks; sharing failures preserve output and file content. Cursor handles own descriptions, including stock views; installing a cursor does not require retaining its C token. Existing wrappers release owned views and forward operations. |
| B-values | Presentation/defaults, math/packed values and remaining imports | PresentationParameters now starts with XNA defaults; wrapper reads native initializer. Existing scalar/struct identities remain fixed. Pure managed math/packed implementations do not call imported helpers merely because they are declared. Runtime metadata/canaries remain enforced, not bypassed. |

The lane report contains the full 1419-row import classification and the 271 individual changed
header-comment deltas, including ownership/error wording; those deltas are distinguished from
machine-level prototype changes (zero). The review does not claim the new unimported services,
provider callbacks, schema-2 routes or mutable declaration views are implemented in CNA.NET.

## Evidence and limits

Linux x64 validation on the local .NET 8 SDK and cached packages:

- Offline restore/build of the complete solution and the native ABI probe succeeded.
- `tools/abi-verify`: 1137 native/managed values, 1419/1419 prototypes, 23 callbacks and
  599 constants; zero mismatches/unresolved mappings. All 12 negative prototype controls rejected,
  zero stale. Compilation uses the platform C compiler and current headers, not a fabricated ABI.
- `tools/coverage/baselinediff.py --from <CNA>@fbd8e64a3 --to <CNA>@1fd900487
  --allowlist eng/cna-upstream-abi-allowlist.txt`: 26 reviewed findings applied, zero stale,
  zero unreviewed differences. Its 10 unit tests passed.
- `Verify-NativeAbiCompatibility.sh`: two accepted fixtures, thirteen rejected fixtures,
  including retired 0.47.0 and unreviewed 0.61.0, missing export, changed signature and guarded
  struct-overwrite controls. The real `cmake-build-sdlgpu/modules/c-api/libcna_c_api.so` passes
  all 1419 export checks, version and core signature/layout canaries and reports 0.60.0.
- A separate headless native cursor check passed before/after Current, same-count mutation,
  output write guard and cached Current assertions. The managed pure compile probe ran.
- VSTest cannot open its TCP listener in this sandbox (SocketException 13). Managed/native
  integration regressions compile but have not executed here. Display/GPU, audio-device,
  Windows/macOS/ARM64/browser runtime and fresh CI native-build qualification remain to run
  on the integration host. This review does not claim them.

Only 0.60.0 remains accepted. Historical 0.47.0 has the right imports/shapes but obsolete
behavior, so keeping it would undermine the reviewed point matrix. The policy fixtures explicitly
prove version-only retirement. CI pins the reviewed CNA commit plus matching local sibling source
revisions; no CNA/native dependency was built or edited for this admission.
