using System.Runtime.InteropServices;

namespace CNA.Interop;

/// <summary>
/// Raw P/Invoke surface over the CNA stable C ABI (<c>modules/c-api/include/CNA/C/cna.h</c> in
/// <c>openeggbert/cna</c>). This is the *only* file in the solution that names the native
/// library. Nothing here is ergonomic on purpose -- see CNA for the idiomatic layer.
///
/// This is a minimal slice covering exactly what the <c>samples/HelloGame</c> vertical slice
/// needs (clear screen, load a texture, draw with SpriteBatch, read the keyboard, exit cleanly),
/// per the first-program milestone in libcna/cna's docs/binding.md. It intentionally
/// does not try to guess the full upstream ABI signature set before that ABI exists -- see
/// plan.md Phase 1 / Phase 4.
///
/// Signatures follow the ABI conventions of libcna/cna's docs/c-api/ (summarized in
/// docs/binding.md, "The boundary rules"): opaque handles,
/// <see cref="CnaResult"/> instead of exceptions, UTF-8 strings, fixed-width primitives, and
/// explicit buffers instead of collections.
/// </summary>
internal static partial class Native
{
    private const string LibraryName = "cna-native";

    // -- ABI versioning (abi.h:114) ----------------------------------------------------------

    [LibraryImport(LibraryName)]
    internal static partial uint cna_get_abi_version();

    // There is deliberately no cna_runtime_initialize / cna_runtime_shutdown here. Both were
    // declared, neither exists in any header, and nothing ever called them -- so unlike
    // cna_content_load_spritefont (which was reachable and would have crashed) these were merely
    // dead fabrications. Found by sweeping every declaration in this file against the headers;
    // 713 of 715 matched. The C API has no explicit runtime lifecycle: a game handle is created
    // and destroyed, and that is the whole of it.

    // -- Error retrieval (real ABI, core.h:154-178, caller-owned two-call size-then-copy) ----

    /// <summary>Matches <c>cna_error_get_last_message_size</c> exactly (<c>core.h:163</c>) --
    /// the earlier <c>cna_get_last_error_message_length</c> guess returned the byte count
    /// directly instead of through a <see cref="CnaResult"/> out-param; the real function has no
    /// equivalent by that name. See <c>CnaError.GetLastErrorMessage</c>.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_error_get_last_message_size(out ulong outBytes);

    /// <summary>The canonical exception type behind the last failure on this thread (ABI 0.37.0,
    /// CBIND-132); zero bytes when the C layer raised the failure itself.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_error_get_last_exception_type_size_ext(out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_error_copy_last_exception_type_ext(byte* destination, ulong capacity, out ulong outBytes);

    /// <summary>An argument exception's parameter name, for the same failure.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_error_get_last_exception_param_name_size_ext(out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_error_copy_last_exception_param_name_ext(byte* destination, ulong capacity, out ulong outBytes);

    /// <summary>Matches <c>cna_error_copy_last_message</c> exactly (<c>core.h:175</c>) -- same
    /// correction as <see cref="cna_error_get_last_message_size"/>: returns <see cref="CnaResult"/>
    /// (<c>BufferTooSmall</c> when <paramref name="capacity"/> is insufficient) rather than a
    /// directly-returned byte count, and always reports the required size through
    /// <paramref name="outBytes"/> regardless of outcome.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_error_copy_last_message(byte* destination, ulong capacity, out ulong outBytes);

    // -- Game lifecycle (real ABI, runtime.h) -------------------------------------------------
    //
    // Replaces the old, guessed cna_managed_game_* names/shapes entirely -- none of them exist
    // upstream by those names. The real API splits lifecycle callbacks across two tables:
    // CnaManagedGameCallbacks (load_content/update/draw/unload_content/exiting, passed inside
    // CnaGameCreateInfo at creation) and CnaGameFrameHooks (initialize/begin_run/end_run/
    // begin_draw/end_draw, installed separately via cna_game_set_frame_hooks_ext after creation)
    // -- see Game.cs's constructor and NEXT.md's native-ABI-migration entry.

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_game_create(in CnaGameCreateInfo createInfo, out CnaHandle game);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_game_set_frame_hooks_ext(CnaHandle game, in CnaGameFrameHooks hooks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_run(CnaHandle game);


    /// <summary>Steps exactly one native frame instead of blocking until exit -- a distinct real
    /// function <see cref="cna_game_run"/> wraps, with no equivalent in the old guessed API.
    /// Unused today (<c>Game.Run()</c> only calls <see cref="cna_game_run"/>); declared here since
    /// it is part of the real, confirmed ABI surface, matching this file's existing practice of
    /// declaring functions ahead of a managed caller that uses them (see the audio/media sections
    /// below).</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_run_one_frame(CnaHandle game);

    /// <summary>Matches <c>cna_game_run_frame_ext</c> (ABI 0.38.0): one frame of a run the host
    /// drives -- the first call begins the run, the call that finds the game exited ends it
    /// (<c>exiting</c>, <c>end_run</c>) and reports <paramref name="running"/> false. The browser's
    /// <c>Game.Run</c>, which cannot block a page.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_run_frame_ext(CnaHandle game, out byte running);

    /// <summary>Matches <c>cna_game_set_foreign_thread_calls_ext</c> (ABI 0.39.0): a call another
    /// thread makes on the game thread's handles runs on the game thread instead of being refused,
    /// which XNA's loading threads need.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_set_foreign_thread_calls_ext(CnaHandle game, byte enabled);

    /// <summary>Matches <c>cna_game_run_foreign_thread_calls_ext</c> (ABI 0.41.0): the game thread
    /// runs the calls other threads queued for it now, while it waits for them, rather than at the
    /// start of its next update or draw.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_run_foreign_thread_calls_ext(CnaHandle game);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_request_exit(CnaHandle game);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_destroy(CnaHandle game);

    /// <summary>Only succeeds from inside an active lifecycle callback for <paramref name="game"/>;
    /// the resulting handle is documented as valid only until that callback returns -- see
    /// <c>Game.GetNativeGraphicsDeviceHandle</c>'s own doc comment for how this project currently
    /// (and only partially correctly) handles that constraint.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_get_graphics_device(CnaHandle game, out CnaHandle graphicsDevice);

    /// <summary>Unlike <see cref="cna_game_get_graphics_device"/>, safe to call at any time with an
    /// owned or callback-borrowed <paramref name="game"/> handle -- a game's content manager is a
    /// stable value member, not a per-callback borrow (see <c>content.h</c>).</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_get_content_manager_ext(CnaHandle game, out CnaHandle contentManager);

    /// <summary>Matches <c>cna_game_set_window_title</c> exactly (<c>runtime.h:246</c>) -- takes an
    /// owned game handle, safe to call any time (not callback-scoped), which is why
    /// <c>CNA.GameWindow.Title</c>'s setter can run from a game's own constructor, the same way
    /// <c>HelloGame</c> in cna-dotnet-template does.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_set_window_title(CnaHandle game, CnaStringView title);

    /// <summary>Matches <c>cna_game_window_get_title_size</c> exactly
    /// (<c>runtime_window.h:126</c>) -- same two-call size-then-copy pattern as
    /// <see cref="cna_error_get_last_message_size"/>, see <c>CNA.GameWindow.Title</c>'s getter.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_window_get_title_size(CnaHandle game, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_game_window_copy_title(CnaHandle game, byte* destination, ulong capacity, out ulong outBytes);

    // The rest of runtime_window.h, added when a sweep of unbound header functions showed
    // GameWindow had exactly one member bound out of a real XNA surface of eight.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_window_get_allow_user_resizing(CnaHandle game, out byte outAllowed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_window_set_allow_user_resizing(CnaHandle game, byte allowed);

    // CSX-114: an XNA game's Windows Forms form drops its border (FormBorderStyle.None).
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_window_get_is_borderless_ext(CnaHandle game, out byte outBorderless);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_window_set_is_borderless_ext(CnaHandle game, byte borderless);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_window_get_client_bounds(CnaHandle game, out CnaRect outBounds);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_window_get_current_orientation(CnaHandle game, out uint outOrientation);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_window_get_screen_device_name_size(CnaHandle game, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_game_window_copy_screen_device_name(
        CnaHandle game, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_window_begin_screen_device_change(CnaHandle game, byte willBeFullScreen);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_window_end_screen_device_change(
        CnaHandle game, CnaStringView screenDeviceName, int clientWidth, int clientHeight);

    /// <summary>Subscribes to one <c>CNA_GAME_WINDOW_EVENT_*</c>. Released with
    /// <c>cna_game_unsubscribe</c>, like every other runtime registration.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_window_subscribe(
        CnaHandle game, uint eventId, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_get_is_mouse_visible(CnaHandle game, out byte outVisible);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_set_is_mouse_visible(CnaHandle game, byte visible);

    // -- GraphicsDevice (real ABI, graphics_device.h) -----------------------------------------
    //
    // Step 3 of the native-ABI migration (see NEXT.md) fixed the confirmed-shape functions below
    // (Clear, SetVertexBuffer, Indices' rename, DrawIndexedPrimitives' real 7-argument arity) and
    // the device-handle-sourcing problem project-wide; step 5 (RenderTarget2D) finished the job by
    // fixing SetRenderTarget's own native call too, once RenderTarget2D had its own real handle
    // type to pass it.

    /// <summary>Matches <c>cna_graphics_device_clear_options</c> exactly
    /// (<c>graphics_device.h:731</c>) -- the real ABI's general clear route; no bare
    /// <c>cna_graphics_device_clear</c> exists at all. See
    /// <c>CNA.Graphics.GraphicsDevice.Clear</c>'s own doc comment for why this specific real
    /// function (of the three real clear routes) is the right match for XNA's simple
    /// <c>Clear(Color)</c> overload.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_clear_options(
        CnaHandle device, CnaClearOptions options, CnaColor color, float depth, int stencil);

    /// <summary>Matches <c>cna_graphics_device_set_render_target2d</c> exactly
    /// (<c>render_target.h:210</c>) -- the old guessed <c>cna_graphics_device_set_render_target</c>
    /// has no real equivalent; render-target binding is type-specific (a separate
    /// <c>_set_render_target_cube</c> also exists, unused here since this project has no cube
    /// render targets). <see cref="CnaHandle.Zero"/> restores the back buffer, same sentinel this
    /// file already used elsewhere before this function's real name was confirmed.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_render_target2d(CnaHandle device, CnaHandle renderTarget);

    /// <summary>Matches <c>cna_graphics_device_set_vertex_buffer</c> exactly
    /// (<c>graphics_device.h:798</c>, "Binds one vertex buffer at vertex offset zero") -- a real,
    /// confirmed name-and-shape match, unlike most of this file before this migration.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_vertex_buffer(CnaHandle device, CnaHandle vertexBuffer);

    /// <summary>Matches <c>cna_graphics_device_set_index_buffer</c> exactly
    /// (<c>graphics_device.h:874</c>) -- the old guessed name was
    /// <c>cna_graphics_device_set_indices</c>; the real function is named for the resource it binds
    /// (<c>index_buffer</c>), not the device property that exposes it (<c>Indices</c>).</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_index_buffer(CnaHandle device, CnaHandle indexBuffer);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_draw_primitives(
        CnaHandle device,
        uint primitiveType,
        int startVertex,
        int primitiveCount);

    /// <summary>Matches <c>cna_graphics_device_draw_indexed_primitives</c> exactly
    /// (<c>graphics_device.h:1013</c>) -- real XNA's own full 7-argument signature, confirmed by
    /// reading the header directly. The old guessed shape had only 5 parameters (missing
    /// <paramref name="minVertexIndex"/>/<paramref name="numVertices"/>) despite matching the real
    /// function's *name* exactly -- the clearest concrete case this migration found of a name match
    /// being no evidence of a shape match.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_draw_indexed_primitives(
        CnaHandle device,
        uint primitiveType,
        int baseVertex,
        int minVertexIndex,
        int numVertices,
        int startIndex,
        int primitiveCount);

    // -- Graphics state (real ABI, graphics_state.h) -------------------------------------------
    //
    // Part of the post-migration "missing XNA surface" push (see NEXT.md): BlendState/
    // DepthStencilState/RasterizerState were guessed-and-never-built during the original
    // migration since the old HelloGame smoke test never touched them. The preset init functions
    // take no device handle at all -- pure value computation, safe to call at any time, including
    // before any game exists -- which is why CNA.Graphics.BlendState/DepthStencilState/
    // RasterizerState's static presets can be plain `static readonly` fields instead of needing
    // lazy/deferred initialization.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_blend_state_init(CnaBlendStatePreset preset, out CnaBlendState outState);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_depth_stencil_state_init(CnaDepthStencilStatePreset preset, out CnaDepthStencilState outState);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_rasterizer_state_init(CnaRasterizerStatePreset preset, out CnaRasterizerState outState);

    /// <summary>Matches <c>cna_graphics_device_get_blend_state</c> exactly
    /// (<c>graphics_state.h:393</c>).</summary>
    /// <summary>/// <b>Passed by <c>ref</c>, not <c>out</c>, despite the header calling it an output.</b> The
    /// implementation validates the caller-supplied header (<c>HasSupportedOutputHeader</c>) before
    /// writing anything, so an <c>out</c> -- which skips the parameterless constructor and so the
    /// version stamp -- is rejected with INVALID_ARGUMENT. Found by calling it: the setter worked
    /// and the getter answered "the sampler-state query is invalid".</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_blend_state(CnaHandle device, ref CnaBlendState outState);

    /// <summary>Matches <c>cna_graphics_device_set_blend_state</c> exactly
    /// (<c>graphics_state.h:404</c>) -- <paramref name="state"/> is "copied during the call" per
    /// the header, so a plain <c>in</c> by-value parameter (not <c>ref</c>) is correct here, unlike
    /// this project's self-populating-constructor structs elsewhere.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_blend_state(CnaHandle device, in CnaBlendState state);

    /// <summary>/// <b>Passed by <c>ref</c>, not <c>out</c>, despite the header calling it an output.</b> The
    /// implementation validates the caller-supplied header (<c>HasSupportedOutputHeader</c>) before
    /// writing anything, so an <c>out</c> -- which skips the parameterless constructor and so the
    /// version stamp -- is rejected with INVALID_ARGUMENT. Found by calling it: the setter worked
    /// and the getter answered "the sampler-state query is invalid".</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_depth_stencil_state(CnaHandle device, ref CnaDepthStencilState outState);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_depth_stencil_state(CnaHandle device, in CnaDepthStencilState state);

    /// <summary>/// <b>Passed by <c>ref</c>, not <c>out</c>, despite the header calling it an output.</b> The
    /// implementation validates the caller-supplied header (<c>HasSupportedOutputHeader</c>) before
    /// writing anything, so an <c>out</c> -- which skips the parameterless constructor and so the
    /// version stamp -- is rejected with INVALID_ARGUMENT. Found by calling it: the setter worked
    /// and the getter answered "the sampler-state query is invalid".</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_rasterizer_state(CnaHandle device, ref CnaRasterizerState outState);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_rasterizer_state(CnaHandle device, in CnaRasterizerState state);

