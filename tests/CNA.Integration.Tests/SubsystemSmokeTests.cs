using CNA.Audio;
using CNA.Graphics;
using CNA.Input;
using CNA.Media;
using Xunit;
using Xunit.Abstractions;

namespace CNA.Integration.Tests;

/// <summary>
/// Breadth rather than depth: one real call into each major subsystem, to find the ones that do not
/// work at all.
///
/// The motivating number is that of 223 public compat types, runtime coverage had touched 43. The
/// rest was verified statically -- declarations present in headers, arities matching, structs
/// versioned -- which catches a fabricated import and cannot catch a wrong struct layout, a stale
/// handle, or a route that simply fails on every renderer. Those only show up when something calls
/// them.
///
/// Deliberately shallow. A subsystem that answers plausibly here is not proven correct; it is
/// proven reachable. That is still the difference between "compiles" and "runs".
/// </summary>
[Collection(NativeGameCollection.Name)]
public class SubsystemSmokeTests(ITestOutputHelper output, NativeGameFixture fixture)
{


    /// <summary>Input state readers. All three are pure reads of a device snapshot, so they work
    /// headless -- the values are meaningless without a device, but a crash or a garbled struct is
    /// not.</summary>
    /// <summary>
    /// The capability gate agrees with the device for every capability, and says which are absent.
    ///
    /// Twenty tests decide whether to run by asking this, so a gate that answered wrongly would
    /// either skip work that could have run or run work the renderer cannot do -- and the second
    /// reads as a broken binding. Every capability is checked rather than a sample, because the gate
    /// is a straight forwarding call and the way a forwarding call breaks is one enum value, not all
    /// of them.
    ///
    /// The list of absent capabilities is printed. On a renderer that lacks one, the tests gated on
    /// it pass without exercising anything, and this is the line that says so.
    /// </summary>
    [NativeFact]
    public void CapabilityGate_AgreesWithTheDeviceForEveryCapability()
    {
        fixture.InsideAFrameWithDevice(device =>
        {
            var absent = new List<string>();

            foreach (GraphicsCapability capability in Enum.GetValues<GraphicsCapability>())
            {
                bool supported = device.SupportsCapability(capability);

                Assert.Equal(supported, CnaNativeProbe.HasCapability(device, capability));

                if (!supported)
                {
                    absent.Add(capability.ToString());
                }
            }

            output.WriteLine(
                $"renderer '{device.RendererName}' lacks {absent.Count} capability(ies)" +
                (absent.Count == 0 ? "." : ": " + string.Join(", ", absent)));
        });
    }

    [NativeFact]
    public void Input_AllThreeDevices_ReportState()
    {
        fixture.InsideAFrameWithDevice(_ =>
        {
            KeyboardState keyboard = Keyboard.GetState();
            Assert.NotNull(keyboard.GetPressedKeys());

            MouseState mouse = Mouse.GetState();
            output.WriteLine($"mouse ({mouse.X},{mouse.Y}) left={mouse.LeftButton}");

            GamePadState pad = GamePad.GetState(PlayerIndex.One);
            output.WriteLine($"gamepad connected={pad.IsConnected}");

            GamePadCapabilities caps = GamePad.GetCapabilities(PlayerIndex.One);
            output.WriteLine($"gamepad type={caps.GamePadType}");
        });
    }

    /// <summary>A render target is a separate native resource type upstream, not a texture with
    /// flags, so it exercises a different create/info/destroy trio from Texture2D.</summary>
    [NativeFact]
    public void RenderTarget2D_CreatesAndReportsItsProperties()
    {
        fixture.InsideAFrame(game =>
        {
            using var target = new RenderTarget2D(game.GraphicsDevice, 64, 32);

            output.WriteLine(
                $"{target.Width}x{target.Height} {target.Format} depth={target.DepthStencilFormat} " +
                $"usage={target.RenderTargetUsage} msaa={target.MultiSampleCount} lost={target.IsContentLost}");

            Assert.Equal(64, target.Width);
            Assert.Equal(32, target.Height);
        });
    }

    /// <summary>
    /// The mouse cursor surface, which XNA has no API for and a MonoGame-derived game calls anyway.
    ///
    /// Compiling an 18,391-line Windows Phone XNA game ported to MonoGame against the strict facade
    /// leaves exactly one unresolved call: <c>Mouse.SetCursor(MouseCursor.Arrow)</c>. It cannot go
    /// on the facade's <c>Mouse</c> -- an extra member fails the metadata gate -- so it lives in the
    /// CNA extensions, and this is the runtime half of that answer.
    ///
    /// A stock cursor is the system's: disposing it must leave the system's alone, which is why the
    /// same identity is fetched twice and both are disposed.
    ///
    /// Whether the platform can set a system cursor is the environment's answer, not this binding's:
    /// the <c>offscreen</c> SDL video driver refuses with "CreateSystemCursor is not currently
    /// supported", and an X11 one does it. Both outcomes are asserted rather than one being skipped,
    /// because dynamic skip does not survive this runner -- <c>xunit.runner.visualstudio</c> 2.8.2
    /// reports a <c>SkipException</c> as a failure, and bumping it to the v3 runner broke an
    /// unrelated test. So the refusal is checked for what it is: a platform error, not an argument
    /// or state error, and never a silent success.
    /// </summary>
    [NativeFact]
    public void MouseCursor_SetsAStockCursorAndLeavesTheSystemsAlone()
    {
        fixture.InsideAFrame(_ =>
        {
            Assert.Throws<ArgumentNullException>(() => Mouse.SetCursor(null!));

            // Creating the cursor object succeeds even where the driver has no system cursors; it is
            // setting one that the offscreen driver refuses. So the probe is the set, not the
            // create.
            bool canSet;
            using (MouseCursor probe = MouseCursor.FromStock(MouseCursorStock.Arrow))
            {
                try
                {
                    Mouse.SetCursor(probe);
                    canSet = true;
                }
                catch (CnaException exception)
                {
                    output.WriteLine($"this driver cannot set a system cursor: {exception.Message}");
                    Assert.Contains("Platform", exception.Message, StringComparison.Ordinal);
                    canSet = false;
                }
            }

            if (!canSet)
            {
                return;
            }

            // The probe was disposed; the system's arrow must still be usable, because a stock
            // cursor is the system's and disposing one names nothing to destroy.
            using MouseCursor again = MouseCursor.FromStock(MouseCursorStock.Arrow);
            Mouse.SetCursor(again);

            using MouseCursor hand = MouseCursor.FromStock(MouseCursorStock.Hand);
            Mouse.SetCursor(hand);

            output.WriteLine("stock cursors set and released");
        });
    }

    /// <summary>
    /// The graphics capabilities that had been unreachable from managed code since CNA 0.8 added
    /// them. CNA surface with no XNA counterpart, so this asserts that the queries answer rather
    /// than what they answer: asserting a value would be asserting a renderer.
    /// </summary>
    [NativeFact]
    public void ExtendedCapabilities_AnswerOnEveryRenderer()
    {
        fixture.InsideAFrameWithDevice(device =>
        {
            output.WriteLine(
                $"floatRT={device.SupportsCapability(GraphicsCapability.FloatRenderTargets)} " +
                $"halfFloatRT={device.SupportsCapability(GraphicsCapability.HalfFloatRenderTargets)} " +
                $"halfFloatFilter={device.SupportsCapability(GraphicsCapability.HalfFloatTextureLinearFiltering)}");
        });
    }