    // -- Texture3D / TextureCube / RenderTargetCube (real ABI, texture_volume.h + render_target.h,
    // Phase 8 WP3b). Each kind has its own create/get_info/destroy triple -- there is no shared
    // texture create or destroy -- which is why CNA.Graphics.Texture leaves ReleaseNative abstract.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_texture3d_create(CnaHandle device, in CnaTexture3DCreateInfo createInfo, out CnaHandle texture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_texture3d_get_info(CnaHandle texture, ref CnaTexture3DInfo info);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_texture3d_destroy(CnaHandle texture);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_texture3d_set_data(
        CnaHandle texture, in CnaTexture3DTransfer transfer, CnaColor* data, ulong dataCapacity);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_texture3d_get_data(
        CnaHandle texture, in CnaTexture3DTransfer transfer, CnaColor* destination, ulong destinationCapacity, out ulong outRequiredElements);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_texture3d_set_data_bytes(
        CnaHandle texture, in CnaTexture3DTransfer transfer, byte* data, ulong dataByteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_texturecube_create(CnaHandle device, in CnaTextureCubeCreateInfo createInfo, out CnaHandle texture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_texturecube_get_info(CnaHandle texture, ref CnaTextureCubeInfo info);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_texturecube_destroy(CnaHandle texture);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_texturecube_set_data(
        CnaHandle texture, in CnaTextureCubeTransfer transfer, CnaColor* data, ulong dataCapacity);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_texturecube_get_data(
        CnaHandle texture, in CnaTextureCubeTransfer transfer, CnaColor* destination, ulong destinationCapacity, out ulong outRequiredElements);

    /// <summary>Matches <c>cna_render_target_cube_create</c> exactly
    /// (<c>render_target.h:187</c>). Released through the shared
    /// <see cref="cna_render_target_destroy"/>, same as the 2D form -- render targets are the one
    /// family whose destroy route *is* shared across shapes.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_render_target_cube_create(
        CnaHandle device, in CnaRenderTargetCubeCreateInfo createInfo, out CnaHandle renderTarget);

    /// <summary>Matches <c>cna_graphics_device_set_render_target_cube</c> exactly
    /// (<c>render_target.h</c>) -- the cube counterpart of
    /// <see cref="cna_graphics_device_set_render_target2d"/>, which this project noticed existed
    /// during the native-ABI migration but had no cube render target to call it with until
    /// now.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_render_target_cube(
        CnaHandle device, CnaHandle renderTarget, uint cubeMapFace);

    /// <summary>Replaces the whole active binding array (<c>render_target.h:238</c>). Zero
    /// bindings restores the backbuffer.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_graphics_device_set_render_targets(
        CnaHandle graphicsDevice, CnaRenderTargetBinding* bindings, ulong bindingCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_render_target_count(
        CnaHandle graphicsDevice, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_graphics_device_copy_render_targets(
        CnaHandle graphicsDevice, CnaRenderTargetBinding* destination, ulong capacity, out ulong outCount);

    /// <summary>Matches <c>cna_texture_get_info</c> exactly (<c>texture.h:130</c>) -- documented
    /// as accepting a "Texture2D, Texture3D, TextureCube or matching render-target handle", which
    /// is what lets <c>CNA.Graphics.Texture</c> expose <c>LevelCount</c>/<c>Format</c> on the base
    /// class instead of duplicating a per-subclass info call.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_texture_get_info(CnaHandle texture, ref CnaTextureInfo info);

    /// <summary>
    /// <c>texture.h</c>. Validates that an element size evenly divides a surface-format unit.
    ///
    /// This is the query <c>Texture2D.GetData</c> claimed the ABI lacked -- "needs a
    /// format-and-element-size query upstream" -- while it sat in the same header as the read it
    /// was guarding. Twelfth entry in plan.md's Corrections table.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_texture_validate_get_data_format(
        uint format, int elementSizeInBytes);

    /// <summary><c>texture.h</c>. Bytes per compression block or uncompressed texel.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_texture_get_format_size(uint format, out int outSize);

    /// <summary>
    /// <c>texture.h</c>. Reads one typed mip/rectangle transfer into a caller array, atomically:
    /// failure does not modify the destination.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_texture2d_set_data(
        CnaHandle texture,
        uint dataType,
        CnaTexture2DTransfer* transfer,
        void* data,
        ulong dataCapacity);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_texture2d_get_data(
        CnaHandle texture,
        uint dataType,
        CnaTexture2DTransfer* transfer,
        void* destination,
        ulong destinationCapacity,
        out ulong outRequiredElements);

    /// <summary>Matches <c>cna_graphics_device_set_texture</c> exactly
    /// (<c>graphics_device.h:642</c>). <see cref="CnaHandle.Zero"/> empties the slot. Binding
    /// stores no ownership native-side -- a destroyed texture unbinds itself.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_texture(
        CnaHandle device, CnaShaderStage stage, uint slot, CnaHandle texture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sampler_state_init(CnaSamplerStatePreset preset, out CnaSamplerState outState);

    /// <summary>Matches <c>cna_graphics_device_get_sampler_state</c> exactly
    /// (<c>graphics_state.h:461</c>) -- unlike the blend/depth/rasterizer trio, a device has two
    /// whole *collections* of these (<paramref name="stage"/>) of
    /// <see cref="CnaSamplerState.MaxSamplers"/> entries each (<paramref name="slot"/>), rather
    /// than one current value.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_sampler_state(
        CnaHandle device, CnaShaderStage stage, uint slot, ref CnaSamplerState outState);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_sampler_state(
        CnaHandle device, CnaShaderStage stage, uint slot, in CnaSamplerState state);

    /// <summary>Matches <c>cna_graphics_device_get_graphics_profile</c> exactly
    /// (<c>graphics_device.h:368</c>).</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_graphics_profile(CnaHandle device, out CnaGraphicsProfile outProfile);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_viewport(CnaHandle device, out CnaViewport outViewport);
    /// <summary>
    /// <c>graphics.h</c>. Queries one capability of the active renderer.
    ///
    /// A recognized but unavailable capability is a <em>successful</em> query that writes false --
    /// not a failure. Operations needing it then answer <c>NOT_SUPPORTED</c> rather than silently
    /// substituting other behaviour, which is what makes asking first worthwhile.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_supports_capability(
        CnaHandle graphicsDevice, uint capability, out byte outSupported);

    /// <summary><c>graphics.h</c>. The renderer's own name, for a caller reporting what it got.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_renderer_name_size(
        CnaHandle graphicsDevice, out ulong outBytes);

    /// <summary>See <see cref="cna_graphics_device_get_renderer_name_size"/>.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_graphics_device_copy_renderer_name(
        CnaHandle graphicsDevice, byte* destination, ulong capacity, out ulong outBytes);


    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_viewport(CnaHandle device, CnaViewport viewport);

    /// <summary>Matches <c>cna_graphics_device_draw_user_primitives</c> exactly
    /// (<c>graphics_device.h:1058</c>) -- "no vertex array is retained after the call returns", so
    /// the pinned pointer inside <paramref name="primitives"/> only needs to stay valid for the
    /// duration of this call, matching <c>CNA.Graphics.GraphicsDevice.DrawUserPrimitives</c>'s own
    /// <see langword="fixed"/> block.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_graphics_device_draw_user_primitives(CnaHandle device, in CnaUserPrimitives primitives);

    /// <summary>Matches <c>cna_graphics_device_draw_user_indexed_primitives</c> exactly
    /// (<c>graphics_device.h:1071</c>). Unlike the non-indexed form, <c>num_vertices</c> in the
    /// primitives descriptor is meaningful here -- the header notes it is "used only by the indexed
    /// route".</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_graphics_device_draw_user_indexed_primitives(
        CnaHandle device, in CnaUserPrimitives primitives, in CnaUserIndices indices);

    // -- Effect / BasicEffect (real ABI, effects.h -- step 8 of the native-ABI migration) -----
    //
    // The old cna_graphics_device_apply_basic_effect (a device-scoped "push these 33 fields as
    // pending per-draw state" call) has no real equivalent at all. Real BasicEffect is a full
    // native object: cna_basic_effect_create returns its own CNA_EffectHandle, every property is a
    // real, immediate get/set round trip (not staged client-side state pushed once at Apply time
    // the way the old design needed, since there was no native object to push it to before now),
    // and cna_effect_apply(effect) selects it on its owning device. World/View/Projection route
    // through the shared IEffectMatrices contract (cna_effect_matrices_*); fog through the shared
    // IEffectFog contract (cna_effect_fog_*); ambient color and the three DirectionalLight members
    // through the shared IEffectLights contract (cna_effect_lights_*) -- all three contracts are
    // shared across every stock effect type, not BasicEffect-specific, matching this project's own
    // IEffectMatrices/IEffectFog/IEffectLights interfaces exactly (confirmed against the real C++
    // engine's own interfaces before this migration ever started). Only VertexColorEnabled/
    // PreferPerPixelLighting/DiffuseColor/EmissiveColor/SpecularColor/SpecularPower/Alpha/
    // TextureEnabled/Texture are BasicEffect-specific (cna_basic_effect_*).
    //
    // Each of the three DirectionalLight members is fetched via cna_effect_lights_get_directional_light
    // into its own CNA_DirectionalLightHandle -- confirmed directly against BasicEffectSmoke.c that
    // this handle is independently owned (it stays valid and usable even after the parent effect is
    // destroyed) and must be released with its own cna_directional_light_destroy call, not freed
    // implicitly with the effect. CNA.Graphics.BasicEffect fetches all three once, at construction,
    // and destroys them in its own Dispose -- matching how it now needs to be IDisposable at all
    // (the old design never allocated anything native until Apply(), so had nothing to dispose).

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_destroy(CnaHandle effect);

    /// <summary>Clones an effect into "an owned clone of the same concrete native type"
    /// (<c>effects.h:1166</c>) -- so a cloned BasicEffect really is a BasicEffect natively, which is
    /// what lets the managed side rewrap it as the same class.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_clone(CnaHandle effect, out CnaHandle outClone);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_apply(CnaHandle effect);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_matrices_get_world(CnaHandle effect, out CnaMatrix outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_matrices_set_world(CnaHandle effect, CnaMatrix value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_matrices_get_view(CnaHandle effect, out CnaMatrix outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_matrices_set_view(CnaHandle effect, CnaMatrix value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_matrices_get_projection(CnaHandle effect, out CnaMatrix outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_matrices_set_projection(CnaHandle effect, CnaMatrix value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_fog_get_color(CnaHandle effect, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_fog_set_color(CnaHandle effect, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_fog_get_enabled(CnaHandle effect, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_fog_set_enabled(CnaHandle effect, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_fog_get_start(CnaHandle effect, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_fog_set_start(CnaHandle effect, float value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_fog_get_end(CnaHandle effect, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_fog_set_end(CnaHandle effect, float value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_lights_get_ambient_color(CnaHandle effect, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_lights_set_ambient_color(CnaHandle effect, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_lights_get_directional_light(CnaHandle effect, uint index, out CnaHandle outLight);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_lights_get_enabled(CnaHandle effect, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_lights_set_enabled(CnaHandle effect, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_lights_enable_default(CnaHandle effect);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_create(CnaHandle graphicsDevice, out CnaHandle effect);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_get_vertex_color_enabled(CnaHandle effect, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_set_vertex_color_enabled(CnaHandle effect, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_get_prefer_per_pixel_lighting(CnaHandle effect, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_set_prefer_per_pixel_lighting(CnaHandle effect, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_get_diffuse_color(CnaHandle effect, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_set_diffuse_color(CnaHandle effect, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_get_emissive_color(CnaHandle effect, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_set_emissive_color(CnaHandle effect, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_get_specular_color(CnaHandle effect, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_set_specular_color(CnaHandle effect, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_get_specular_power(CnaHandle effect, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_set_specular_power(CnaHandle effect, float value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_get_alpha(CnaHandle effect, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_set_alpha(CnaHandle effect, float value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_get_texture_enabled(CnaHandle effect, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_set_texture_enabled(CnaHandle effect, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_get_texture(CnaHandle effect, out byte outHasTexture, out CnaHandle outTexture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_basic_effect_set_texture(CnaHandle effect, CnaHandle texture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_directional_light_destroy(CnaHandle light);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_directional_light_get_diffuse_color(CnaHandle light, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_directional_light_set_diffuse_color(CnaHandle light, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_directional_light_get_direction(CnaHandle light, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_directional_light_set_direction(CnaHandle light, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_directional_light_get_specular_color(CnaHandle light, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_directional_light_set_specular_color(CnaHandle light, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_directional_light_get_enabled(CnaHandle light, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_directional_light_set_enabled(CnaHandle light, byte value);

    // -- Texture2D (real ABI, graphics.h -- step 5 of the native-ABI migration) --------------
    //
    // cna_texture2d_create here (graphics.h) is a different, simpler function from the several
    // creation routes in texture.h (create_standalone/create_from_rgba8/create_cpu_only_rgba8/
    // create_from_file[_with_device]/create_from_encoded_memory) -- this is the one that actually
    // allocates an empty, dimensions-only, device-attached texture, matching real XNA's own
    // new Texture2D(device, width, height). texture.h's own CNA_TextureInfo has no width/height at
    // all; graphics.h's CNA_Texture2DInfo does -- resolves what was an open question before this
    // migration read graphics.h directly.

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_texture2d_create(
        CnaHandle device,
        in CnaTexture2DCreateInfo createInfo,
        out CnaHandle texture);

    // Texture2D.FromStream / SaveAsPng / SaveAsJpeg (texture.h). Found unbound by the same sweep.
    // `decode_info` is optional -- null preserves the source dimensions, which is what real XNA's
    // FromStream does.

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_texture2d_create_from_encoded_memory(
        CnaHandle graphicsDevice,
        byte* encodedData,
        ulong encodedByteCount,
        CnaTexture2DDecodeInfo* decodeInfo,
        out CnaHandle outTexture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_texture2d_get_encoded_byte_count(
        CnaHandle texture, uint imageFormat, uint targetWidth, uint targetHeight, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_texture2d_copy_encoded(
        CnaHandle texture, uint imageFormat, uint targetWidth, uint targetHeight,
        byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_texture2d_destroy(CnaHandle texture);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_texture2d_get_info(CnaHandle texture, ref CnaTexture2DInfo outInfo);

    /// <summary>Matches <c>cna_texture2d_set_data_rgba8</c> exactly (<c>graphics.h:674</c>) --
    /// takes a <c>const CNA_Color*</c> pixel array plus a pixel count, not a raw byte pointer plus
    /// a byte length the way the old guessed shape did.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_texture2d_set_data_rgba8(
        CnaHandle texture,
        CnaColor* pixels,
        ulong pixelCount);

    // -- RenderTarget2D (real ABI, render_target.h -- step 5) ---------------------------------
    //
    // RenderTarget2D is its own real resource type upstream, not a texture created with special
    // usage flags the way this project originally guessed: its own create/get_info/destroy routes,
    // none of which are cna_texture2d_*. CNA.Graphics.RenderTarget2D still subclasses Texture2D
    // (matching real XNA's own RenderTarget2D : Texture2D), but its release/Width/Height now
    // override Texture2D's own (now virtual) implementations to call these functions instead --
    // see RenderTarget2D.cs.

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_render_target2d_create(
        CnaHandle device,
        in CnaRenderTarget2DCreateInfo createInfo,
        out CnaHandle renderTarget);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_render_target_get_info(CnaHandle renderTarget, ref CnaRenderTargetInfo outInfo);

    /// <summary>
    /// <c>render_target.h</c>. The per-target ContentLost subscription this binding recorded as
    /// missing upstream, in the exact words "closing this needs a
    /// <c>cna_render_target_subscribe_content_lost</c> upstream". CNA 0.19.0 has it.
    ///
    /// Its callback is <c>void(handle, void* context)</c> -- two arguments, the same shape as the
    /// vertex/index buffer ContentLost callbacks -- so it goes through
    /// <c>NativeEventBridge.SubscribeWithSender</c> rather than the game/audio one.
    ///
    /// The header is explicit that only a renderer family which can genuinely lose a device ever
    /// raises it -- it names <c>DIRECTX9</c>, <c>DIRECT2D</c> and <c>SKIA</c>, and upstream is
    /// removing <c>SKIA</c>, so that set shrinks rather than grows -- and that a caller-initiated
    /// reset does not. A subscription on any other renderer is valid and silent, which is a real
    /// answer rather than the inert managed event it replaces. Nothing here depends on which
    /// families those are.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_render_target_subscribe_content_lost(
        CnaHandle renderTarget, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_render_target_unsubscribe_content_lost(CnaHandle registration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_render_target_destroy(CnaHandle renderTarget);

    // -- SpriteBatch (real ABI, graphics.h -- step 7 of the native-ABI migration) -------------
    //
    // The best-preserved subsystem in this whole migration: this project's own pre-migration
    // design already funneled every Draw overload (position-based AND destination-rectangle-based)
    // down to one canonical position+scale form before ever touching the command buffer (see
    // SpriteBatch.cs's own DrawEx overloads) -- which turns out to be exactly
    // CNA_SpriteScaledCommand's own shape, one of the *two* real batched-submission routes
    // (cna_sprite_batch_submit_many takes a destination-rectangle CNA_SpriteCommand instead; this
    // project never needs it, since it already resolves rectangles to position+scale in managed
    // code). CnaSpriteDrawCommand's field order already matched CNA_SpriteScaledCommand's exactly
    // before this migration touched it -- only the struct_size/struct_version header was missing.
    // cna_sprite_batch_create/_end were also already an exact name+shape match. Only
    // cna_sprite_batch_begin (needs a real CNA_SpriteBatchBeginInfo now) and the draw/release names
    // needed fixing.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sprite_batch_create(CnaHandle device, out CnaHandle spriteBatch);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sprite_batch_destroy(CnaHandle spriteBatch);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sprite_batch_begin(CnaHandle spriteBatch, in CnaSpriteBatchBeginInfo beginInfo);

    /// <summary>The state-taking <c>Begin</c> (<c>graphics_state.h</c>). Every state pointer is
    /// optional -- null selects the canonical default for that slot (AlphaBlend, LinearClamp, and
    /// so on), which is exactly what real XNA's <see langword="null"/> arguments mean.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_sprite_batch_begin_with_states(
        CnaHandle spriteBatch, uint sortMode, CnaBlendState* blendState, CnaSamplerState* samplerState,
        CnaDepthStencilState* depthStencilState, CnaRasterizerState* rasterizerState);

    /// <summary>As above, plus a custom effect and an optional transform matrix.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_sprite_batch_begin_with_effect(
        CnaHandle spriteBatch, uint sortMode, CnaBlendState* blendState, CnaSamplerState* samplerState,
        CnaDepthStencilState* depthStencilState, CnaRasterizerState* rasterizerState,
        CnaHandle effect, CnaMatrix* transformMatrix);

    /// <summary>Matches <c>cna_sprite_batch_submit_scaled_many</c> exactly
    /// (<c>graphics.h:533</c>) -- the old guessed name, <c>cna_sprite_batch_draw_many</c>, has no
    /// real equivalent, but <see cref="CnaSpriteDrawCommand"/>'s own field shape needed only a
    /// <c>struct_size</c>/<c>struct_version</c> header added, not restructuring -- see this file's
    /// own section comment above.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_sprite_batch_submit_scaled_many(
        CnaHandle spriteBatch,
        CnaSpriteDrawCommand* commands,
        ulong commandCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sprite_batch_end(CnaHandle spriteBatch);

    // -- Keyboard / Mouse / GamePad (real ABI, input.h/input_gamepad.h -- step 11 of the --------
    // native-ABI migration, the last one). Every one of these now needs a game handle
    // (CnaAmbientGame.Current) and returns a real CnaResult (the old guessed shapes were void,
    // silently discarding any failure -- an ABI-independent bug this step also fixes: none of
    // these three static classes checked a result at all before this migration, unlike every
    // other native call site in this codebase). Every out-parameter below is `ref`, not `out` --
    // deliberately, not a style choice: these structs are self-populating (see
    // CnaGameFrameHooks's own constructor doc comment), and `ref` makes it a compile error to
    // pass one without constructing it first, unlike `out`, which would silently let a
    // freshly-declared (zero-initialized, so struct_size/version both zero) struct through to a
    // native call that then rejects it with CNA_RESULT_INVALID_ARGUMENT -- caught only because
    // this exact mistake was made and found earlier in this same migration (see NEXT.md's
    // native-ABI-migration entry for this step; cna_texture2d_get_info/cna_render_target_get_info/
    // cna_sound_effect_instance_get_info were fixed the same way, retroactively).

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_keyboard_get_state(CnaHandle game, ref CnaKeyboardState state);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_mouse_get_state(CnaHandle game, ref CnaMouseState state);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_gamepad_get_state(CnaHandle game, uint playerIndex, ref CnaGamePadState state);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_gamepad_get_capabilities(CnaHandle game, uint playerIndex, ref CnaGamePadCapabilities capabilities);

    /// <summary>Rumble (<c>input_gamepad.h</c>). <c>out_applied</c> reports whether the controller
    /// accepted it -- a pad without motors answers false rather than failing, which is what real
    /// XNA's <c>bool</c> return means.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_gamepad_set_vibration(
        CnaHandle game, uint playerIndex, float leftMotor, float rightMotor, out byte outApplied);

    /// <summary>The dead-zone-mode overload of the state capture (<c>input_gamepad.h</c>), matching
    /// real XNA's <c>GamePad.GetState(PlayerIndex, GamePadDeadZone)</c>.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_gamepad_get_state_with_dead_zone(
        CnaHandle game, uint playerIndex, uint deadZoneMode, ref CnaGamePadState outState);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_keyboard_get_state_for_player(
        CnaHandle game, uint playerIndex, ref CnaKeyboardState outState);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_mouse_set_position(CnaHandle game, int x, int y);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_mouse_get_window_handle(CnaHandle game, out ulong outWindow);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_mouse_set_window_handle(CnaHandle game, ulong window);

    /// <summary>The real device query. Distinct from <c>cna_touch_capabilities_init</c>, which only
    /// fills a default value -- see <c>CNA.Input.TouchPanel.GetCapabilities</c>.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_get_capabilities(CnaHandle game, ref CnaTouchCapabilities outCapabilities);

    // -- Content reader extensibility (real ABI, content_readers.h -- Phase 8 WP14) -----------
    //
    // This is the *extensibility* half of the content system: ContentManager.Load<T> covers the
    // built-in types, while a ContentTypeReader lets a game deserialize its own. The reader reads
    // typed values straight off the stream, which is why the value-reading functions are one per
    // type rather than a generic route.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_reader_create(in CnaContentReaderCreateInfo createInfo, out CnaHandle outReader);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_reader_destroy(CnaHandle reader);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_reader_get_content_manager(CnaHandle reader, out CnaHandle outContentManager);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_reader_get_asset_name_size(CnaHandle reader, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_content_reader_copy_asset_name(
        CnaHandle reader, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_reader_get_version(CnaHandle reader, out int outVersion);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_reader_get_platform(CnaHandle reader, out byte outPlatform);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_reader_read_matrix(CnaHandle reader, out CnaMatrix outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_reader_read_quaternion(CnaHandle reader, out CnaQuaternion outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_reader_read_vector2(CnaHandle reader, out CnaVector2 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_reader_read_vector3(CnaHandle reader, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_reader_read_vector4(CnaHandle reader, out CnaVector4 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_reader_read_color(CnaHandle reader, out CnaColor outValue);

    /// <summary>Reports whether the tag introduced an object at all -- an <c>.xnb</c> object graph
    /// encodes null as a tag with no value, which is why this returns a flag rather than an object
    /// reference.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_reader_read_object_tag(CnaHandle reader, out byte outHasValue);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_content_reader_read_bytes_exact(
        CnaHandle reader, int count, CnaStringView readerName, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_type_reader_manager_get_is_registered(CnaStringView canonicalName, out byte outIsRegistered);

    /// <summary>
    /// <c>content_readers.h:464</c>. The registry's extension point for a type CNA does not know.
    ///
    /// Refusing a duplicate is a deliberate deviation from the canonical <c>AddTypeCreator</c>,
    /// which silently ignores a repeat: a caller who registered a name someone else already owned
    /// would otherwise hold a live handle whose factory is never called, and would find out only
    /// from assets deserializing into the wrong type.
    ///
    /// The registry is process-wide, so a registration outlives any one game.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_content_type_reader_manager_register(
        CnaStringView canonicalName,
        CnaContentTypeReaderCallbacks* callbacks,
        out CnaHandle registration);

    /// <summary>See <see cref="cna_content_type_reader_manager_register"/>. Withdraws the factory
    /// and destroys the registration handle.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_type_reader_manager_unregister(CnaHandle registration);

    /// <summary>
    /// <c>content.h:247</c>. Loads an asset whose root reader the caller registered -- the only
    /// route that reaches a caller-supplied reader from outside. The typed loaders each name a C++
    /// type this ABI knows, and a custom content type is by definition not one of them, so this
    /// needs no type argument: the asset's own type-reader table decides which reader runs.
    ///
    /// <b>Only compiled <c>.xnb</c> assets reach a registered reader.</b> A loose file or a
    /// <c>.cnj</c> descriptor is dispatched by requested C++ type instead, and there is no C++ type
    /// here to dispatch on, so such an asset fails with <c>IO</c> rather than being read by the
    /// wrong reader.
    ///
    /// The <c>void*</c> that comes back is not owned by this call -- neither dereferenced nor
    /// freed -- and its lifetime is whatever the reader that made it says it is.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_manager_load_foreign_ext(
        CnaHandle contentManager, CnaStringView assetName, out nint outObject);

    /// <summary>
    /// <c>content.h</c>. Registers a loader for one <c>"type"</c> value in a <c>.cnj</c> descriptor
    /// -- the <c>.cnj</c> counterpart of <c>cna_content_type_reader_manager_register</c>.
    ///
    /// Together they close what <c>cna_content_manager_load_foreign_ext</c>'s own header used to
    /// note as a limit: a registered reader answers for an <c>.xnb</c>, this answers for a
    /// <c>.cnj</c>, and the load route needs no type argument and does not care which produced the
    /// object.
    ///
    /// <b>Per manager, and no unregister</b>, unlike the reader registry which is process-wide and
    /// hands back a releasable handle. The canonical table belongs to the content manager and goes
    /// with it, so the registration's lifetime is the manager's -- which means the context must
    /// outlive the manager, not merely the call.
    /// </summary>
    /// <remarks><paramref name="callback"/> is a
    /// <c>delegate* unmanaged[Cdecl]&lt;nint, CnaStringView, nint*, CnaResult&gt;</c> passed as
    /// <see cref="nint"/>: Mono's WebAssembly interpreter has no trampoline for a function-pointer
    /// parameter type and aborts the call, so no import here takes one.</remarks>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_manager_register_cnj_loader_ext(
        CnaHandle contentManager,
        CnaStringView typeName,
        nint callback,
        nint context);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_type_reader_manager_create_reader(CnaStringView canonicalName, out CnaHandle outTypeReader);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_type_reader_manager_clear_type_creators();

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_type_reader_destroy(CnaHandle typeReader);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_type_reader_get_can_deserialize_into_existing_object(CnaHandle typeReader, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_type_reader_get_target_type_name_size(CnaHandle typeReader, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_content_type_reader_copy_target_type_name(
        CnaHandle typeReader, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_type_reader_get_type_version(CnaHandle typeReader, out int outVersion);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_type_reader_supports_version(CnaHandle typeReader, int serializedVersion, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_type_reader_initialize(CnaHandle typeReader);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_type_reader_read_untyped(CnaHandle typeReader, CnaHandle reader, out byte outHasValue);

    // -- Game components (real ABI, runtime_components.h -- Phase 8 WP7b) --------------------
    //
    // The native game owns the component collection and drives each component through its own
    // callback table -- which is why this is bound rather than reimplemented managed-side (a
    // managed-only component model would compile and never run; see plan.md WP7).

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_game_component_create(
        CnaHandle game, in CnaGameComponentCallbacks callbacks, out CnaHandle outComponent);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_drawable_game_component_create(
        CnaHandle game, in CnaGameComponentCallbacks callbacks, out CnaHandle outComponent);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_component_destroy(CnaHandle component);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_component_get_enabled(CnaHandle component, out byte outEnabled);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_component_set_enabled(CnaHandle component, byte enabled);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_component_get_update_order(CnaHandle component, out int outOrder);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_component_set_update_order(CnaHandle component, int order);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_component_get_is_drawable(CnaHandle component, out byte outIsDrawable);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_drawable_game_component_get_draw_order(CnaHandle component, out int outOrder);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_drawable_game_component_set_draw_order(CnaHandle component, int order);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_drawable_game_component_get_visible(CnaHandle component, out byte outVisible);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_drawable_game_component_set_visible(CnaHandle component, byte visible);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_components_get_count(CnaHandle game, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_components_get_at(CnaHandle game, ulong index, out CnaHandle outComponent);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_components_add(CnaHandle game, CnaHandle component);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_components_insert(CnaHandle game, ulong index, CnaHandle component);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_components_remove(CnaHandle game, CnaHandle component, out byte outRemoved);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_components_remove_at(CnaHandle game, ulong index);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_components_clear(CnaHandle game);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_components_contains(CnaHandle game, CnaHandle component, out byte outContains);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_components_index_of(CnaHandle game, CnaHandle component, out int outIndex);

    // -- Microphone + DynamicSoundEffectInstance (real ABI, audio.h -- Phase 8 WP11b) ---------
    //
    // Microphones are addressed by index against the game handle, not by their own handle -- the
    // same shape GraphicsAdapter has, and the reason CNA.Audio.Microphone holds an index rather
    // than wrapping a resource.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_microphone_get_count(CnaHandle game, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_microphone_get_default_index_ext(CnaHandle game, out ulong outIndex, out byte outAvailable);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_microphone_get_name_size_at(CnaHandle game, ulong index, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_microphone_copy_name_at(
        CnaHandle game, ulong index, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_microphone_get_buffer_duration_ticks_at(CnaHandle game, ulong index, out long outTicks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_microphone_set_buffer_duration_ticks_at(CnaHandle game, ulong index, long ticks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_microphone_get_is_headset_at(CnaHandle game, ulong index, out byte outHeadset);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_microphone_get_sample_rate_at(CnaHandle game, ulong index, out int outSampleRate);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_microphone_get_state_at(CnaHandle game, ulong index, out uint outState);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_microphone_start_at(CnaHandle game, ulong index);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_microphone_stop_at(CnaHandle game, ulong index);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_microphone_get_data_at(
        CnaHandle game, ulong index, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_microphone_get_sample_duration_ticks_at(
        CnaHandle game, ulong index, int sizeInBytes, out long outTicks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_microphone_get_sample_size_in_bytes_at(
        CnaHandle game, ulong index, long durationTicks, out int outBytes);

    /// <summary>Matches <c>cna_sound_effect_instance_apply_3d</c> exactly
    /// (<c>audio.h:1189</c>) -- positional audio on an *instance*, which is why
    /// <c>SoundEffect.Apply3D</c> creates one rather than being a fire-and-forget call.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_instance_apply_3d(
        CnaHandle instance, in CnaAudioListener listener, in CnaAudioEmitter emitter);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_sound_effect_instance_apply_3d_multi_ext(
        CnaHandle instance, CnaAudioListener* listeners, ulong listenerCount, in CnaAudioEmitter emitter);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dynamic_sound_effect_instance_create(
        CnaHandle game, int sampleRate, uint channels, out CnaHandle outInstance);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dynamic_sound_effect_instance_get_pending_buffer_count(CnaHandle instance, out int outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dynamic_sound_effect_instance_subscribe_buffer_needed(
        CnaHandle instance, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_unsubscribe_ext(CnaHandle registration);

    // The four XACT Disposing events and the storage container's. All `void(void*)`, so they go
    // through NativeEventBridge.Subscribe like the rest.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_engine_subscribe_disposing_ext(
        CnaHandle engine, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_wave_bank_subscribe_disposing_ext(
        CnaHandle waveBank, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_bank_subscribe_disposing_ext(
        CnaHandle soundBank, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cue_subscribe_disposing_ext(
        CnaHandle cue, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_subscribe_disposing(
        CnaHandle container, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_unsubscribe_disposing(CnaHandle registration);

    /// <summary>Matches <c>cna_microphone_subscribe_buffer_ready_at</c> (<c>audio.h:1017</c>).
    /// Indexed like every other microphone route, because microphones are entries in a runtime list
    /// rather than resources a caller owns. See the sibling manager subscribe for why
    /// <paramref name="callback"/> is <see cref="nint"/>.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_microphone_subscribe_buffer_ready_at(
        CnaHandle game, ulong index, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_dynamic_sound_effect_instance_submit_buffer(
        CnaHandle instance, byte* bytes, ulong byteCount, int offset, int count);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dynamic_sound_effect_instance_get_sample_duration_ticks(
        CnaHandle instance, int sizeInBytes, out long outTicks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dynamic_sound_effect_instance_get_sample_size_in_bytes(
        CnaHandle instance, long durationTicks, out int outBytes);

    // -- XACT + 3D audio (real ABI, xact.h + audio.h -- Phase 8 WP11a) ------------------------
    //
    // XACT is the authored-audio half of XNA: an AudioEngine loads a project settings file, wave
    // and sound banks load asset files against it, and cues are played by name. Everything here is
    // handle-based except the listener/emitter, which are plain versioned value structs passed by
    // pointer.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_engine_create(CnaHandle game, CnaStringView settingsFile, out CnaHandle outEngine);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_engine_create_with_renderer(
        CnaHandle game,
        CnaStringView settingsFile,
        long lookAheadTicks,
        CnaStringView rendererId,
        out CnaHandle outEngine);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_engine_destroy(CnaHandle engine);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_engine_get_is_disposed(CnaHandle engine, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_engine_update(CnaHandle engine);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_engine_get_global_variable(CnaHandle engine, CnaStringView name, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_engine_set_global_variable(CnaHandle engine, CnaStringView name, float value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_engine_get_category(CnaHandle engine, CnaStringView name, out CnaHandle outCategory);

    // AudioEngine.RendererDetails (xact.h:122-...). Index-addressed rather than a value struct,
    // because a detail carries two strings -- the header says so explicitly.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_engine_get_renderer_count(CnaHandle engine, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_engine_get_renderer_friendly_name_size(
        CnaHandle engine, ulong rendererIndex, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_audio_engine_copy_renderer_friendly_name(
        CnaHandle engine, ulong rendererIndex, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_engine_get_renderer_id_size(
        CnaHandle engine, ulong rendererIndex, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_audio_engine_copy_renderer_id(
        CnaHandle engine, ulong rendererIndex, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_engine_get_renderer_hash_code(
        CnaHandle engine, ulong rendererIndex, out int outHashCode);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_category_destroy(CnaHandle category);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_category_get_name_size(CnaHandle category, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_audio_category_copy_name(CnaHandle category, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_category_pause(CnaHandle category);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_category_resume(CnaHandle category);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_category_set_volume(CnaHandle category, float volume);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_category_stop(CnaHandle category, uint options);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_category_equals(CnaHandle a, CnaHandle b, out byte outEqual);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_audio_category_get_hash_code(CnaHandle category, out int outHashCode);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_wave_bank_create(CnaHandle engine, CnaStringView fileName, out CnaHandle outWaveBank);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_wave_bank_create_streaming(
        CnaHandle engine, CnaStringView fileName, int offset, short packetSize, out CnaHandle outWaveBank);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_wave_bank_destroy(CnaHandle waveBank);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_wave_bank_get_is_disposed(CnaHandle waveBank, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_wave_bank_get_is_prepared(CnaHandle waveBank, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_wave_bank_get_is_in_use(CnaHandle waveBank, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_bank_create(CnaHandle engine, CnaStringView fileName, out CnaHandle outSoundBank);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_bank_destroy(CnaHandle soundBank);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_bank_get_is_disposed(CnaHandle soundBank, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_bank_get_is_in_use(CnaHandle soundBank, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_bank_get_cue(CnaHandle soundBank, CnaStringView name, out CnaHandle outCue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_bank_play_cue(CnaHandle soundBank, CnaStringView name);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_bank_play_cue_3d(
        CnaHandle soundBank, CnaStringView name, in CnaAudioListener listener, in CnaAudioEmitter emitter);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cue_destroy(CnaHandle cue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cue_get_info(CnaHandle cue, ref CnaCueInfo info);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cue_get_name_size(CnaHandle cue, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cue_copy_name(CnaHandle cue, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cue_apply_3d(CnaHandle cue, in CnaAudioListener listener, in CnaAudioEmitter emitter);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cue_get_variable(CnaHandle cue, CnaStringView name, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cue_set_variable(CnaHandle cue, CnaStringView name, float value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cue_play(CnaHandle cue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cue_pause(CnaHandle cue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cue_resume(CnaHandle cue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cue_stop(CnaHandle cue, uint options);

    // -- Storage (real ABI, storage.h -- Phase 8 WP13) ----------------------------------------
    //
    // The C API deliberately collapses XNA's fake-async BeginXxx/EndXxx pairs into one synchronous
    // call each, invoking CNA_StorageCompletionCallback before returning "so the canonical
    // completion contract is preserved" (storage.h:76-78). CNA.Storage therefore implements
    // Begin/End over an already-completed IAsyncResult rather than inventing real async, and never
    // passes a callback here -- the managed side owns the completion contract.
    //
    // CNA_FileMode and CNA_SeekOrigin map one-to-one onto System.IO.FileMode/SeekOrigin, so those
    // BCL enums cross the boundary by cast rather than getting CNA-flavoured duplicates (design
    // invariant #7).

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_storage_device_show_selector(nint callback, nint context, out CnaHandle outDevice);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_device_destroy(CnaHandle device);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_device_get_free_space(CnaHandle device, out long outFreeSpace);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_device_get_total_space(CnaHandle device, out long outTotalSpace);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_device_get_is_connected(CnaHandle device, out byte outIsConnected);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_device_delete_container(CnaHandle device, CnaStringView titleName);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_storage_container_open(
        CnaHandle device, CnaStringView displayName, nint callback, nint context, out CnaHandle outContainer);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_destroy(CnaHandle container);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_dispose(CnaHandle container);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_get_is_disposed(CnaHandle container, out byte outIsDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_get_display_name_size(CnaHandle container, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_storage_container_copy_display_name(
        CnaHandle container, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_create_directory(CnaHandle container, CnaStringView directory);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_directory_exists(CnaHandle container, CnaStringView directory, out byte outExists);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_delete_directory(CnaHandle container, CnaStringView directory);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_file_exists(CnaHandle container, CnaStringView file, out byte outExists);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_delete_file(CnaHandle container, CnaStringView file);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_get_file_name_count(
        CnaHandle container, CnaStringView searchPattern, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_storage_container_copy_file_name(
        CnaHandle container, CnaStringView searchPattern, ulong index, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_get_directory_name_count(
        CnaHandle container, CnaStringView searchPattern, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_storage_container_copy_directory_name(
        CnaHandle container, CnaStringView searchPattern, ulong index, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_create_file(CnaHandle container, CnaStringView file, out CnaHandle outStream);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_open_file(
        CnaHandle container, CnaStringView file, uint fileMode, out CnaHandle outStream);

    /// <summary>The access- and share-taking <c>OpenFile</c> overloads
    /// (<c>storage.h</c>). <c>CNA_FileMode</c>/<c>CNA_FileAccess</c>/<c>CNA_FileShare</c> match
    /// <see cref="System.IO"/>'s own enums value for value, which is why nothing is translated.
    /// Found unbound by the header sweep.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_open_file_access(
        CnaHandle container, CnaStringView file, uint fileMode, uint fileAccess, out CnaHandle outStream);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_open_file_share(
        CnaHandle container, CnaStringView file, uint fileMode, uint fileAccess, uint fileShare,
        out CnaHandle outStream);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_container_get_storage_device(
        CnaHandle container, out CnaHandle outDevice);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_device_show_selector_for_player(
        uint player, nint callback, nint context, out CnaHandle outDevice);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_device_show_selector_with_space(
        int sizeInBytes, int directoryCount, nint callback, nint context, out CnaHandle outDevice);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_device_show_selector_for_player_with_space(
        uint player, int sizeInBytes, int directoryCount, nint callback, nint context, out CnaHandle outDevice);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_storage_stream_read(CnaHandle stream, byte* destination, ulong capacity, out ulong outRead);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_storage_stream_write(CnaHandle stream, byte* data, ulong count);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_stream_seek(CnaHandle stream, long offset, uint origin, out long outPosition);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_stream_get_position(CnaHandle stream, out long outPosition);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_stream_get_length(CnaHandle stream, out long outLength);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_stream_set_length(CnaHandle stream, long length);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_stream_get_can_read(CnaHandle stream, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_stream_get_can_write(CnaHandle stream, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_stream_get_can_seek(CnaHandle stream, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_stream_flush(CnaHandle stream);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_stream_close(CnaHandle stream);

    // -- Video / VideoPlayer (real ABI, video.h -- Phase 8 WP12) ------------------------------
    //
    // A Video is created against a graphics device (it decodes into a texture); a VideoPlayer is
    // created against the *game*, not the device -- confirmed from the header, and the reason
    // VideoPlayer's managed constructor takes no argument and uses the ambient game handle the way
    // Keyboard/Mouse/TouchPanel already do.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_create(CnaHandle device, CnaStringView fileName, out CnaHandle outVideo);

    /// <summary>
    /// <c>video.h</c>. Creation with the metadata a compiled <c>.xnb</c> carries alongside the file
    /// name.
    ///
    /// XNA's <c>VideoReader</c> writes duration, dimensions, frame rate and soundtrack type into
    /// the asset and passes them to the constructor rather than having the decoder rediscover
    /// them. Without this route the managed reader would have to throw those values away and hope
    /// the container agrees.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_create_with_metadata(
        CnaHandle device,
        CnaStringView fileName,
        int durationMilliseconds,
        int width,
        int height,
        float framesPerSecond,
        uint soundtrackType,
        out CnaHandle outVideo);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_destroy(CnaHandle video);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_get_width(CnaHandle video, out int outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_get_height(CnaHandle video, out int outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_get_frames_per_second(CnaHandle video, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_get_duration(CnaHandle video, out long outTicks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_get_soundtrack_type(CnaHandle video, out uint outType);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_get_file_name_size(CnaHandle video, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_video_copy_file_name(CnaHandle video, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_create(CnaHandle game, out CnaHandle outPlayer);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_destroy(CnaHandle player);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_get_is_disposed(CnaHandle player, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_play(CnaHandle player, CnaHandle video);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_stop(CnaHandle player);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_pause(CnaHandle player);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_resume(CnaHandle player);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_get_state(CnaHandle player, out uint outState);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_get_is_looped(CnaHandle player, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_set_is_looped(CnaHandle player, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_get_is_muted(CnaHandle player, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_set_is_muted(CnaHandle player, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_get_volume(CnaHandle player, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_set_volume(CnaHandle player, float value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_get_play_position_ticks(CnaHandle player, out long outTicks);

    /// <summary>Reports availability separately from the handle: between frames, or before
    /// playback starts, there is genuinely no texture yet -- which is why
    /// <c>CNA.Media.VideoPlayer.GetTexture</c> can return <see langword="null"/> rather than
    /// failing.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_get_texture(CnaHandle player, out CnaHandle outTexture, out byte outAvailable);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_get_video(CnaHandle player, out CnaHandle outVideo, out byte outAvailable);

    // -- Effect reflection surface (real ABI, effects.h -- Phase 8 WP4a) ----------------------
    //
    // Parameters, techniques, passes and annotations are all real native objects reachable from an
    // effect handle. Note the collections and their elements are *borrowed*: cna_effect_get_parameters
    // hands back a collection owned by the effect, and get_at hands back a parameter owned by that
    // collection -- only the explicitly-created ones (cna_effect_parameter_create and friends, which
    // this project never calls) need destroying, which is why nothing here is wrapped in a
    // NativeResourceHandle.

    // Every handle these hand out is documented "Owned" (effects.h:462-465, 809-818, 1252-1270,
    // and every collection get_at/find_*), and each mints a fresh registry slot per call -- they
    // are NOT views aliasing something the effect owns. An earlier revision of this binding claimed
    // the opposite and destroyed none of them, leaking on the per-frame ModelMesh.Draw path.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_destroy(CnaHandle parameter);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_collection_destroy(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_destroy(CnaHandle annotation);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_collection_destroy(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_technique_destroy(CnaHandle technique);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_technique_collection_destroy(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_pass_destroy(CnaHandle pass);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_pass_collection_destroy(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_get_parameters(CnaHandle effect, out CnaHandle outCollection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_get_techniques(CnaHandle effect, out CnaHandle outCollection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_get_current_technique(CnaHandle effect, out CnaHandle outTechnique);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_set_current_technique(CnaHandle effect, CnaHandle technique);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_collection_get_count(CnaHandle collection, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_collection_get_at(CnaHandle collection, ulong index, out CnaHandle outParameter);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_collection_find_name(
        CnaHandle collection, CnaStringView name, out byte outFound, out CnaHandle outParameter);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_collection_find_semantic(
        CnaHandle collection, CnaStringView semantic, out byte outFound, out CnaHandle outParameter);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_get_info(CnaHandle parameter, ref CnaEffectParameterInfo info);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_get_name_byte_count(CnaHandle parameter, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_effect_parameter_copy_name(
        CnaHandle parameter, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_get_semantic_byte_count(CnaHandle parameter, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_effect_parameter_copy_semantic(
        CnaHandle parameter, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_get_elements(CnaHandle parameter, out CnaHandle outCollection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_get_structure_members(CnaHandle parameter, out CnaHandle outCollection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_get_annotations(CnaHandle parameter, out CnaHandle outCollection);

    /// <summary>One function for every scalar/vector/matrix type, discriminated by
    /// <paramref name="valueType"/> -- <paramref name="outValue"/> must point at storage of the
    /// matching size, which is why <c>CNA.Graphics.EffectParameter</c> wraps each call in a typed
    /// method rather than exposing this shape.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_effect_parameter_get_value(CnaHandle parameter, CnaEffectValueType valueType, void* outValue);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_effect_parameter_set_value(CnaHandle parameter, CnaEffectValueType valueType, void* value);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_effect_parameter_get_values(
        CnaHandle parameter, CnaEffectValueType valueType, ulong requestedCount, void* destination, ulong capacity, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_effect_parameter_set_values(
        CnaHandle parameter, CnaEffectValueType valueType, void* values, ulong count);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_get_value_texture(
        CnaHandle parameter, CnaEffectTextureType textureType, out CnaHandle outTexture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_set_value_texture(
        CnaHandle parameter, CnaEffectTextureType textureType, CnaHandle texture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_get_value_string_byte_count(CnaHandle parameter, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_effect_parameter_copy_value_string(
        CnaHandle parameter, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_parameter_set_value_string(CnaHandle parameter, CnaStringView value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_technique_collection_get_count(CnaHandle collection, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_technique_collection_get_at(CnaHandle collection, ulong index, out CnaHandle outTechnique);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_technique_collection_find(
        CnaHandle collection, CnaStringView name, out byte outFound, out CnaHandle outTechnique);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_technique_get_name_byte_count(CnaHandle technique, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_effect_technique_copy_name(
        CnaHandle technique, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_technique_get_passes(CnaHandle technique, out CnaHandle outCollection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_technique_get_annotations(CnaHandle technique, out CnaHandle outCollection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_pass_collection_get_count(CnaHandle collection, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_pass_collection_get_at(CnaHandle collection, ulong index, out CnaHandle outPass);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_pass_collection_find(
        CnaHandle collection, CnaStringView name, out byte outFound, out CnaHandle outPass);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_pass_get_name_byte_count(CnaHandle pass, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_effect_pass_copy_name(CnaHandle pass, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_pass_get_annotations(CnaHandle pass, out CnaHandle outCollection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_pass_apply(CnaHandle pass);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_collection_get_count(CnaHandle collection, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_collection_get_at(CnaHandle collection, ulong index, out CnaHandle outAnnotation);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_collection_find(
        CnaHandle collection, CnaStringView name, out byte outFound, out CnaHandle outAnnotation);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_get_info(CnaHandle annotation, ref CnaEffectAnnotationInfo info);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_get_name_byte_count(CnaHandle annotation, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_effect_annotation_copy_name(
        CnaHandle annotation, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_get_semantic_byte_count(CnaHandle annotation, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_effect_annotation_copy_semantic(
        CnaHandle annotation, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_get_value_boolean(CnaHandle annotation, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_get_value_int32(CnaHandle annotation, out int outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_get_value_single(CnaHandle annotation, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_get_value_matrix(CnaHandle annotation, out CnaMatrix outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_get_value_vector2(CnaHandle annotation, out CnaVector2 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_get_value_vector3(CnaHandle annotation, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_get_value_vector4(CnaHandle annotation, out CnaVector4 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_annotation_get_value_string_byte_count(CnaHandle annotation, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_effect_annotation_copy_value_string(
        CnaHandle annotation, byte* destination, ulong capacity, out ulong outByteCount);

    // -- Stock effects beyond BasicEffect (real ABI, effects.h -- Phase 8 WP4b) ---------------
    //
    // Each is created from a device and then driven through its own property setters, exactly as
    // BasicEffect already is; the shared IEffectMatrices/IEffectFog/IEffectLights contracts
    // (cna_effect_matrices_*/cna_effect_fog_*/cna_effect_lights_*) are already declared above and
    // apply to these too, which is why only the effect-specific members appear here.

    /// <summary>Matches <c>cna_effect_material_create</c> exactly (<c>effects.h:1216</c>) -- note
    /// it clones an *existing* effect rather than taking a device, which is what an EffectMaterial
    /// is: a per-material copy of a shared effect with its own parameter values.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_effect_material_create(CnaHandle cloneSource, out CnaHandle outEffect);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_alpha_test_effect_create(CnaHandle device, out CnaHandle outEffect);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_alpha_test_effect_get_diffuse_color(CnaHandle effect, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_alpha_test_effect_set_diffuse_color(CnaHandle effect, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_alpha_test_effect_get_alpha(CnaHandle effect, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_alpha_test_effect_set_alpha(CnaHandle effect, float value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_alpha_test_effect_get_texture(CnaHandle effect, out byte outHasTexture, out CnaHandle outTexture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_alpha_test_effect_set_texture(CnaHandle effect, CnaHandle texture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_alpha_test_effect_get_vertex_color_enabled(CnaHandle effect, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_alpha_test_effect_set_vertex_color_enabled(CnaHandle effect, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_alpha_test_effect_get_alpha_function(CnaHandle effect, out uint outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_alpha_test_effect_set_alpha_function(CnaHandle effect, uint value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_alpha_test_effect_get_reference_alpha(CnaHandle effect, out int outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_alpha_test_effect_set_reference_alpha(CnaHandle effect, int value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dual_texture_effect_create(CnaHandle device, out CnaHandle outEffect);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dual_texture_effect_get_diffuse_color(CnaHandle effect, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dual_texture_effect_set_diffuse_color(CnaHandle effect, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dual_texture_effect_get_alpha(CnaHandle effect, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dual_texture_effect_set_alpha(CnaHandle effect, float value);

    /// <summary>Matches <c>cna_dual_texture_effect_get_texture</c> exactly
    /// (<c>effects.h:1851</c>). <paramref name="textureIndex"/> selects the layer -- "zero or one"
    /// per the header, which is what makes <c>DualTextureEffect.Texture2</c> bindable at all; an
    /// earlier declaration omitted it, shifting every later argument by one slot.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dual_texture_effect_get_texture(
        CnaHandle effect, uint textureIndex, out byte outHasTexture, out CnaHandle outTexture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dual_texture_effect_set_texture(CnaHandle effect, uint textureIndex, CnaHandle texture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dual_texture_effect_get_vertex_color_enabled(CnaHandle effect, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_dual_texture_effect_set_vertex_color_enabled(CnaHandle effect, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_create(CnaHandle device, out CnaHandle outEffect);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_get_diffuse_color(CnaHandle effect, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_set_diffuse_color(CnaHandle effect, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_get_emissive_color(CnaHandle effect, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_set_emissive_color(CnaHandle effect, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_get_alpha(CnaHandle effect, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_set_alpha(CnaHandle effect, float value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_get_texture(CnaHandle effect, out byte outHasTexture, out CnaHandle outTexture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_set_texture(CnaHandle effect, CnaHandle texture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_get_environment_map(CnaHandle effect, out byte outHasTexture, out CnaHandle outTexture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_set_environment_map(CnaHandle effect, CnaHandle texture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_get_amount(CnaHandle effect, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_set_amount(CnaHandle effect, float value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_get_specular(CnaHandle effect, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_set_specular(CnaHandle effect, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_get_fresnel_factor(CnaHandle effect, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_environment_map_effect_set_fresnel_factor(CnaHandle effect, float value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_create(CnaHandle device, out CnaHandle outEffect);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_get_diffuse_color(CnaHandle effect, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_set_diffuse_color(CnaHandle effect, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_get_emissive_color(CnaHandle effect, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_set_emissive_color(CnaHandle effect, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_get_specular_color(CnaHandle effect, out CnaVector3 outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_set_specular_color(CnaHandle effect, CnaVector3 value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_get_specular_power(CnaHandle effect, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_set_specular_power(CnaHandle effect, float value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_get_alpha(CnaHandle effect, out float outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_set_alpha(CnaHandle effect, float value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_get_prefer_per_pixel_lighting(CnaHandle effect, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_set_prefer_per_pixel_lighting(CnaHandle effect, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_get_texture(CnaHandle effect, out byte outHasTexture, out CnaHandle outTexture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_set_texture(CnaHandle effect, CnaHandle texture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_get_vertex_color_enabled(CnaHandle effect, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_set_vertex_color_enabled(CnaHandle effect, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_get_weights_per_vertex(CnaHandle effect, out int outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_skinned_effect_set_weights_per_vertex(CnaHandle effect, int value);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_skinned_effect_set_bone_transforms(CnaHandle effect, CnaMatrix* transforms, ulong transformCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_skinned_effect_copy_bone_transforms(
        CnaHandle effect, ulong requestedCount, CnaMatrix* destination, ulong capacity, out ulong outCount);

    // -- OcclusionQuery + multi-stream vertex binding (real ABI, graphics_device.h + ------------
    // -- vertex_resources.h -- Phase 8 WP10)

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_occlusion_query_create(CnaHandle device, out CnaHandle outQuery);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_occlusion_query_destroy(CnaHandle query);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_occlusion_query_begin(CnaHandle query);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_occlusion_query_end(CnaHandle query);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_occlusion_query_get_is_complete(CnaHandle query, out byte outIsComplete);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_occlusion_query_get_pixel_count(CnaHandle query, out int outPixelCount);

    /// <summary>Matches <c>cna_graphics_device_set_vertex_buffers</c> exactly
    /// (<c>graphics_device.h:826</c>) -- the multi-stream form
    /// <see cref="cna_graphics_device_set_vertex_buffer"/> is the single-stream shorthand for. An
    /// empty array unbinds every stream; every binding is validated before any is applied.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_graphics_device_set_vertex_buffers(
        CnaHandle device, CnaVertexBufferBinding* bindings, ulong bindingCount);

    // -- GraphicsDeviceManager (real ABI, runtime_graphics_manager.h -- Phase 8 WP6) ----------
    //
    // cna_graphics_device_manager_create is documented as registering the new manager as the
    // game's graphics device manager and graphics device service, and refuses a second manager per
    // game -- which is exactly real XNA's own `new GraphicsDeviceManager(this)` contract, so the
    // managed constructor maps one-to-one onto it rather than deferring creation.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_create(CnaHandle game, out CnaHandle outManager);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_destroy(CnaHandle manager);

    /// <summary>
    /// <c>runtime_graphics_manager.h</c>. The mutable <c>PreparingDeviceSettings</c> subscription --
    /// what the handler writes is what the device is created from.
    ///
    /// This project reported the observation-only sibling as a blocker ("the event delivers its
    /// argument as a const reference, so a C++ subscriber cannot reach the mutable accessor
    /// either", called canonical rather than introduced). It was fixed at the source in CBIND-057;
    /// the <c>_ext</c> suffix marks the shape, not weaker behaviour -- the header is explicit that
    /// the canonical event is exactly this one and the observation-only route exists only because
    /// it was published first.
    ///
    /// The callback returns <c>void</c> deliberately: a handler that cannot decide what to change
    /// changes nothing, and there is no failure device preparation could act on.
    /// </summary>
    /// <remarks><paramref name="callback"/> is a
    /// <c>delegate* unmanaged[Cdecl]&lt;CnaGraphicsDeviceInformation*, nint, void&gt;</c> passed as
    /// <see cref="nint"/>, for the reason <see cref="cna_content_manager_register_cnj_loader_ext"/>
    /// gives.</remarks>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_subscribe_preparing_device_settings_ext(
        CnaHandle manager,
        nint callback,
        nint context,
        out CnaHandle registration);

    /// <summary>Matches <c>cna_graphics_device_manager_subscribe</c>
    /// (<c>runtime_graphics_manager.h:518</c>). <paramref name="callback"/> is a
    /// <c>CNA_GameEventCallback</c>, i.e. <c>void(void*)</c> -- declared as <see cref="nint"/>
    /// rather than as a function-pointer type so no call site needs an <c>unsafe</c> context; the
    /// one place that produces the pointer (<c>CNA.NativeEventBridge</c>) is already unsafe, and
    /// nothing else has any business synthesising one. The same consequence applies as for the
    /// game-component table: nothing may unwind out of that callback.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_subscribe(
        CnaHandle manager, uint eventId, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_unsubscribe(CnaHandle registration);

    // The rest of runtime.h's game surface. A sweep of unbound header functions showed Game was
    // missing nine real XNA members -- IsActive, IsFixedTimeStep, TargetElapsedTime,
    // InactiveSleepTime, ResetElapsedTime, SuppressDraw, Tick, LaunchParameters, and the
    // Activated/Deactivated/Exiting/Disposed events.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_get_is_active(CnaHandle game, out byte outActive);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_get_is_fixed_time_step(CnaHandle game, out byte outFixed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_set_is_fixed_time_step(CnaHandle game, byte isFixed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_get_target_elapsed_time_ticks(CnaHandle game, out long outTicks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_set_target_elapsed_time_ticks(CnaHandle game, long ticks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_get_inactive_sleep_time_ticks(CnaHandle game, out long outTicks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_set_inactive_sleep_time_ticks(CnaHandle game, long ticks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_reset_elapsed_time(CnaHandle game);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_suppress_draw(CnaHandle game);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_tick(CnaHandle game);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_subscribe(
        CnaHandle game, uint eventId, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_launch_parameters_get_count(CnaHandle game, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_launch_parameters_contains_key(
        CnaHandle game, CnaStringView key, out byte outPresent);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_launch_parameters_get_value_size(
        CnaHandle game, CnaStringView key, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_game_launch_parameters_copy_value(
        CnaHandle game, CnaStringView key, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_launch_parameters_add(
        CnaHandle game, CnaStringView key, CnaStringView value);

    /// <summary>The canonical framework pump (<c>runtime.h</c>). <c>FrameworkDispatcher.Update</c>
    /// forwards to it.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_framework_dispatcher_update(CnaHandle game);

    /// <summary>
    /// The title's base content directory, in the ABI's two-call size-then-copy shape.
    ///
    /// The <c>game</c> handle is taken "for thread affinity only" per the header -- the canonical
    /// accessor behind it is static and resolves the executable's directory on first use, which is
    /// why <c>CNA.TitleLocation</c> can be a static class despite these needing a handle.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_title_location_get_path_size(CnaHandle game, out ulong outBytes);

    /// <summary>See <see cref="cna_title_location_get_path_size"/>.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_title_location_copy_path(
        CnaHandle game, byte* destination, ulong capacity, out ulong outBytes);

    /// <summary>
    /// <c>runtime.h:598</c>. The index-addressed key accessor that makes
    /// <c>Game.LaunchParameters</c> materialisable: every other accessor is keyed, which is enough
    /// to read a parameter a caller can already name and not enough to build the map.
    ///
    /// <b>Order is by name, ordinal, ascending</b> -- deliberately not the underlying container's
    /// own order, which is a hash map's and so unspecified between calls. Sorting makes an index a
    /// function of the key set alone. Adding a parameter still invalidates indices taken before it,
    /// so read the count and walk it without adding in between.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_launch_parameters_get_key_size(
        CnaHandle game, ulong index, out ulong outBytes);

    /// <summary>See <see cref="cna_game_launch_parameters_get_key_size"/>.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_game_launch_parameters_copy_key(
        CnaHandle game, ulong index, byte* destination, ulong capacity, out ulong outBytes);

    /// <summary>
    /// <c>content.h:211</c>. The canonical <c>Load&lt;SpriteFont&gt;</c> specialization, reading
    /// both the <c>.xnb</c> font container and CNA's own <c>.cnj</c> font descriptor.
    ///
    /// <b>Two owned handles for one asset</b>, which is why this loader has a parameter the other
    /// three do not: a SpriteFont is a font *and* the atlas it draws from, and handing back only
    /// the font would leave the atlas alive but unnameable. Destroy the font first -- the atlas is
    /// retained while a font uses it, so <c>cna_texture2d_destroy</c> refuses with
    /// <c>INVALID_STATE</c> until then.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_manager_load_sprite_font(
        CnaHandle contentManager,
        CnaStringView assetName,
        out CnaHandle spriteFont,
        out CnaHandle texture);

    /// <summary>
    /// <c>sprite_font.h:146</c>. The inverse of <c>cna_sprite_font_create</c>: the same array that
    /// constructor accepts, in the same order, so a font round-trips without loss. Element
    /// <c>i</c> describes the character <c>cna_sprite_font_copy_characters</c> returns at <c>i</c>.
    ///
    /// It exists because measuring is not drawing. <c>cna_sprite_font_measure_utf8</c> sizes a whole
    /// string, which lays out a text box and cannot place a glyph -- without per-glyph bounds,
    /// cropping and kerning a native-owned font could be measured and never drawn.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_sprite_font_copy_glyphs(
        CnaHandle spriteFont, CnaSpriteFontGlyph* destination, ulong capacity, out ulong outCount);

    /// <summary><c>sprite_font.h</c>. Caller-initialized versioned output, hence <c>ref</c>.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sprite_font_get_info(CnaHandle spriteFont, ref CnaSpriteFontInfo info);

    /// <summary><c>sprite_font.h</c>. UTF-16 code units, so <see cref="ushort"/> maps to C#'s
    /// <see cref="char"/> one for one.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_sprite_font_copy_characters(
        CnaHandle spriteFont, ushort* destination, ulong capacity, out ulong outCount);

    /// <summary><c>sprite_font.h</c>. Must run before the atlas texture is destroyed -- the font
    /// retains it, and <c>cna_texture2d_destroy</c> answers <c>INVALID_STATE</c> until then.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sprite_font_destroy(CnaHandle spriteFont);

    // CNB: CNA's own binary content container. The read path first -- a document is parsed, asked
    // what it is, and destroyed. The writer routes below exist so a test can author a fixture
    // through CNA's own encoder instead of vendoring somebody's content.
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_read_limits_init(ref CnaCnbReadLimits limits);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_document_parse_file(
        CnaStringView path, in CnaCnbReadLimits limits, out CnaHandle outDocument);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_document_destroy(CnaHandle document);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_document_get_container_major(CnaHandle document, out ushort outMajor);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_document_get_container_minor(CnaHandle document, out ushort outMinor);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_document_get_asset_type_id(CnaHandle document, out uint outAssetTypeId);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_document_get_asset_schema_version(
        CnaHandle document, out uint outSchemaVersion);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_document_get_chunk_count(CnaHandle document, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_document_get_chunk(
        CnaHandle document, ulong index, ref CnaCnbChunkEntry outEntry);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_document_copy_chunk_data(
        CnaHandle document, ulong index, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_writer_create(
        uint assetTypeId, uint assetSchemaVersion, out CnaHandle outWriter);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_writer_destroy(CnaHandle writer);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_writer_add_chunk(
        CnaHandle writer, uint type, byte* data, ulong byteCount, uint flags, uint alignment);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_writer_write_to_file(CnaHandle writer, CnaStringView path);

    // -- CNB textures ---------------------------------------------------------------------------
    //
    // The decode side of cnb.h's texture family. `CNA_CnbTextureDataHandle` is a handle rather than
    // a struct because the canonical C++ type is a value made of nested vectors, which C cannot
    // carry: the shape is read through get_info and each level's bytes are copied out one at a
    // time.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_decode_texture2d(CnaHandle document, out CnaHandle outTexture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_texture_data_destroy(CnaHandle texture);

    /// <summary>Caller-initialised and versioned, so <c>ref</c> and not <c>out</c> -- the same
    /// contract <see cref="CnaCnbReadLimits"/> documents, and one no C prototype can express.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_texture_data_get_info(
        CnaHandle texture, ref CnaCnbTextureInfo outInfo);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_texture_data_get_level_dimensions(
        CnaHandle texture, uint level, out uint outWidth, out uint outHeight, out uint outDepth);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_texture_data_get_representation_count(
        CnaHandle texture, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_texture_data_get_representation_format(
        CnaHandle texture, ulong representation, out CnaCnbTextureFormat outFormat);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_texture_data_get_level_count(
        CnaHandle texture, ulong representation, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_texture_data_copy_level(
        CnaHandle texture, ulong representation, ulong level, byte* destination, ulong capacity, out ulong outBytes);

    /// <summary>Takes a predicate CNA calls back for each representation in order. The pointer is a
    /// plain <c>nint</c> here, as every callback in this assembly is; what actually crosses is
    /// checked by <c>CNA.AbiVerify</c>'s callback probe rather than by this declaration.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_texture_data_select_representation(
        CnaHandle texture, nint supported, nint context, out byte outFound, out ulong outIndex);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_texture_format_to_surface_format(
        CnaCnbTextureFormat format, out uint outSurfaceFormat);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_is_block_compressed_texture_format(
        CnaCnbTextureFormat format, out byte outBlockCompressed);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_texture_data_create_rgba8(
        uint width, uint height, byte* rgba, ulong byteCount, out CnaHandle outTexture);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_encode_texture2d(
        CnaHandle texture, CnaStringView contentName, byte* destination, ulong capacity, out ulong outBytes);

    /// <summary>
    /// The current frame together with a monotonic generation -- the "explicit validity generation"
    /// the video blocker row asked for, as an alternative to stable frame-slot identity.
    ///
    /// The texture is borrowed on exactly the terms <c>cna_video_player_get_texture</c> documents,
    /// so this does not make a frame cacheable; what it adds is the ability to say whether the
    /// pixels changed without comparing them.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_video_player_get_frame_ext(
        CnaHandle player, ref CnaVideoFrameExt outFrame);

    // -- CNB sound effects (cnb.h) ------------------------------------------------------------
    //
    // No new audio routes are needed on the other side: a decoded PCM16 sound is samples, a rate
    // and a channel count, which is exactly what SoundEffect's public constructor already takes.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_decode_sound_effect(CnaHandle document, out CnaHandle outSound);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_sound_effect_data_destroy(CnaHandle sound);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_sound_effect_data_get_info(
        CnaHandle sound, ref CnaCnbSoundEffectInfo outInfo);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_sound_effect_data_copy_samples(
        CnaHandle sound, byte* destination, ulong capacity, out ulong outByteCount);

    // The authoring half, so a fixture is written by CNA's own encoder.

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_sound_effect_data_create(
        in CnaCnbSoundEffectInfo info, byte* samples, ulong byteCount, out CnaHandle outSound);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_encode_sound_effect(
        CnaHandle sound, CnaStringView contentName, byte* destination, ulong capacity, out ulong outBytes);

    // -- CNB sprite fonts (cnb.h) -------------------------------------------------------------
    //
    // The glyph struct is the same CNA_SpriteFontGlyph the ordinary SpriteFont path already uses,
    // and the atlas comes out as a CNB texture description the texture slice already knows how to
    // upload -- so a decoded CNB font becomes a real SpriteFont with no new vocabulary.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_decode_sprite_font(CnaHandle document, out CnaHandle outFont);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_sprite_font_data_destroy(CnaHandle font);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_sprite_font_data_get_info(
        CnaHandle font, ref CnaCnbSpriteFontInfo outInfo);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_sprite_font_data_get_glyph(
        CnaHandle font, ulong index, ref CnaSpriteFontGlyph outGlyph);

    /// <summary>The atlas, as a new CNB texture description the caller releases -- not a view into
    /// the font, which is why a font disposed first cannot invalidate one already taken.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_sprite_font_data_copy_atlas(
        CnaHandle font, out CnaHandle outAtlas);

    // The authoring half, so a fixture is written by CNA's own encoder.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_sprite_font_data_create(out CnaHandle outFont);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_sprite_font_data_set_info(
        CnaHandle font, in CnaCnbSpriteFontInfo info);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_sprite_font_data_add_glyph(
        CnaHandle font, in CnaSpriteFontGlyph glyph, out ulong outIndex);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_sprite_font_data_set_atlas(CnaHandle font, CnaHandle atlas);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_encode_sprite_font(
        CnaHandle font, CnaStringView contentName, byte* destination, ulong capacity, out ulong outBytes);

    // -- CNB loader registry (cnb.h) ----------------------------------------------------------
    //
    // The extension mechanism for CNA's own container: a process-wide table mapping a custom asset
    // type identifier to a loader callback, which the header itself describes as matching how the
    // .xnb reader table already works. This binding's ContentTypeReaderRegistration is that table's
    // managed face, and CnbLoaderRegistration is this one's.

    /// <summary>Mints the custom asset type identifier a canonical type name hashes to. The
    /// registry refuses an identifier that does not hash from the name it is registered under, so
    /// this is the only correct way to obtain one.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_asset_type_id_from_name(
        CnaStringView name, out uint outAssetTypeId);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_loader_registry_register(
        uint assetTypeId, CnaStringView canonicalTypeName, nint callback, nint context);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_loader_registry_remove(uint assetTypeId, out byte outRemoved);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_loader_registry_is_registered(
        uint assetTypeId, out byte outRegistered);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_loader_registry_find(
        uint assetTypeId, out byte outFound, out CnaHandle outLoader);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_loader_registry_resolve_for_document(
        CnaHandle document, out CnaHandle outLoader);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_loader_registry_get_registered_type_name_size(
        uint assetTypeId, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_loader_registry_copy_registered_type_name(
        uint assetTypeId, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_loader_registry_register_builtins();

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_loader_destroy(CnaHandle loader);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_loader_invoke(
        CnaHandle loader, CnaHandle document, CnaHandle contentManager,
        CnaStringView assetName, nint* outObject);

    /// <summary>Stamps the container's metadata, which is what carries the canonical type name a
    /// custom-typed file must declare for the registry to resolve it.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_writer_set_metadata(
        CnaHandle writer, CnaStringView assetTypeName, CnaStringView contentName);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_document_get_metadata_asset_type_name_size(
        CnaHandle document, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_document_copy_metadata_asset_type_name(
        CnaHandle document, byte* destination, ulong capacity, out ulong outByteCount);

    // -- CNB models (cnb.h) -------------------------------------------------------------------
    //
    // The read path first: decode a model out of an open document and walk the graph it produced.
    // The write routes below it exist so a test fixture can be authored through CNA's own encoder
    // rather than assembled here from a reading of the decoder, which would test this repository's
    // understanding against itself.

    /// <summary>Decodes a model from an open document. The document's asset type must be
    /// <c>CNA_CNB_ASSET_TYPE_MODEL</c>; anything else is <c>IO</c>. The model is a new owned handle
    /// that outlives the document -- decoding copies rather than viewing.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_decode_model(CnaHandle document, out CnaHandle outModel);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_destroy(CnaHandle model);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_info(CnaHandle model, ref CnaCnbModelInfo outInfo);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_bone(
        CnaHandle model, ulong index, ref CnaCnbModelBone outBone);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_bone_name_size(
        CnaHandle model, ulong index, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_copy_bone_name(
        CnaHandle model, ulong index, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_part(
        CnaHandle model, ulong index, ref CnaCnbModelPartInfo outInfo);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_part_name_size(
        CnaHandle model, ulong index, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_copy_part_name(
        CnaHandle model, ulong index, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_part_external_effect_size(
        CnaHandle model, ulong index, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_copy_part_external_effect(
        CnaHandle model, ulong index, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_copy_part_vertex_bytes(
        CnaHandle model, ulong index, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_copy_part_index_bytes(
        CnaHandle model, ulong index, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_material(
        CnaHandle model, ulong part, ref CnaCnbMaterialInfo outInfo);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_material_texture_size(
        CnaHandle model, ulong part, CnaCnbMaterialTextureSlot slot, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_copy_material_texture(
        CnaHandle model, ulong part, CnaCnbMaterialTextureSlot slot,
        byte* destination, ulong capacity, out ulong outByteCount);

    // The three per-slot routes take a plain **importer** slot index, not a
    // CnaCnbMaterialTextureSlot -- the header says so and calls the difference "a real trap". The
    // two spaces are different sizes (7 against 8) and different orders (emissive and occlusion are
    // swapped), so the parameter is deliberately spelled `ulong` here rather than the name-slot
    // enum: a declaration that accepted the enum would let a call site pass the wrong space without
    // a cast. CnbMaterialTextureSlotMap does the crossing, once.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_material_texture_coordinate_set(
        CnaHandle model, ulong part, ulong importerSlot, out byte outCoordinateSet);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_material_texture_transform(
        CnaHandle model, ulong part, ulong importerSlot, out CnaCnbTextureTransform outTransform);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_material_sampler(
        CnaHandle model, ulong part, ulong importerSlot, out CnaCnbSamplerState outSampler);

    /// <summary>The authoring twins, taking the same importer slot index. Present so a fixture can
    /// give each importer slot a distinguishable value, which is what makes the two slot spaces
    /// tellable apart at all. The transform is the one that can carry seven distinct values: CNA
    /// refuses a coordinate set above 1, because its vertex layouts carry two at most.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_set_material_texture_coordinate_set(
        CnaHandle model, ulong part, ulong importerSlot, byte coordinateSet);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_set_material_texture_transform(
        CnaHandle model, ulong part, ulong importerSlot, in CnaCnbTextureTransform transform);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_mesh(
        CnaHandle model, ulong index, ref CnaCnbMeshInfo outInfo);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_mesh_name_size(
        CnaHandle model, ulong index, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_copy_mesh_name(
        CnaHandle model, ulong index, byte* destination, ulong capacity, out ulong outByteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_copy_mesh_part_indices(
        CnaHandle model, ulong index, uint* destination, ulong capacity, out ulong outIndexCount);

    /// <summary>The skeleton's shape. A model with no skeleton answers <c>InvalidArgument</c>, which
    /// <c>cna_cnb_model_get_info</c>'s <c>has_skeleton</c> distinguishes in advance -- so asking
    /// first is the contract, not a defensive habit.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_get_skeleton(CnaHandle model, ref CnaCnbSkeletonInfo outInfo);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_copy_skeleton_hierarchy(
        CnaHandle model, int* destination, ulong capacity, out ulong outIndexCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_copy_skeleton_matrices(
        CnaHandle model, CnaCnbSkeletonMatrixSet set, float* destination, ulong capacity, out ulong outValueCount);

    // The authoring half. Present so a fixture is written by CNA's own encoder: a byte array
    // assembled here from a reading of the decoder would test this repository's understanding
    // against itself and keep passing if reader and fixture were wrong in the same way.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_create(out CnaHandle outModel);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_set_flags(
        CnaHandle model, byte appliesGltfLightingPolicy, byte hasBoneHierarchy);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_add_bone(
        CnaHandle model, CnaStringView name, int parent, float* transform, out ulong outIndex);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_add_part(
        CnaHandle model, in CnaCnbModelPartInfo info, CnaStringView name,
        CnaStringView externalEffect, out ulong outIndex);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_set_part_vertex_bytes(
        CnaHandle model, ulong index, byte* bytes, ulong byteCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_set_part_index_bytes(
        CnaHandle model, ulong index, byte* bytes, ulong byteCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_set_material(
        CnaHandle model, ulong part, in CnaCnbMaterialInfo info);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_cnb_model_set_material_texture(
        CnaHandle model, ulong part, CnaCnbMaterialTextureSlot slot, CnaStringView assetName);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_add_mesh(
        CnaHandle model, CnaStringView name, int parentBone,
        uint* partIndices, ulong partIndexCount, out ulong outIndex);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_model_set_skeleton(
        CnaHandle model, int* hierarchy, ulong jointCount,
        float* bindPose, float* inverseBindPose, float* rootPrefix);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_cnb_encode_model(
        CnaHandle model, CnaStringView contentName, byte* destination, ulong capacity, out ulong outBytes);

    /// <summary>Known and supported usage masks for one surface format. A bit absent from the known
    /// mask means <em>unknown</em>, not unsupported -- the header says so explicitly, and collapsing
    /// the two would turn "this renderer has not classified BC7" into "this renderer refuses
    /// BC7".</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_surface_format_support_ext(
        CnaHandle graphicsDevice, uint format, out CnaRendererFormatUsage outKnownUsages,
        out CnaRendererFormatUsage outSupportedUsages);

    /// <summary>Builds a native SpriteFont from a glyph table -- the inverse of
    /// <c>cna_sprite_font_copy_glyphs</c>, and the only way to get a font handle for a font this
    /// binding built itself rather than loaded.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_sprite_font_create(
        in CnaSpriteFontCreateInfo createInfo, out CnaHandle outSpriteFont);

    /// <summary>Draws one string through an active batch interval, in one transition rather than
    /// one per glyph. Requires an owned SpriteFont handle belonging to the batch's game.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sprite_batch_draw_string(
        CnaHandle spriteBatch, in CnaSpriteTextCommand command);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_apply_changes(CnaHandle manager);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_create_device(CnaHandle manager);

    /// <summary>Returns whether the frame should be drawn -- the native counterpart of XNA's
    /// <c>IGraphicsDeviceManager.BeginDraw</c>, which reports <see langword="false"/> while the
    /// device cannot present.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_begin_draw(CnaHandle manager, out byte outShouldDraw);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_end_draw(CnaHandle manager);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_toggle_full_screen(CnaHandle manager);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_get_graphics_profile(CnaHandle manager, out CnaGraphicsProfile outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_set_graphics_profile(CnaHandle manager, CnaGraphicsProfile value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_get_is_full_screen(CnaHandle manager, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_set_is_full_screen(CnaHandle manager, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_get_prefer_multi_sampling(CnaHandle manager, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_set_prefer_multi_sampling(CnaHandle manager, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_get_preferred_back_buffer_format(CnaHandle manager, out uint outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_set_preferred_back_buffer_format(CnaHandle manager, uint value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_get_preferred_back_buffer_width(CnaHandle manager, out int outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_set_preferred_back_buffer_width(CnaHandle manager, int value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_get_preferred_back_buffer_height(CnaHandle manager, out int outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_set_preferred_back_buffer_height(CnaHandle manager, int value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_get_preferred_depth_stencil_format(CnaHandle manager, out uint outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_set_preferred_depth_stencil_format(CnaHandle manager, uint value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_get_synchronize_with_vertical_retrace(CnaHandle manager, out byte outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_set_synchronize_with_vertical_retrace(CnaHandle manager, byte value);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_get_supported_orientations(CnaHandle manager, out uint outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_set_supported_orientations(CnaHandle manager, uint value);

    // -- Display / adapter / presentation (real ABI, display.h -- Phase 8 WP5) ----------------
    //
    // Every adapter function takes a graphics-device handle plus an adapter index rather than an
    // adapter handle: adapters are not resources with a lifetime here, they are indices into a
    // list the device can enumerate, refreshed by cna_graphics_adapters_refresh. That is why
    // CNA.Graphics.GraphicsAdapter holds a device + index, not a handle.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_display_mode_init(int width, int height, uint format, out CnaDisplayMode outMode);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_adapter_get_count(CnaHandle device, out ulong outCount);

    /// <summary>Matches <c>cna_graphics_device_get_adapter_index</c> exactly
    /// (<c>graphics_device.h:357</c>) -- the index of the adapter *this device* renders with,
    /// which is not necessarily the machine's default one.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_adapter_index(CnaHandle device, out uint outAdapterIndex);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_adapter_get_info(CnaHandle device, uint adapterIndex, ref CnaGraphicsAdapterInfo info);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_graphics_adapter_copy_description(
        CnaHandle device, uint adapterIndex, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_graphics_adapter_copy_device_name(
        CnaHandle device, uint adapterIndex, byte* destination, ulong capacity, out ulong outBytes);

    /// <summary><c>ref</c>, not <c>out</c>: the header documents <c>out_mode</c> as a
    /// "Caller-initialized versioned output structure", so its
    /// <c>struct_size</c>/<c>struct_version</c> must already be filled in -- which <c>out</c> would
    /// zero, since it does not run the struct's parameterless constructor.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_adapter_get_current_display_mode(
        CnaHandle device, uint adapterIndex, ref CnaDisplayMode mode);

    /// <summary>Matches <c>cna_graphics_adapter_get_display_mode_count</c> exactly
    /// (<c>display.h:255</c>) -- including <paramref name="filterByFormat"/>/<paramref name="format"/>,
    /// which an earlier declaration omitted, shifting <c>out_count</c> into the filter slot.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_adapter_get_display_mode_count(
        CnaHandle device, uint adapterIndex, byte filterByFormat, uint format, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_graphics_adapter_copy_display_modes(
        CnaHandle device, uint adapterIndex, byte filterByFormat, uint format,
        CnaDisplayMode* destination, ulong capacity, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_adapter_is_profile_supported(
        CnaHandle device, uint adapterIndex, CnaGraphicsProfile profile, out byte outSupported);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_adapters_refresh(CnaHandle device);

    /// <summary>
    /// <c>display.h</c>'s <c>CNA_NativeHandleValue</c> is a plain <c>uint64_t</c>, not a handle
    /// struct, so it binds as <see cref="ulong"/> rather than <see cref="CnaHandle"/>.
    ///
    /// Documented to return <c>CNA_RESULT_NOT_SUPPORTED</c> and write zero, always -- it is bound
    /// anyway so <c>CNA.Graphics.GraphicsAdapter.MonitorHandle</c> can exist with its real XNA
    /// signature and start answering for free if the engine ever implements it. (Named in
    /// <c>&lt;c&gt;</c> rather than <c>&lt;see cref&gt;</c>: this is the bottom layer, so it cannot
    /// reference CNA.Framework -- invariant 1.)
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_adapter_get_native_monitor_handle(
        CnaHandle device, uint adapterIndex, out ulong outValue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_presentation_parameters_init(out CnaPresentationParameters outParameters);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_get_preferred_presentation_mode_ext(
        CnaHandle manager, out uint outMode);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_manager_set_preferred_presentation_mode_ext(
        CnaHandle manager, uint mode);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_notify_content_lost_resources_ext(CnaHandle device);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_presentation_parameters_clone(
        in CnaPresentationParameters source, out CnaPresentationParameters outParameters);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_presentation_parameters_get_bounds(
        in CnaPresentationParameters parameters, out CnaRectangle outBounds);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_presentation_parameters(
        CnaHandle device, ref CnaPresentationParameters parameters);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_presentation_parameters(
        CnaHandle device, in CnaPresentationParameters parameters);

    // The rest of graphics_device.h's XNA surface. A sweep of unbound header functions found
    // GraphicsDevice missing its render-state properties, Present/Reset, status/IsDisposed and all
    // six of its events.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_is_disposed(CnaHandle device, out byte outIsDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_status(CnaHandle device, out uint outStatus);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_scissor_rectangle(CnaHandle device, out CnaRect outRect);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_scissor_rectangle(CnaHandle device, CnaRect rect);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_blend_factor(CnaHandle device, out CnaColor outColor);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_blend_factor(CnaHandle device, CnaColor color);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_multi_sample_mask(CnaHandle device, out int outMask);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_multi_sample_mask(CnaHandle device, int mask);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_reference_stencil(CnaHandle device, out int outStencil);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_set_reference_stencil(CnaHandle device, int stencil);

    // -- Mouse cursors (input_cursor.h) ---------------------------------------------------------
    //
    // Not XNA 4.0: XNA's Mouse has GetState, SetPosition and WindowHandle, and nothing about the
    // cursor image. MonoGame added Mouse.SetCursor(MouseCursor), and a game ported from MonoGame
    // calls it -- so this is bound for CNA.XnaCompat.Extensions rather than for the strict facade,
    // where an extra member is a gate failure.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_mouse_cursor_get_stock_ext(
        CnaHandle game, uint stock, out CnaHandle outCursor);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_mouse_cursor_create_from_texture2d(
        CnaHandle game, CnaHandle texture, int originX, int originY, out CnaHandle outCursor);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_mouse_cursor_destroy(CnaHandle cursor);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_mouse_set_cursor_ext(CnaHandle game, CnaHandle cursor);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_present(CnaHandle device);

    /// <summary>
    /// <c>graphics_device.h</c>, added in CNA 0.19.0. The only route in this ABI that *creates* a
    /// device rather than lending out a game's, which is what real XNA's public
    /// <c>GraphicsDevice</c> constructor does.
    ///
    /// The header states the properties that make it usable: several may exist at once, the handle
    /// is accepted everywhere a borrowed one is, resources remember which device made them and
    /// mixing them is refused, and resources on a caller-created device do not gate
    /// <c>cna_game_destroy</c>. This is what closes the corpus's
    /// <c>not-run(CNA-ABI-has-one-game-owned-device)</c> cross-device observation.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_create(
        uint adapterIndex,
        uint graphicsProfile,
        in CnaPresentationParameters parameters,
        out CnaHandle outGraphicsDevice);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_destroy(CnaHandle device);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_dispose(CnaHandle device);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_reset(CnaHandle device);

    /// <summary><paramref name="adapterIndex"/> is a pointer so it can be omitted -- null keeps the
    /// current adapter, which is what real XNA's two-argument <c>Reset</c> overload means.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_graphics_device_reset_with_parameters(
        CnaHandle device, in CnaPresentationParameters parameters, uint* adapterIndex);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_draw_instanced_primitives(
        CnaHandle device, uint primitiveType, int baseVertex, int minVertexIndex, int numVertices,
        int startIndex, int primitiveCount, int instanceCount);

    /// <summary>Subscribes to one <c>CNA_GRAPHICS_DEVICE_EVENT_*</c>. Released with
    /// <c>cna_graphics_device_unsubscribe</c> -- its own family, not the runtime one.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_subscribe_event(
        CnaHandle device, uint deviceEvent, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_unsubscribe(CnaHandle registration);

    // The last of the member-level gaps: back-buffer readback, the two resource events, the bound
    // vertex-buffer count, adapter preferences and format queries, encoded sound-effect decoding,
    // the storage device-changed event, and the two native window handles.

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_graphics_device_get_backbuffer_data_window(
        CnaHandle device, in CnaBackBufferReadback readback, CnaColor* destination, ulong capacity);

    /// <summary>The resource callbacks have three arguments: device, a callback-scoped event-info
    /// pointer, and context. They must not use the two-argument sender bridge. The current ABI
    /// reports only resource presence (created) or name/tag-presence (destroyed), not the managed
    /// resource and tag objects required by XNA, so the strict facade deliberately does not wire
    /// these declarations yet.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_subscribe_resource_created(
        CnaHandle device, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_subscribe_resource_destroyed(
        CnaHandle device, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_vertex_buffer_count(CnaHandle device, out ulong outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_adapter_set_device_preferences(
        CnaHandle device, uint adapterIndex, byte useNullDevice, byte useReferenceDevice);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_adapter_query_render_target_format(
        CnaHandle device, uint adapterIndex, uint profile, uint format, uint depthFormat,
        int multiSampleCount, ref CnaGraphicsFormatSelection outSelection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_adapter_query_backbuffer_format(
        CnaHandle device, uint adapterIndex, uint profile, uint format, uint depthFormat,
        int multiSampleCount, ref CnaGraphicsFormatSelection outSelection);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_sound_effect_create_from_encoded_ext(
        CnaHandle game, byte* bytes, ulong byteCount, out CnaHandle outSoundEffect);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_device_subscribe_device_changed(
        nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_storage_device_unsubscribe_device_changed(CnaHandle registration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_game_window_get_native_handle_ext(CnaHandle game, out ulong outHandle);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_panel_get_window_handle(CnaHandle game, out ulong outWindow);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_panel_get_mouse_touch_emulation_enabled_ext(CnaHandle game, out byte outEnabled);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_panel_set_mouse_touch_emulation_enabled_ext(CnaHandle game, byte enabled);

    /// <summary><c>ref</c> for the same caller-initialized reason as
    /// <see cref="cna_graphics_adapter_get_current_display_mode"/>.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_display_mode(CnaHandle device, ref CnaDisplayMode mode);

    // -- TouchPanel (real ABI, input.h + input_touch.h -- Phase 8 WP9) ------------------------
    //
    // Split across two headers: the state snapshot lives with the rest of input (cna_touch_get_state
    // in input.h, alongside keyboard/mouse/gamepad), while the panel's own configuration and the
    // gesture queue live in input_touch.h. The _ext-suffixed panel functions (enqueue_gesture,
    // raise_touch_event, set_finger, update, reset_for_tests) are CNA test hooks with no XNA
    // counterpart and are deliberately not bound.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_get_state(CnaHandle game, ref CnaTouchState state);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_capabilities_init(ref CnaTouchCapabilities capabilities);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_panel_get_display_width(CnaHandle game, out int outWidth);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_panel_set_display_width(CnaHandle game, int width);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_panel_get_display_height(CnaHandle game, out int outHeight);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_panel_set_display_height(CnaHandle game, int height);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_panel_get_display_orientation(CnaHandle game, out uint outOrientation);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_panel_set_display_orientation(CnaHandle game, uint orientation);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_panel_get_enabled_gestures(CnaHandle game, out uint outGestures);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_panel_set_enabled_gestures(CnaHandle game, uint gestures);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_panel_get_is_gesture_available(CnaHandle game, out byte outAvailable);

    /// <summary>Dequeues the next gesture. <c>CNA_RESULT_INVALID_STATE</c> when the queue is empty,
    /// which <c>CNA.Input.TouchPanel.ReadGesture</c> turns into the documented
    /// <see cref="System.InvalidOperationException"/> real XNA throws in the same
    /// situation.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_touch_panel_read_gesture(CnaHandle game, ref CnaGestureSample sample);

    // -- ContentManager (real ABI, content.h -- step 6 of the native-ABI migration) -----------
    //
    // Renamed throughout (cna_content_* -> cna_content_manager_*) and switched from
    // StringMarshalling.Utf8 (null-terminated) to CnaStringView (pointer+length) -- see
    // CnaStringMarshal.cs. content_manager itself is now always the handle
    // cna_game_get_content_manager_ext returns (see Game.cs step 2), a borrowed handle safe to
    // reuse across calls -- unaffected by that string-marshaling change.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_manager_create(
        CnaHandle graphicsDevice,
        in CnaContentManagerCreateInfo createInfo,
        out CnaHandle contentManager);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_manager_create_resource(
        CnaHandle graphicsDevice,
        in CnaContentManagerCreateInfo createInfo,
        out CnaHandle contentManager);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_manager_destroy(CnaHandle contentManager);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_manager_set_root_directory(CnaHandle contentManager, CnaStringView rootDirectory);

    // Found unbound by the header sweep: the real RootDirectory query (the managed side was
    // answering from a cached field), Unload, and the texture-cube loader.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_manager_get_root_directory_size(
        CnaHandle contentManager, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_content_manager_copy_root_directory(
        CnaHandle contentManager, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_manager_unload(CnaHandle contentManager);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_manager_load_texture_cube(
        CnaHandle contentManager, CnaStringView assetName, out CnaHandle outTexture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_get_is_disposed(CnaHandle soundEffect, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_get_name_size(CnaHandle soundEffect, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_sound_effect_copy_name(
        CnaHandle soundEffect, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_set_name(CnaHandle soundEffect, CnaStringView name);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_instance_get_is_disposed(
        CnaHandle instance, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_manager_load_texture2d(
        CnaHandle contentManager,
        CnaStringView assetName,
        out CnaHandle texture);

    // There is deliberately no `cna_content_load_spritefont` here. One used to be declared, and it
    // named a function that exists in no header -- every Load<SpriteFont> would have died with an
    // EntryPointNotFoundException. content.h loads textures, sound effects and texture cubes and
    // nothing else, so the .xnb SpriteFont container is parsed managed-side instead
    // (CNA.Content.Xnb.XnbSpriteFontReader).

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_manager_load_sound_effect(
        CnaHandle contentManager,
        CnaStringView assetName,
        out CnaHandle soundEffect);

    /// <summary>
    /// <c>effects.h:1262</c>. Maps the canonical <c>Load&lt;Effect&gt;</c> specialization, and reads
    /// all three shapes CNA supports: a compiled <c>.xnb</c> Effect asset, a <c>.cnj</c> descriptor
    /// naming a stock effect, and a <c>.cnj</c> descriptor carrying custom shader source. What comes
    /// back is an ordinary owned effect handle, destroyed with <c>cna_effect_destroy</c>.
    ///
    /// Declared beside the other content loaders even though it lives in <c>effects.h</c> rather
    /// than <c>content.h</c>, because that is where its callers are.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_content_manager_load_effect(
        CnaHandle contentManager,
        CnaStringView assetName,
        out CnaHandle effect);

    /// <summary>
    /// <c>effects.h:1234</c>. Builds an effect straight from compiled Effect Framework bytecode,
    /// for a caller holding bytes rather than an asset name.
    ///
    /// This is the route whose doc once said it answered <c>NOT_SUPPORTED</c> "while native CNA
    /// bytecode loading is unavailable", and that sentence outlived the implementation -- it works,
    /// subject to the renderer's <c>COMPILED_EFFECTS</c> capability. It was listed as this
    /// binding's single largest functional blocker on the strength of that stale sentence alone.
    /// </summary>
    /// <summary>
    /// <c>effects.h:1281</c>. A custom effect from shader <em>source</em>, which is a different
    /// capability from compiled bytecode -- <c>CUSTOM_EFFECTS</c> rather than
    /// <c>COMPILED_EFFECTS</c>, and the SOFTWARE renderer reports the first true and the second
    /// false.
    ///
    /// That distinction is why this is bound at all. This project had recorded custom shaders as
    /// blocked on the strength of the compiled route, and both are needed for the whole story: a
    /// game shipping a compiled <c>.fx</c> still cannot load it here, and a game that can supply
    /// source now can.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_shader_effect_create(
        CnaHandle graphicsDevice,
        CnaStringView vertexSource,
        CnaStringView fragmentSource,
        out CnaHandle effect);

    /// <summary>
    /// <c>graphics.h:661</c>. Which dialect a source-based effect's text must be written in.
    ///
    /// Required rather than optional: source is renderer-specific text, and the header is explicit
    /// that the renderer identity is not a safe way to infer it. Guessing from the name is wrong in
    /// a build carrying more than one backend.
    ///
    /// <b>This was first declared as <c>cna_graphics_device_get_shading_dialect</c>, which exists
    /// in no header.</b> That name was taken from the prose at <c>graphics.h:634</c> -- "gets the
    /// shading dialect a custom effect's sources must be written in" -- rather than from the
    /// declaration eight lines below it, and the grep that "found" it had the invented name in the
    /// pattern. Every declaration here must be copied from a <c>CNA_C_API</c> line, never
    /// reconstructed from a sentence about one.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_graphics_device_get_shader_dialect_ext(
        CnaHandle graphicsDevice, out uint outDialect);

    /// <summary>
    /// <c>effects.h:1465</c>. What the renderer concluded about a source effect's text.
    ///
    /// Read the answer asymmetrically, as the header instructs: <c>CNA_TRUE</c> means *nothing
    /// rejected this source*, which is weaker than *this will draw* -- the software rasterizer
    /// accepts any non-empty text and reports true. <c>CNA_FALSE</c> is the strong answer, because
    /// it means a renderer looked and refused.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_shader_effect_is_valid(CnaHandle effect, out byte outIsValid);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_effect_create_compiled(
        CnaHandle graphicsDevice,
        byte* effectCode,
        ulong effectCodeCount,
        out CnaHandle effect);

    // -- SoundEffect / SoundEffectInstance (real ABI, audio.h -- step 9 of the native-ABI --------
    // migration). Renamed throughout (cna_soundeffect_* -> cna_sound_effect_*,
    // cna_soundeffectinstance_* -> cna_sound_effect_instance_*) and every creation/instance call
    // now needs the CnaAmbientGame.Current handle from step 2's design -- the real ABI has no
    // parameterless audio route anywhere. cna_sound_effect_create_pcm16_range_ext ("the canonical
    // seven-argument constructor") is a genuine, confirmed match for this project's own
    // offset/count/loopStart/loopLength constructor shape -- see CnaSoundEffectCreateInfo's own
    // doc comment for why the loop region is a route parameter, not a CreateInfo field.
    // cna_sound_effect_instance_get_state/_get_volume/_get_pitch/_get_pan/_get_is_looped don't
    // exist at all -- only one combined cna_sound_effect_instance_get_info snapshot -- but every
    // individual setter does still exist, an asymmetric shape confirmed directly, not assumed.

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_sound_effect_create_pcm16_range_ext(
        CnaHandle game,
        in CnaSoundEffectCreateInfo createInfo,
        byte* pcmBytes,
        ulong byteCount,
        int offset,
        int count,
        int loopStart,
        int loopLength,
        out CnaHandle soundEffect);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_destroy(CnaHandle soundEffect);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_get_duration_ticks(CnaHandle soundEffect, out long outDurationTicks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_create_instance(CnaHandle soundEffect, out CnaHandle instance);

    /// <summary>Fire-and-forget playback (<c>audio.h:483</c>). <c>out_played</c> is the canonical
    /// "instance limit reached" answer -- <c>CNA_FALSE</c> rather than a failure, which is also
    /// what a disposed effect reports.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_play(CnaHandle soundEffect, out byte outPlayed);

    /// <summary>(<c>audio.h:499</c>.) The canonical asymmetry is preserved by native: pan is
    /// range-checked and pitch is clamped.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_play_with_settings(
        CnaHandle soundEffect, float volume, float pitch, float pan, out byte outPlayed);

    // The four process-wide audio settings (audio.h:408-471). Game-handle addressed, like every
    // other route in this header.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_get_master_volume(CnaHandle game, out float outVolume);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_set_master_volume(CnaHandle game, float volume);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_get_distance_scale(CnaHandle game, out float outScale);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_set_distance_scale(CnaHandle game, float scale);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_get_doppler_scale(CnaHandle game, out float outScale);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_set_doppler_scale(CnaHandle game, float scale);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_get_speed_of_sound(CnaHandle game, out float outSpeed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_set_speed_of_sound(CnaHandle game, float speed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_instance_destroy(CnaHandle instance);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_instance_play(CnaHandle instance);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_instance_pause(CnaHandle instance);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_instance_resume(CnaHandle instance);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_instance_stop(CnaHandle instance, byte immediate);

    /// <summary>The only real way to read state/volume/pitch/pan/is_looped -- see this section's
    /// own comment.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_sound_effect_instance_get_info(CnaHandle instance, ref CnaSoundEffectInstanceInfo outInfo);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_instance_set_volume(CnaHandle instance, float volume);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_instance_set_pitch(CnaHandle instance, float pitch);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_instance_set_pan(CnaHandle instance, float pan);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_sound_effect_instance_set_is_looped(CnaHandle instance, byte isLooped);

    // -- VertexBuffer / IndexBuffer / VertexDeclaration (real ABI, step 4 of the native-ABI ------
    // migration -- vertex_resources.h/index_resources.h). Replaces the old cna_vertexbuffer_*/
    // cna_indexbuffer_* names entirely -- neither exists upstream by those names. VertexDeclaration
    // is now a real native resource of its own (create/destroy/get_stride/copy_elements); a vertex
    // buffer's create call takes a *handle* to one, "copied into the buffer" -- so
    // VertexBuffer.cs builds a declaration, passes it to cna_vertex_buffer_create, and destroys it
    // again immediately, rather than keeping it alive for the vertex buffer's own lifetime.
    //
    // Confirmed directly with cnabinding (openeggbert/cna's own binding author) rather than
    // inferred: no raw-bytes vertex readback route exists anywhere in the ABI (only typed transfer
    // for the 7 built-in CNA_VertexType values) because CNA's own C++ VertexBuffer has no generic
    // GetData<T> either -- 14 concrete typed overloads and nothing else -- so this is not a gap
    // this C binding introduced. IndexBuffer has no such restriction: cna_index_buffer_get_data
    // only selects a 16-/32-bit width, so any 2- or 4-byte unmanaged T maps onto it directly, same
    // as CNA's own C++ IndexBuffer (uint16_t/uint32_t overloads only, which is the whole story for
    // an index type anyway). See CnaVertexIndexResources.cs for the struct shapes and
    // CNA.Framework's VertexBuffer.cs/IndexBuffer.cs for how each is actually used.

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_vertex_declaration_create_with_stride(
        int vertexStride,
        CnaVertexElement* elements,
        ulong elementCount,
        out CnaHandle declaration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_vertex_declaration_destroy(CnaHandle declaration);

    /// <summary>Caller-initialized versioned output, so <c>ref</c> over a <c>new</c>-constructed
    /// local rather than <c>out</c> -- <c>out</c> skips the parameterless constructor that fills
    /// the struct header.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_vertex_buffer_get_info(CnaHandle vertexBuffer, ref CnaVertexBufferInfo info);

    /// <summary>Typed transfer for the built-in vertex layouts. Unlike the raw upload routes,
    /// the write descriptor carries <c>CNA_SetDataOptions</c>.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_vertex_buffer_set_data(
        CnaHandle vertexBuffer,
        in CnaVertexBufferTransfer transfer,
        void* data,
        ulong capacity);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_vertex_buffer_get_data(
        CnaHandle vertexBuffer,
        in CnaVertexBufferTransfer transfer,
        void* destination,
        ulong capacity,
        out ulong outElementCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_index_buffer_get_info(CnaHandle indexBuffer, ref CnaIndexBufferInfo info);

    /// <summary>The ContentLost callback is <c>void(handle, void* context)</c> -- two arguments,
    /// unlike the game/audio families -- so it goes through
    /// <c>NativeEventBridge.SubscribeWithSender</c>.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_vertex_buffer_subscribe_content_lost(
        CnaHandle vertexBuffer, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_vertex_buffer_unsubscribe_content_lost(CnaHandle registration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_index_buffer_subscribe_content_lost(
        CnaHandle indexBuffer, nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_index_buffer_unsubscribe_content_lost(CnaHandle registration);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_vertex_buffer_create(
        CnaHandle graphicsDevice,
        in CnaVertexBufferCreateInfo createInfo,
        out CnaHandle vertexBuffer);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_vertex_buffer_destroy(CnaHandle vertexBuffer);

    /// <summary>Matches <c>cna_vertex_buffer_set_data_raw</c> exactly
    /// (<c>vertex_resources.h:346</c>) -- always uploads starting at native buffer vertex zero and
    /// has no <c>SetDataOptions</c> parameter.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_vertex_buffer_set_data_raw(
        CnaHandle vertexBuffer,
        byte* data,
        ulong dataByteCount,
        ulong vertexCount,
        uint vertexStride);

    /// <summary>
    /// <c>vertex_resources.h</c>. Uploads into a window of the buffer, leaving the rest alone.
    ///
    /// <c>bufferOffsetInBytes</c> indexes <b>the buffer</b>, which is what XNA's
    /// <c>offsetInBytes</c> means -- every other transfer route here offsets the caller's array.
    /// This is the route the "buffer transfers always start at element zero" gap asked for; the
    /// throw it replaces was written before it existed.
    ///
    /// The header is candid that the deviation is about cost, not result: the renderer contract
    /// underneath replaces whole-buffer contents, so the window is composed CPU-side and the whole
    /// buffer is re-uploaded. Bytes land exactly where XNA puts them; the transfer is not smaller.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_vertex_buffer_set_data_raw_at(
        CnaHandle vertexBuffer,
        ulong bufferOffsetInBytes,
        void* data,
        ulong dataByteCount,
        ulong vertexCount,
        uint vertexStride);

    /// <summary>
    /// <c>vertex_resources.h</c>. The optioned form of <see cref="cna_vertex_buffer_set_data_raw"/>,
    /// added in CNA 0.19.0.
    ///
    /// Until it existed, <c>SetDataOptions</c> reached the ABI only through the built-in typed
    /// upload, so this binding refused every non-<c>None</c> raw or custom-stride upload rather
    /// than dropping the hint. That refusal is no longer correct.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_vertex_buffer_set_data_raw_with_options(
        CnaHandle vertexBuffer,
        void* data,
        ulong dataByteCount,
        ulong vertexCount,
        uint vertexStride,
        uint options);

    /// <summary>
    /// <c>vertex_resources.h</c>. The optioned form of
    /// <see cref="cna_vertex_buffer_set_data_raw_at"/>, added in CNA 0.19.0.
    ///
    /// The header records a deviation about cost rather than result: a windowed upload cannot keep
    /// <c>NoOverwrite</c>'s promise that nothing the GPU may still be reading is touched, so the
    /// renderer receives the whole buffer. Bytes land where XNA puts them; the transfer is merely
    /// larger. That is materially different from the previous behaviour, which was to refuse.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_vertex_buffer_set_data_raw_at_with_options(
        CnaHandle vertexBuffer,
        ulong bufferOffsetInBytes,
        void* data,
        ulong dataByteCount,
        ulong vertexCount,
        uint vertexStride,
        uint options);

    /// <summary>
    /// <c>vertex_resources.h</c>. Raw readback from a window of the buffer.
    ///
    /// The header states its own reason: typed readback covers only the built-in
    /// <c>CNA_VertexType</c> layouts, so a buffer written with a custom layout through the raw
    /// route could never be read back, and "that asymmetry had no reason behind it". This binding
    /// reported that asymmetry as a gap and then kept a throw saying the ABI "has no raw-bytes
    /// vertex readback" after it was closed.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_vertex_buffer_get_data_raw(
        CnaHandle vertexBuffer,
        ulong bufferOffsetInBytes,
        void* destination,
        ulong destinationByteCount,
        ulong vertexCount,
        uint vertexStride);

    /// <summary>
    /// <c>index_resources.h</c>. The <c>offsetInBytes</c> overload beside
    /// <c>cna_index_buffer_set_data</c>. <c>transfer.start_index</c> still indexes the caller's
    /// array, as everywhere else; only <c>bufferOffsetInBytes</c> indexes the buffer.
    /// </summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_index_buffer_set_data_at(
        CnaHandle indexBuffer,
        ulong bufferOffsetInBytes,
        CnaIndexBufferTransfer* transfer,
        void* data,
        ulong capacity);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_index_buffer_create(
        CnaHandle graphicsDevice,
        in CnaIndexBufferCreateInfo createInfo,
        out CnaHandle indexBuffer);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_index_buffer_destroy(CnaHandle indexBuffer);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_index_buffer_set_data(
        CnaHandle indexBuffer,
        in CnaIndexBufferTransfer transfer,
        byte* data,
        ulong capacity);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_index_buffer_get_data(
        CnaHandle indexBuffer,
        in CnaIndexBufferTransfer transfer,
        byte* destination,
        ulong capacity,
        out ulong outElementCount);

    // -- Song (real ABI, media.h -- step 10 of the native-ABI migration) ---------------------
    //
    // Song is a real native object upstream (CNA_SongHandle, cna_song_*) -- this project's own
    // pre-migration Song was pure C# state with a *file path string* masquerading as "Handle",
    // never crossing the ABI until MediaPlayer.Play (which then passed that path straight to a
    // fictional cna_mediaplayer_play(path) route). cna_song_create/_create_with_duration are a
    // genuine, confirmed match for this project's own two constructors (the "explicit duration in
    // milliseconds" one already matched real XNA's real 3-argument constructor exactly). See
    // CNA.Media.Song.cs for how the file-path text is still kept client-side (for
    // MediaPlayer.LoadSong's defensive-copy pattern) alongside the new native handle.

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_song_create(CnaHandle game, CnaStringView fileName, CnaStringView name, out CnaHandle outSong);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_song_create_with_duration(
        CnaHandle game, CnaStringView fileName, CnaStringView assetName, int durationMilliseconds, out CnaHandle outSong);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_destroy(CnaHandle song);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_get_duration(CnaHandle song, out long outTicks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_set_duration(CnaHandle song, long ticks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_get_is_protected(CnaHandle song, out byte outProtected);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_get_is_rated(CnaHandle song, out byte outRated);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_get_play_count(CnaHandle song, out int outPlayCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_set_play_count(CnaHandle song, int playCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_get_rating(CnaHandle song, out int outRating);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_get_track_number(CnaHandle song, out int outTrackNumber);

    // The rest of media.h's song surface, added with the media-library rebinding: a song reached
    // through a library collection has no client-side name or path for the managed wrapper to have
    // kept, so both have to come from native.

    /// <summary>Media-source enumeration (<c>media.h:128-178</c>). Index-addressed rather than
    /// handle-based, and the header is explicit that the list is "a point-in-time snapshot -- an
    /// index is valid only until the device set changes", which is why nothing here caches
    /// one.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_source_get_available_count(CnaHandle game, out uint outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_source_get_type_at(CnaHandle game, uint index, out uint outType);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_source_get_name_size_at(CnaHandle game, uint index, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_media_source_copy_name_at(
        CnaHandle game, uint index, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_get_name_size(CnaHandle song, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_song_copy_name(
        CnaHandle song, byte* destination, ulong capacity, out ulong outBytes);

    /// <summary>The file path or handle a song plays from -- "the string song equality and hashing
    /// are computed from, not the display name" (<c>media.h:319</c>).</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_get_handle_text_size_ext(CnaHandle song, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_song_copy_handle_text_ext(
        CnaHandle song, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_get_is_disposed(CnaHandle song, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_dispose(CnaHandle song);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_equals(CnaHandle left, CnaHandle right, out byte outEqual);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_get_hash_code(CnaHandle song, out int outHash);

    /// <summary>Builds a collection over existing songs. The collection keeps every song it was
    /// given alive, so a caller may release its own handles immediately afterwards
    /// (<c>media.h:525-528</c>).</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_song_collection_create(
        CnaHandle game, CnaHandle* songs, ulong count, out CnaHandle outCollection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_collection_get_at(CnaHandle collection, int index, out CnaHandle outSong);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_collection_get_count(CnaHandle collection, out int outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_collection_get_is_disposed(CnaHandle collection, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_collection_dispose(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_collection_destroy(CnaHandle collection);

    // -- MediaPlayer (media_player.h) ------------------------------------------------------
    //
    // Every route takes a game handle (CnaAmbientGame.Current); no parameterless media route
    // exists anywhere, the same finding audio produced.
    //
    // The whole 41-function surface is bound. A previous pass bound eight of them and recorded
    // that State/Volume/IsMuted/PlayPosition/Queue "stay deliberately NOT native-backed", mirroring
    // the C++ engine's plain static fields, and that the native queue and move_next/_previous/
    // is_repeating/is_shuffled were "genuine new capability ... not something the ABI mismatch
    // forces". A header audit showed the cost of that: the managed side had reimplemented the
    // engine's queue management, shuffle order, state machine and playback timer, so two
    // independent implementations of the same behaviour could disagree, and native's was the one
    // actually driving the audio device.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_play_song(CnaHandle game, CnaHandle song);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_play_songs(CnaHandle game, CnaHandle songs);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_play_songs_from(CnaHandle game, CnaHandle songs, int index);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_move_next(CnaHandle game);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_move_previous(CnaHandle game);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_get_state(CnaHandle game, out uint outState);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_get_volume(CnaHandle game, out float outVolume);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_get_is_muted(CnaHandle game, out byte outMuted);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_get_is_repeating(CnaHandle game, out byte outRepeating);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_set_is_repeating(CnaHandle game, byte repeating);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_get_is_shuffled(CnaHandle game, out byte outShuffled);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_set_is_shuffled(CnaHandle game, byte shuffled);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_get_play_position_ticks(CnaHandle game, out long outTicks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_get_game_has_control(CnaHandle game, out byte outHasControl);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_get_is_visualization_enabled(CnaHandle game, out byte outEnabled);

    /// <summary>The canonical timer and state-transition pump (<c>media_player.h:276</c>).</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_update_ext(CnaHandle game);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_program_exit_ext(CnaHandle game);

    /// <summary>The fallback song-end detector (<c>media_player.h:301</c>). A song whose duration
    /// is unknown never reports ended, rather than reporting it immediately.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_detect_song_ended_by_elapsed_time_ext(
        CnaHandle song, long elapsedTicks, out byte outEnded);

    /// <summary>The canonical event is <b>static</b>, so this subscription belongs to the process
    /// rather than to a game and takes no game handle (<c>media_player.h:317</c>). See
    /// <see cref="nint"/> callback note on the graphics-device-manager subscribe.</summary>
    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_subscribe_active_song_changed_ext(
        nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_subscribe_media_state_changed_ext(
        nint callback, nint context, out CnaHandle outRegistration);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_unsubscribe_ext(CnaHandle registration);

    // ---- MediaQueue (media_player.h:373-487) ----
    //
    // The queue handle is borrowed from the player, which owns the process-wide queue; releasing it
    // with cna_media_queue_destroy releases the handle, not the queue.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_get_queue(CnaHandle game, out CnaHandle outQueue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_queue_get_count(CnaHandle queue, out int outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_queue_get_active_song_index(CnaHandle queue, out int outIndex);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_queue_set_active_song_index(CnaHandle queue, int index);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_queue_get_active_song(
        CnaHandle queue, out CnaHandle outSong, out byte outAvailable);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_queue_get_at(CnaHandle queue, int index, out CnaHandle outSong);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_queue_add(CnaHandle queue, CnaHandle song);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_queue_clear(CnaHandle queue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_queue_destroy(CnaHandle queue);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_pause(CnaHandle game);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_resume(CnaHandle game);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_stop(CnaHandle game);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_set_volume(CnaHandle game, float volume);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_set_is_muted(CnaHandle game, byte muted);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_player_set_is_visualization_enabled(CnaHandle game, byte enabled);

    /// <summary>Matches <c>cna_media_player_get_visualization_data</c> exactly
    /// (<c>media_player.h:168</c>) -- takes one caller-provided <see cref="CnaVisualizationData"/>
    /// struct filled in place, not the old guessed shape's three flat pointer/pointer/count
    /// arguments.</summary>
    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_media_player_get_visualization_data(CnaHandle game, ref CnaVisualizationData data);

    // ---- Media library (media_library.h) ----
    //
    // Generated straight from the header's own declarations rather than hand-typed, then read back
    // against it -- 148 functions is more than hand-transcription stays honest across.
    //
    // The 26 `*_get_type_name_size`/`*_copy_type_name` routes are deliberately not bound. They
    // report a .NET type's fully-qualified name for hosts that have no reflection; C# has
    // `GetType().FullName` natively, so binding them would add a native round trip that answers a
    // question the runtime already answers, and none of it is XNA 4.0 API surface.
    //
    // Handle ownership, from the header: the library handle is OWNED, and every album/artist/genre/
    // playlist/picture/picture-album/collection handle reached through it is a borrowed *view of a
    // library-owned object* whose HANDLE the caller still has to release with its own `_destroy`
    // ("Releases an album handle. The album itself belongs to its media library and is untouched").
    // Holding any of them keeps the library alive, so releasing the library first is safe.

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_create(CnaHandle game, out CnaHandle outLibrary);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_create_from_source(
        CnaHandle game, uint sourceIndex, out CnaHandle outLibrary);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_get_is_disposed(
        CnaHandle library, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_dispose(CnaHandle library);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_destroy(CnaHandle library);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_get_media_source_type(
        CnaHandle library, out uint outType);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_get_media_source_name_size(
        CnaHandle library, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_media_library_copy_media_source_name(
        CnaHandle library, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_get_songs(CnaHandle library, out CnaHandle outSongs);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_get_albums(CnaHandle library, out CnaHandle outAlbums);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_get_artists(
        CnaHandle library, out CnaHandle outArtists);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_get_genres(CnaHandle library, out CnaHandle outGenres);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_get_playlists(
        CnaHandle library, out CnaHandle outPlaylists);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_get_name_size(CnaHandle album, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_album_copy_name(
        CnaHandle album, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_get_is_disposed(CnaHandle album, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_dispose(CnaHandle album);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_destroy(CnaHandle album);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_get_hash_code(CnaHandle album, out int outHash);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_get_songs(CnaHandle album, out CnaHandle outSongs);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_collection_get_count(CnaHandle collection, out int outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_collection_get_at(
        CnaHandle collection, int index, out CnaHandle outAlbum);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_collection_get_is_disposed(
        CnaHandle collection, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_collection_dispose(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_collection_destroy(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_artist_get_name_size(CnaHandle artist, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_artist_copy_name(
        CnaHandle artist, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_artist_get_is_disposed(CnaHandle artist, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_artist_dispose(CnaHandle artist);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_artist_destroy(CnaHandle artist);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_artist_get_hash_code(CnaHandle artist, out int outHash);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_artist_get_songs(CnaHandle artist, out CnaHandle outSongs);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_artist_collection_get_count(CnaHandle collection, out int outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_artist_collection_get_at(
        CnaHandle collection, int index, out CnaHandle outArtist);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_artist_collection_get_is_disposed(
        CnaHandle collection, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_artist_collection_dispose(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_artist_collection_destroy(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_genre_get_name_size(CnaHandle genre, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_genre_copy_name(
        CnaHandle genre, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_genre_get_is_disposed(CnaHandle genre, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_genre_dispose(CnaHandle genre);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_genre_destroy(CnaHandle genre);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_genre_get_hash_code(CnaHandle genre, out int outHash);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_genre_get_songs(CnaHandle genre, out CnaHandle outSongs);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_genre_collection_get_count(CnaHandle collection, out int outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_genre_collection_get_at(
        CnaHandle collection, int index, out CnaHandle outGenre);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_genre_collection_get_is_disposed(
        CnaHandle collection, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_genre_collection_dispose(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_genre_collection_destroy(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_playlist_get_name_size(CnaHandle playlist, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_playlist_copy_name(
        CnaHandle playlist, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_playlist_get_is_disposed(CnaHandle playlist, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_playlist_dispose(CnaHandle playlist);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_playlist_destroy(CnaHandle playlist);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_playlist_get_hash_code(CnaHandle playlist, out int outHash);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_playlist_get_songs(CnaHandle playlist, out CnaHandle outSongs);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_playlist_collection_get_count(CnaHandle collection, out int outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_playlist_collection_get_at(
        CnaHandle collection, int index, out CnaHandle outPlaylist);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_playlist_collection_get_is_disposed(
        CnaHandle collection, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_playlist_collection_dispose(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_playlist_collection_destroy(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_equals(CnaHandle left, CnaHandle right, out byte outEqual);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_artist_equals(CnaHandle left, CnaHandle right, out byte outEqual);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_genre_equals(CnaHandle left, CnaHandle right, out byte outEqual);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_playlist_equals(CnaHandle left, CnaHandle right, out byte outEqual);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_get_artist(
        CnaHandle album, out CnaHandle outArtist, out byte outAvailable);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_get_genre(
        CnaHandle album, out CnaHandle outGenre, out byte outAvailable);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_get_duration(CnaHandle album, out long outTicks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_get_has_art(CnaHandle album, out byte outHasArt);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_get_art_size(CnaHandle album, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_album_copy_art(
        CnaHandle album, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_album_get_thumbnail_size(CnaHandle album, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_album_copy_thumbnail(
        CnaHandle album, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_artist_get_albums(CnaHandle artist, out CnaHandle outAlbums);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_genre_get_albums(CnaHandle genre, out CnaHandle outAlbums);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_playlist_get_duration(CnaHandle playlist, out long outTicks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_get_album(
        CnaHandle song, out CnaHandle outAlbum, out byte outAvailable);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_get_artist(
        CnaHandle song, out CnaHandle outArtist, out byte outAvailable);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_song_get_genre(
        CnaHandle song, out CnaHandle outGenre, out byte outAvailable);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_get_name_size(CnaHandle picture, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_picture_copy_name(
        CnaHandle picture, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_get_token_size_ext(CnaHandle picture, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_picture_copy_token_ext(
        CnaHandle picture, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_get_album(
        CnaHandle picture, out CnaHandle outAlbum, out byte outAvailable);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_get_date_unix_ticks(CnaHandle picture, out long outUnixTicks);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_get_width(CnaHandle picture, out int outWidth);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_get_height(CnaHandle picture, out int outHeight);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_get_image_size(CnaHandle picture, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_picture_copy_image(
        CnaHandle picture, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_get_thumbnail_size(CnaHandle picture, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_picture_copy_thumbnail(
        CnaHandle picture, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_get_is_disposed(CnaHandle picture, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_dispose(CnaHandle picture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_destroy(CnaHandle picture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_equals(CnaHandle left, CnaHandle right, out byte outEqual);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_get_hash_code(CnaHandle picture, out int outHash);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_get_name_size(CnaHandle album, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_picture_album_copy_name(
        CnaHandle album, byte* destination, ulong capacity, out ulong outBytes);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_get_parent(
        CnaHandle album, out CnaHandle outParent, out byte outAvailable);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_get_albums(CnaHandle album, out CnaHandle outAlbums);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_get_pictures(
        CnaHandle album, out CnaHandle outPictures);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_get_is_disposed(CnaHandle album, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_dispose(CnaHandle album);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_destroy(CnaHandle album);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_equals(
        CnaHandle left, CnaHandle right, out byte outEqual);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_get_hash_code(CnaHandle album, out int outHash);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_get_pictures(
        CnaHandle library, out CnaHandle outPictures);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_get_saved_pictures(
        CnaHandle library, out CnaHandle outPictures);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_get_root_picture_album(
        CnaHandle library, out CnaHandle outAlbum, out byte outAvailable);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_get_picture_from_token(
        CnaHandle library, CnaStringView token, out CnaHandle outPicture, out byte outAvailable);

    [LibraryImport(LibraryName)]
    internal static unsafe partial CnaResult cna_media_library_save_picture(
        CnaHandle library, CnaStringView name, byte* imageData, ulong imageByteCount, out CnaHandle outPicture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_media_library_save_picture_from_stream(
        CnaHandle library, CnaStringView name, CnaHandle source, out CnaHandle outPicture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_collection_get_count(CnaHandle collection, out int outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_collection_get_at(
        CnaHandle collection, int index, out CnaHandle outPicture);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_collection_get_is_disposed(
        CnaHandle collection, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_collection_dispose(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_collection_destroy(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_collection_get_count(
        CnaHandle collection, out int outCount);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_collection_get_at(
        CnaHandle collection, int index, out CnaHandle outAlbum);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_collection_get_is_disposed(
        CnaHandle collection, out byte outDisposed);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_collection_dispose(CnaHandle collection);

    [LibraryImport(LibraryName)]
    internal static partial CnaResult cna_picture_album_collection_destroy(CnaHandle collection);
}