    /// <summary>
    /// <c>ContentLost</c> is a real native subscription since CNA 0.19.0, not an inert
    /// <c>add</c>/<c>remove</c> pair.
    ///
    /// What this can prove on any renderer is that the subscription is taken, that a second
    /// handler reuses it rather than registering twice, and that disposal releases it before the
    /// render-target handle it is registered against -- the ordering that would otherwise leave
    /// native able to call into a dead context. What it deliberately does not assert is that the
    /// event fires: <c>render_target.h</c> says only a renderer family that can genuinely lose a
    /// device reports one, and that a caller-initiated <c>Reset</c> is not loss, so on this
    /// renderer a silent subscription is the correct outcome and a test that demanded a callback
    /// would be demanding a fabrication.
    ///
    /// <see cref="RenderTarget2D_ContentLostFiresWhenNativeIsToldContentIsGone"/> now covers the
    /// firing half, through the explicit notify route rather than by waiting for a loss that this
    /// renderer will never have.
    /// </summary>
    [NativeFact]
    public void RenderTarget2D_ContentLostSubscriptionIsTakenAndReleased()
    {
        fixture.InsideAFrame(game =>
        {
            int raised = 0;
            var target = new RenderTarget2D(game.GraphicsDevice, 16, 16);
            try
            {
                void First(object? sender, EventArgs args) => raised++;
                void Second(object? sender, EventArgs args) => raised++;

                target.ContentLost += First;
                target.ContentLost += Second;
                target.ContentLost -= Second;

                output.WriteLine($"subscribed; renderer reports lost={target.IsContentLost}");
                Assert.Equal(0, raised);
            }
            finally
            {
                target.Dispose();
            }

            // Disposal must have released the registration before the handle, so a second dispose
            // is silent rather than a double release.
            target.Dispose();

            Assert.Throws<ObjectDisposedException>(() => target.ContentLost += (_, _) => { });
        });
    }

    /// <summary>
    /// The other half: a subscription that actually delivers.
    ///
    /// The test above can only show the registration is taken and released, because a renderer that
    /// cannot lose a device never raises the event -- so the handler side had never run, and a
    /// handler that has never run is a handler nobody knows is connected. Between the managed
    /// subscription and a game's callback sit an event bridge, a sender projection and a native
    /// registration, and any of the three could be wrong in a way the registration test cannot see.
    ///
    /// <c>cna_graphics_device_notify_content_lost_resources_ext</c> exists for exactly this and is
    /// bound as a test hook only. It is not a fabrication in the way a synthetic callback would be:
    /// the notification travels the real native path to the real subscription.
    ///
    /// The removed handler is the point of the second counter. An event bridge that fired every
    /// handler it had ever seen would pass a test that only counted the surviving one.
    /// </summary>
    [NativeFact]
    public void RenderTarget2D_ContentLostFiresWhenNativeIsToldContentIsGone()
    {
        fixture.InsideAFrame(game =>
        {
            using var target = new RenderTarget2D(game.GraphicsDevice, 16, 16);

            int kept = 0;
            int removed = 0;
            object? senderSeen = null;

            void Kept(object? sender, EventArgs args)
            {
                kept++;
                senderSeen = sender;
            }

            void Removed(object? sender, EventArgs args) => removed++;

            target.ContentLost += Kept;
            target.ContentLost += Removed;
            target.ContentLost -= Removed;

            game.GraphicsDevice.NotifyContentLostResourcesForTesting();

            output.WriteLine($"after notify: kept={kept} removed={removed} sender={senderSeen?.GetType().Name}");

            Assert.Equal(1, kept);
            Assert.Equal(0, removed);
            Assert.Same(target, senderSeen);
        });
    }

    /// <summary>
    /// The same, for the buffer families -- and it settles a blocker row.
    ///
    /// <c>docs/native-behavior-blockers.md</c> carried "Both headers state that CNA never raises
    /// ContentLost for a buffer. Render targets got <c>cna_render_target_subscribe_content_lost</c>
    /// in 0.19.0; the buffer families did not." Both clauses are false against the live headers:
    /// <c>cna_vertex_buffer_subscribe_content_lost</c> and its index-buffer twin have existed since
    /// 2026-08-15, this binding has consumed them since, and <c>DynamicVertexBuffer.cs</c>'s own doc
    /// comment says so. The row had been re-measured only for whether its named routes still exist,
    /// which is the one thing about it that was never in question.
    ///
    /// What was genuinely unproven is what this test now measures: that the subscription is
    /// *delivered to*. <c>NotifyContentLostResourcesEXT</c> walks every <c>IContentLosable</c> on
    /// the device, and <c>DynamicVertexBuffer</c>/<c>DynamicIndexBuffer</c> are two of the four
    /// implementers, so the same real native path that reaches a render target reaches these.
    ///
    /// A non-dynamic buffer is created alongside and asserted *not* to be notified. That is not
    /// symmetry for its own sake: only the dynamic types implement the interface, so a
    /// notification reaching a static buffer would mean the managed side had invented one.
    /// </summary>
    [Native3DFact]
    public void DynamicBuffers_ContentLostFiresWhenNativeIsToldContentIsGone()
    {
        fixture.InsideAFrame(game =>
        {
            GraphicsDevice device = game.GraphicsDevice;
            // The guess this used to refuse to make is now a measurement. A SDL_RENDERER build
            // reports ThreeD false and refuses a dynamic buffer with NotSupported and a specific
            // message -- it never reaches HandleUnsupported3DCall's bare std::runtime_error,
            // because the C API checks the capability before the renderer is asked.
            if (!CnaNativeProbe.HasCapabilityOrRefuses(
                    device,
                    GraphicsCapability.ThreeD,
                    "creating a DynamicVertexBuffer",
                    () => new DynamicVertexBuffer(
                        device, VertexPositionColor.VertexDeclaration, 4, BufferUsage.None).Dispose(),
                    output))
            {
                return;
            }

            using var vertices = new DynamicVertexBuffer(
                device, VertexPositionColor.VertexDeclaration, 4, BufferUsage.None);
            using var indices = new DynamicIndexBuffer(device, IndexElementSize.SixteenBits, 6, BufferUsage.None);

            int vertexKept = 0;
            int vertexRemoved = 0;
            int indexKept = 0;
            object? vertexSender = null;
            object? indexSender = null;

            void VertexKept(object? sender, EventArgs args)
            {
                vertexKept++;
                vertexSender = sender;
            }

            void VertexRemoved(object? sender, EventArgs args) => vertexRemoved++;

            void IndexKept(object? sender, EventArgs args)
            {
                indexKept++;
                indexSender = sender;
            }

            vertices.ContentLost += VertexKept;
            vertices.ContentLost += VertexRemoved;
            vertices.ContentLost -= VertexRemoved;
            indices.ContentLost += IndexKept;

            device.NotifyContentLostResourcesForTesting();

            output.WriteLine(
                $"after notify: vertexKept={vertexKept} vertexRemoved={vertexRemoved} indexKept={indexKept}");

            Assert.Equal(1, vertexKept);
            Assert.Equal(0, vertexRemoved);
            Assert.Equal(1, indexKept);
            Assert.Same(vertices, vertexSender);
            Assert.Same(indices, indexSender);
        });
    }

    /// <summary>Binding a render target and unbinding it. The cached-binding bookkeeping behind
    /// GetRenderTargets is easy to get wrong in a way only a real bind exposes.</summary>
    [NativeFact]
    public void GraphicsDevice_SetAndClearRenderTarget()
    {
        fixture.InsideAFrame(game =>
        {
            GraphicsDevice device = game.GraphicsDevice;
            using var target = new RenderTarget2D(device, 32, 32);

            device.SetRenderTarget(target);
            RenderTargetBinding[] bound = device.GetRenderTargets();
            Assert.Single(bound);

            device.SetRenderTarget(null);
            Assert.Empty(device.GetRenderTargets());
        });
    }

    /// <summary>Graphics state objects, which cross the ABI as versioned caller-initialised
    /// structs -- the exact shape where an `out` instead of a `ref` corrupts silently.</summary>
    [NativeFact]
    public void GraphicsDevice_StateObjects_RoundTrip()
    {
        fixture.InsideAFrame(game =>
        {
            GraphicsDevice device = game.GraphicsDevice;

            device.BlendState = BlendState.AlphaBlend;
            device.DepthStencilState = DepthStencilState.Default;
            device.RasterizerState = RasterizerState.CullCounterClockwise;

            output.WriteLine(
                $"blend={device.BlendState.ColorSourceBlend} depth={device.DepthStencilState.DepthBufferEnable} " +
                $"cull={device.RasterizerState.CullMode}");

            Assert.NotNull(device.BlendState);
            Assert.NotNull(device.DepthStencilState);
            Assert.NotNull(device.RasterizerState);
        });
    }

    /// <summary>SoundEffect from raw PCM. Audio is an entire subsystem nothing had executed.</summary>
    [NativeFact]
    public void SoundEffect_CreatesFromPcm_AndReportsDuration()
    {
        fixture.InsideAFrameWithDevice(_ =>
        {
            // A tenth of a second of silence, 16-bit mono at 44.1 kHz.
            var pcm = new byte[44100 / 10 * 2];

            using var effect = new SoundEffect(pcm, 44100, AudioChannels.Mono);

            output.WriteLine($"duration {effect.Duration}");
            Assert.True(effect.Duration > TimeSpan.Zero, "A non-empty buffer reported zero duration.");
        });
    }

    /// <summary>The static audio listener/emitter values plus the global mixer knobs.</summary>
    [NativeFact]
    public void SoundEffect_GlobalMixerSettings_RoundTrip()
    {
        fixture.InsideAFrameWithDevice(_ =>
        {
            SoundEffect.MasterVolume = 0.5f;
            Assert.Equal(0.5f, SoundEffect.MasterVolume, 1e-4f);

            SoundEffect.MasterVolume = 1f;
            Assert.Equal(1f, SoundEffect.MasterVolume, 1e-4f);
        });
    }

    /// <summary>MediaPlayer's global state. Rebound on all 41 media_player.h functions and never
    /// executed until now.</summary>
    [NativeFact]
    public void MediaPlayer_ReportsItsGlobalState()
    {
        fixture.InsideAFrameWithDevice(_ =>
        {
            output.WriteLine($"state={MediaPlayer.State} volume={MediaPlayer.Volume} muted={MediaPlayer.IsMuted}");

            MediaPlayer.Volume = 0.25f;
            Assert.Equal(0.25f, MediaPlayer.Volume, 1e-4f);

            MediaPlayer.IsMuted = true;
            Assert.True(MediaPlayer.IsMuted);
            MediaPlayer.IsMuted = false;
        });
    }


    /// <summary>GameWindow, whose handle route is an `_ext` and whose title crosses the ABI as a
    /// string in both directions.</summary>
    [NativeFact]
    public void GameWindow_TitleRoundTrips()
    {
        fixture.InsideAFrame(game =>
        {
            game.Window.Title = "cna-dotnet integration";
            Assert.Equal("cna-dotnet integration", game.Window.Title);

            output.WriteLine($"client bounds {game.Window.ClientBounds}, handle 0x{game.Window.Handle:x}");
        });
    }

    /// <summary>The border a Windows Forms game removes with FormBorderStyle.None (CSX-114), through
    /// CNA's borderless window route.</summary>
    [NativeFact]
    public void GameWindow_BorderlessRoundTrips()
    {
        fixture.InsideAFrame(game =>
        {
            bool original = game.Window.IsBorderlessEXT;
            try
            {
                game.Window.IsBorderlessEXT = true;
                Assert.True(game.Window.IsBorderlessEXT);
                game.Window.IsBorderlessEXT = false;
                Assert.False(game.Window.IsBorderlessEXT);
            }
            finally
            {
                game.Window.IsBorderlessEXT = original;
            }
        });
    }
}
