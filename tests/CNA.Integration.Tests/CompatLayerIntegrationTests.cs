using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using CNA.XnaCompat.Extensions;
using Xunit;
using Xunit.Abstractions;
using XnaAudio = Microsoft.Xna.Framework.Audio;
using XnaGame = Microsoft.Xna.Framework.Game;

// NOT under CNA. That is load-bearing, and it is the second thing this file established.
//
// A namespace nested under CNA puts CNA's own root types in scope, and an enclosing namespace's
// members shadow a using directive's imports in C#. So inside `namespace CNA.Integration.Tests`,
// `GameTime` resolves to CNA.GameTime even with `using Microsoft.Xna.Framework;` at the top, and
// overriding the compat Game's Update fails with "cannot override inherited member because it is
// sealed" -- an error naming neither the namespace nor the shadowing.
//
// cna-dotnet-template hit the same thing and works around it in its csproj (`<RootNamespace>
// CnaDotnetTemplate</RootNamespace>`, with a comment). It is a real constraint on consumers: a ported
// XNA game cannot live under a CNA namespace. Recorded in docs rather than only in a csproj comment
// now, because the symptom points nowhere near the cause.
namespace CnaDotnet.Integration.Tests.Compat;

/// <summary>
/// The compat layer, against the real library. <b>Nothing here had ever run.</b>
///
/// Every integration test written before this one used <c>CNA.*</c> types. A ported XNA game uses
/// <c>Microsoft.Xna.Framework.*</c>, which is 238 re-typing members -- <c>new</c> properties that
/// convert, overrides that cast, value types that duplicate with implicit conversions -- and each
/// is a place a conversion can be wrong in a way the CNA layer running correctly says nothing
/// about.
///
/// It went unnoticed because the coverage measurement could not see it: both layers name their
/// types identically, so <c>Texture2D</c> counted as covered while only
/// <c>CNA.Graphics.Texture2D</c> had ever executed. A metric matching bare identifiers cannot
/// distinguish two namespaces, and this one did not.
///
/// In the own-game collection: a compat game is a different <c>Game</c> subclass, so it builds its
/// own rather than borrowing the shared fixture's.
/// </summary>
[Collection(global::CNA.Integration.Tests.OwnGameCollection.Name)]
public class CompatLayerIntegrationTests(ITestOutputHelper output)
{
    private struct ManagedReferenceVertex
    {
        public ManagedReferenceVertex(Vector3 position, string label)
        {
            Position = position;
            Label = label;
        }

        public Vector3 Position;
        public string? Label;
    }

    /// <summary>Runs one frame of a real compat game and surfaces what the body threw.</summary>
    private sealed class CompatProbe(Action<CompatProbe> body) : XnaGame
    {
        public bool Ran { get; private set; }

        public Exception? Failure { get; private set; }

        protected override void Update(GameTime gameTime)
        {
            if (!Ran)
            {
                Ran = true;
                try
                {
                    body(this);
                }
                catch (Exception ex)
                {
                    Failure = ex;
                }
            }

            Exit();
            base.Update(gameTime);
        }
    }

    /// <summary>
    /// Exercises the complete Game/device/window facade construction order. In particular the
    /// manager is created while the outer XNA game is still in its constructor, whereas its CNA
    /// backend is private; this is the path that used to depend on public inheritance.
    /// </summary>
    private sealed class FacadeGroupProbe : XnaGame
    {
        public FacadeGroupProbe()
        {
            Manager = new GraphicsDeviceManager(this);
        }

        public GraphicsDeviceManager Manager { get; }

        public bool Updated { get; private set; }

        protected override void Update(GameTime gameTime)
        {
            Updated = true;
            Exit();
            base.Update(gameTime);
        }
    }

    private static void InsideACompatFrame(Action<CompatProbe> body)
    {
        using var game = new CompatProbe(body);

        for (int i = 0; i < 4 && !game.Ran; i++)
        {
            game.RunFrameEXT();
        }

        if (game.Failure is { } failure)
        {
            // See NativeGameFixture.InsideAFrame: a skip decided inside the body is a skip.
            if (failure is Xunit.Sdk.SkipException)
            {
                throw failure;
            }

            throw new Xunit.Sdk.XunitException($"The body threw inside the compat frame: {failure}");
        }

        Assert.True(game.Ran, "The frame never ran, so nothing was exercised.");
    }

    /// <summary>
    /// The covariant-return factory hooks: a compat game's Content, GraphicsDevice and Window must
    /// be the compat types, not the CNA ones they derive from.
    ///
    /// This is the load-bearing part of the whole layer. If any of those three hands back a base
    /// type, ported source stops compiling -- or worse, compiles and casts at run time.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGame_ExposesCompatTypedMembers()
    {
        InsideACompatFrame(game =>
        {
            Assert.IsAssignableFrom<Microsoft.Xna.Framework.Content.ContentManager>(game.Content);
            Assert.IsAssignableFrom<Microsoft.Xna.Framework.Graphics.GraphicsDevice>(game.GraphicsDevice);
            Assert.IsAssignableFrom<Microsoft.Xna.Framework.GameWindow>(game.Window);

            output.WriteLine(
                $"content={game.Content.GetType().FullName}, device={game.GraphicsDevice.GetType().FullName}");
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGameFacade_DeviceManagerAndWindowRemainFacadeTypedAcrossAFrame()
    {
        using var game = new FacadeGroupProbe();

        game.RunFrameEXT();

        Assert.True(game.Updated);
        Assert.Same(
            game.Manager,
            game.Services.GetService(typeof(IGraphicsDeviceService)));
        Assert.Same(
            game.Manager,
            game.Services.GetService(typeof(IGraphicsDeviceManager)));
        Assert.IsType<Microsoft.Xna.Framework.Graphics.GraphicsDevice>(game.Manager.GraphicsDevice);
        Assert.IsAssignableFrom<Microsoft.Xna.Framework.GameWindow>(game.Window);
    }

    /// <summary>
    /// How the back buffer is fitted into the window -- a choice XNA does not offer.
    ///
    /// XNA 4.0 stretches the back buffer to the client area and gives a game no say, which is why
    /// a fixed-aspect XNA game draws its own letterbox bars. CNA does it in the presentation step,
    /// so this is one of the places where a ported game can delete code rather than port it.
    ///
    /// Every identity is round-tripped rather than just one: the enum is a straight numeric cast
    /// across the ABI, and the failure mode of a wrong cast is an off-by-one that a single-value
    /// test passes and every other value fails.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void GraphicsDeviceManager_PreferredPresentationMode_RoundTripsEveryIdentity()
    {
        using var game = new FacadeGroupProbe();

        game.RunFrameEXT();

        CnaPresentationMode initial = game.Manager.GetCnaPreferredPresentationMode();
        output.WriteLine($"initial mode: {initial}");

        foreach (CnaPresentationMode mode in Enum.GetValues<CnaPresentationMode>())
        {
            game.Manager.SetCnaPreferredPresentationMode(mode);
            Assert.Equal(mode, game.Manager.GetCnaPreferredPresentationMode());
        }

        game.Manager.SetCnaPreferredPresentationMode(initial);
        Assert.Equal(initial, game.Manager.GetCnaPreferredPresentationMode());
    }

    /// <summary>A texture created and uploaded through the compat types, with the compat
    /// <see cref="Color"/> -- a duplicated value type, not the CNA one.</summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatTexture2D_SetDataWithCompatColor()
    {
        InsideACompatFrame(game =>
        {
            using var texture = new Texture2D(game.GraphicsDevice, 2, 2);

            texture.SetData(
            [
                Color.Red, Color.Green,
                Color.Blue, Color.White,
            ]);

            Assert.Equal(2, texture.Width);
            Assert.Equal(new Rectangle(0, 0, 2, 2), texture.Bounds);

            var read = new Color[4];
            texture.GetData(read);
            output.WriteLine($"read back {string.Join(", ", read)}");
            Assert.Equal(Color.Red, read[0]);
        });
    }

    /// <summary>
    /// The strict facade's cache identity, and the claim that the two layers agree about it.
    ///
    /// The compat manager has had a cache since it was written and never had a test for it, which is
    /// how <c>CNA.Content.ContentManager</c> came to diverge without anything noticing. The rows
    /// here are the same rows <c>ContentManagerCacheTests</c> asserts on the CNA-native manager --
    /// same instance, same atlas, case- and separator-insensitive key -- so "both layers agree" is
    /// checked rather than asserted in a comment.
    ///
    /// XNA is the authority for this layer, and this is XNA's behaviour: <c>loadedAssets</c> keyed
    /// by the asset name with backslashes normalised, compared case-insensitively.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatContent_CachesByCanonicalAssetNameLikeTheCnaNativeManager()
    {
        InsideACompatFrame(game =>
        {
            // The game's own manager, not a hand-built one: a ContentManager resolves its device
            // through IGraphicsDeviceService, which GraphicsDeviceManager registers, so a manager
            // constructed straight from Services has a provider with nothing in it. Each compat
            // frame builds its own game, so setting the root here affects no other test.
            Microsoft.Xna.Framework.Content.ContentManager content = game.Content;
            content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "xnb");

            SpriteFont canonical = content.Load<SpriteFont>("lzx/FontCalibri14");
            SpriteFont again = content.Load<SpriteFont>("lzx/FontCalibri14");
            SpriteFont backslashed = content.Load<SpriteFont>("lzx\\FontCalibri14");
            SpriteFont lowercased = content.Load<SpriteFont>("lzx/fontcalibri14");

            Assert.Same(canonical, again);
            Assert.Same(canonical, backslashed);
            Assert.Same(canonical, lowercased);

            // No atlas assertion here, and that is the facade being right rather than a gap: XNA's
            // SpriteFont.Texture is internal, so the strict contract exposes none. Same instance is
            // the whole observable claim on this layer.

            // And the same non-collapsing rule, so the two layers do not disagree about what is
            // *not* one asset either.
            Assert.NotSame(canonical, content.Load<SpriteFont>("./lzx/FontCalibri14"));

            output.WriteLine("compat and CNA-native managers agree on content identity");
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void CompatContent_LoadModelAcceptsXnbRawByteBufferPayloads()
    {
        InsideACompatFrame(game =>
        {
            game.Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "xnb");

            // Same measured branch as NestedModelContentTests: a model is buffers, and a renderer
            // with no 3D pipeline refuses them. The refusal is asserted so the absent side carries a
            // claim rather than being skipped.
            if (!global::CNA.XnaCompat.Extensions.CnaGraphicsDeviceExtensions.SupportsCnaCapability(
                    game.GraphicsDevice,
                    global::CNA.XnaCompat.Extensions.CnaGraphicsCapability.ThreeD))
            {
                var refusal = Assert.Throws<global::CNA.CnaException>(
                    () => game.Content.Load<Model>("BlenderDefaultCube"));
                Assert.Equal("NotSupported", refusal.NativeResult);
                output.WriteLine(
                    $"ABSENT BRANCH EXERCISED: a 2D-only renderer refuses the model's buffers -- {refusal.Message}");
                return;
            }

            Model model = game.Content.Load<Model>("BlenderDefaultCube");
            ModelMeshPart part = model.Meshes[0].MeshParts[0];

            Assert.NotNull(part.VertexBuffer);
            Assert.NotNull(part.IndexBuffer);
            Assert.NotNull(part.Effect);
            Assert.True(part.NumVertices > 0);
            Assert.True(part.PrimitiveCount > 0);

            // Model XNB readers supply byte[] blobs to generic SetData. This used to interpret
            // byte count as vertex/index count and fail in native before a model could load.
            game.Content.Unload();
            Assert.True(part.VertexBuffer.IsDisposed);
            Assert.True(part.IndexBuffer.IsDisposed);
            Assert.True(part.Effect.IsDisposed);
        });
    }

    /// <summary>
    /// cna-cs-samples CNA-REPORT-002, which was this binding and not CNA: <c>ModelMesh.Draw</c>
    /// disposed the technique, pass collection and passes it read, but the effect caches those
    /// wrappers and hands the same ones back, so the second draw used a released technique handle --
    /// "frame 1 OK, frame 2 The EffectTechnique handle is invalid" in every sample that draws a Model.
    /// </summary>
    [global::CNA.Integration.Tests.Native3DFact]
    public void CompatModel_DrawsRepeatedlyWithTheEffectsOwnTechnique()
    {
        InsideACompatFrame(game =>
        {
            game.Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "xnb");
            Model model = game.Content.Load<Model>("BlenderDefaultCube");
            Effect effect = model.Meshes[0].MeshParts[0].Effect!;
            EffectTechnique technique = effect.CurrentTechnique;

            for (int frame = 0; frame < 3; frame++)
            {
                model.Meshes[0].Draw();
                model.Draw(Matrix.Identity, Matrix.Identity, Matrix.Identity);
            }

            Assert.Same(technique, effect.CurrentTechnique);
            Assert.NotNull(effect.CurrentTechnique.Passes[0]);
            game.Content.Unload();
        });
    }

    /// <summary>Exercises the raw-byte Texture3D ABI route through XNA's generic overload. A
    /// uint is intentionally used for a four-byte Color texel so this cannot fall back to the
    /// managed Color conversion path.</summary>
    [global::CNA.Integration.Tests.NativeFactRequiring(global::CNA.Graphics.GraphicsCapability.Texture3D)]
    public void CompatTexture3D_SetDataAcceptsBlittableNonColorElements()
    {
        InsideACompatFrame(game =>
        {
            if (!global::CNA.XnaCompat.Extensions.CnaGraphicsDeviceExtensions.SupportsCnaCapability(
                    game.GraphicsDevice,
                    global::CNA.XnaCompat.Extensions.CnaGraphicsCapability.Texture3D))
            {
                // The absent branch, asserted rather than skipped. Measured on both a HEADLESS and
                // a SDL_RENDERER build: constructing a Texture3D answers NotSupported with a
                // specific message. A renderer that instead handed back a volume texture with no
                // storage behind it would satisfy this whole file and discard every SetData.
                var refusal = Assert.Throws<global::CNA.CnaException>(
                    () => new Texture3D(game.GraphicsDevice, 1, 1, 1, false, SurfaceFormat.Color).Dispose());
                Assert.Equal("NotSupported", refusal.NativeResult);
                output.WriteLine(
                    $"ABSENT BRANCH EXERCISED: Texture3D refused with NotSupported -- {refusal.Message}");
                return;
            }

            using var texture = new Texture3D(game.GraphicsDevice, 1, 1, 1, false, SurfaceFormat.Color);
            texture.SetData<uint>([0xFFFFFFFFu]);

            Assert.Equal(1, texture.Depth);
        });
    }

    /// <summary>
    /// The listener-array overload must be one native request, and it must succeed.
    ///
    /// CNA carried the whole array across the ABI from 0.6.0, but the implementation behind it
    /// refused every count other than one until CABI-6, so this test used to assert
    /// <c>NotSupportedException</c>. The only admitted runtime now accepts any count of one or more
    /// and applies the dominant listener, which is what XNA does; asserting the refusal would now
    /// be asserting a limitation that no admitted library has. What the managed layer must still
    /// never do is loop over the array applying one listener at a time, which would silently retain
    /// only the last -- that is why this goes through the atomic route and then checks the instance
    /// is still usable through the canonical one.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatSoundEffectInstance_AppliesAMultipleListenerArrayAtomically()
    {
        InsideACompatFrame(_ =>
        {
            var pcm = new byte[44100 / 20 * 2];
            using var effect = new XnaAudio.SoundEffect(pcm, 44100, XnaAudio.AudioChannels.Mono);
            using XnaAudio.SoundEffectInstance instance = effect.CreateInstance();

            XnaAudio.AudioListener[] listeners = [new XnaAudio.AudioListener(), new XnaAudio.AudioListener()];
            listeners[1].Position = new Vector3(10f, 0f, 0f);

            instance.Apply3D(listeners, new XnaAudio.AudioEmitter());

            // Applying again with a single listener must remain legal afterwards: the array route
            // must not have left the instance in a state the canonical route cannot use.
            instance.Apply3D(listeners[0], new XnaAudio.AudioEmitter());
        });
    }

    /// <summary>A full compat SpriteBatch pass with the compat Vector2 and Color.</summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatSpriteBatch_DrawsWithCompatValueTypes()
    {
        InsideACompatFrame(game =>
        {
            GraphicsDevice device = game.GraphicsDevice;

            using var texture = new Texture2D(device, 1, 1);
            texture.SetData([Color.White]);

            using var batch = new SpriteBatch(device);

            device.Clear(Color.CornflowerBlue);
            batch.Begin();
            batch.Draw(texture, new Vector2(3f, 4f), Color.White);
            batch.End();
        });
    }

    /// <summary>The compat viewport and its re-typed Rectangle, read from a live device.</summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGraphicsDevice_ViewportIsCompatTyped()
    {
        InsideACompatFrame(game =>
        {
            Viewport viewport = game.GraphicsDevice.Viewport;
            Rectangle bounds = viewport.Bounds;

            output.WriteLine($"viewport {viewport.Width}x{viewport.Height}, bounds {bounds}");

            Assert.Equal(viewport.Width, bounds.Width);
            Assert.True(viewport.Width > 0);
        });
    }

    /// <summary>The compat state objects, which are separate types per namespace and convert on
    /// the way down.</summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGraphicsDevice_StateObjectsRoundTrip()
    {
        InsideACompatFrame(game =>
        {
            GraphicsDevice device = game.GraphicsDevice;

            BlendState blend = device.BlendState;
            DepthStencilState depth = device.DepthStencilState;
            RasterizerState rasterizer = device.RasterizerState;

            output.WriteLine($"blend={blend.ColorSourceBlend} depth={depth.DepthBufferEnable} cull={rasterizer.CullMode}");

            device.BlendState = BlendState.AlphaBlend;
            Assert.Equal(BlendState.AlphaBlend.ColorSourceBlend, device.BlendState.ColorSourceBlend);
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGraphicsDevice_StateAndCollectionWrappersHaveXnaIdentity()
    {
        InsideACompatFrame(game =>
        {
            GraphicsDevice device = game.GraphicsDevice;

            Assert.Same(BlendState.Opaque, device.BlendState);
            Assert.Same(DepthStencilState.Default, device.DepthStencilState);
            Assert.Same(RasterizerState.CullCounterClockwise, device.RasterizerState);
            Assert.Same(device, device.BlendState.GraphicsDevice);
            Assert.Equal("BlendState.Opaque", BlendState.Opaque.Name);
            Assert.Equal("DepthStencilState.Default", DepthStencilState.Default.Name);
            Assert.Equal("RasterizerState.CullCounterClockwise", RasterizerState.CullCounterClockwise.Name);
            Assert.Equal("SamplerState.LinearWrap", SamplerState.LinearWrap.Name);
            Assert.Throws<InvalidOperationException>(() =>
                BlendState.Opaque.ColorSourceBlend = Blend.Zero);

            Assert.Same(device.SamplerStates, device.SamplerStates);
            Assert.Same(device.VertexSamplerStates, device.VertexSamplerStates);
            Assert.Same(device.Textures, device.Textures);
            Assert.Same(device.VertexTextures, device.VertexTextures);
            Assert.Same(SamplerState.LinearWrap, device.SamplerStates[0]);

            // Colour and alpha share one function, so this state is valid in Reach too: a separate
            // alpha function is HiDef-only, and XNA's BlendState.Apply refuses it on assignment.
            using var blend = new BlendState
            {
                ColorSourceBlend = Blend.SourceAlpha,
                AlphaSourceBlend = Blend.SourceAlpha,
            };
            device.BlendState = blend;
            Assert.Same(blend, device.BlendState);
            Assert.Same(device, blend.GraphicsDevice);
            Assert.Throws<InvalidOperationException>(() => blend.ColorSourceBlend = Blend.One);
            device.BlendState = BlendState.Opaque;

            using var sampler = new SamplerState { Filter = TextureFilter.Point };
            device.SamplerStates[0] = sampler;
            Assert.Same(sampler, device.SamplerStates[0]);
            Assert.Throws<InvalidOperationException>(() => sampler.Filter = TextureFilter.Linear);
            device.SamplerStates[0] = SamplerState.LinearWrap;

            using var texture = new Texture2D(device, 1, 1);
            TextureCollection first = device.Textures;
            first[0] = texture;
            Assert.Same(texture, device.Textures[0]);
            device.Textures[0] = null;
        });
    }

    private sealed class LifecycleOrderGame : XnaGame
    {
        public LifecycleOrderGame()
        {
            _ = new GraphicsDeviceManager(this);
            Exiting += (_, _) => Order.Add("Exiting");
            Disposed += (_, _) => Order.Add("Disposed");
        }

        public List<string> Order { get; } = [];

        protected override void Initialize()
        {
            Order.Add("Initialize");
            base.Initialize();
        }

        protected override void LoadContent() => Order.Add("LoadContent");

        protected override void BeginRun() => Order.Add("BeginRun");

        protected override void Update(GameTime gameTime)
        {
            Order.Add("Update");
            Exit();
        }

        protected override void EndRun() => Order.Add("EndRun");
    }

    /// <summary>
    /// <c>Run</c> delivers XNA's order (its <c>RunGame</c>): Initialize, which loads content, then
    /// BeginRun, then the first Update; EndRun after Exiting. The facade used to call BeginRun
    /// itself before handing over to native, so it ran before Initialize and LoadContent -- a game
    /// that sets something up in LoadContent and starts using it in BeginRun found it missing.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGame_RunDeliversXnasLifecycleOrder()
    {
        var game = new LifecycleOrderGame();
        using (game)
        {
            game.Run();
        }

        output.WriteLine(string.Join(" ", game.Order));
        Assert.Equal(
            ["Initialize", "LoadContent", "BeginRun", "Update", "Exiting", "EndRun", "Disposed"],
            game.Order);
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGame_RunFrameEXTDeliversLifecycleAndStopsAfterExit()
    {
        using var game = new LifecycleOrderGame();
        bool running = true;
        for (int frame = 0; frame < 5 && running; frame++)
        {
            running = game.RunFrameEXT();
        }

        Assert.False(running);
        Assert.Equal(
            ["Initialize", "LoadContent", "BeginRun", "Update", "Exiting", "EndRun"],
            game.Order);
        Assert.False(game.RunFrameEXT());
        Assert.Equal(1, game.Order.Count(phase => phase == "Update"));
    }

    /// <summary>
    /// XNA's <c>Dispose(true)</c> raises <c>Disposed</c>, once. No game's did: the native event that
    /// was meant to carry it is raised inside <c>cna_game_destroy</c>, after the game has released
    /// the bridges that would have delivered it.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGame_DisposeRaisesDisposedOnce()
    {
        int disposed = 0;
        var game = new CompatProbe(_ => { });
        game.Disposed += (sender, _) =>
        {
            Assert.Same(game, sender);
            disposed++;
        };

        game.RunFrameEXT();
        game.Dispose();
        game.Dispose();

        Assert.Equal(1, disposed);
    }

    /// <summary>
    /// A SpriteBatch's states become the device's, where XNA's <c>SetRenderState</c> assigns them
    /// through the device properties: at End for a deferred batch -- an empty one included -- and at
    /// Begin for an Immediate one, null meaning the slot's default.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatSpriteBatch_AppliedStatesAreTheDevicesStates()
    {
        InsideACompatFrame(game =>
        {
            GraphicsDevice device = game.GraphicsDevice;
            using var batch = new SpriteBatch(device);

            batch.Begin();
            Assert.Same(BlendState.Opaque, device.BlendState);
            batch.End();
            Assert.Same(BlendState.AlphaBlend, device.BlendState);
            Assert.Same(DepthStencilState.None, device.DepthStencilState);
            Assert.Same(RasterizerState.CullCounterClockwise, device.RasterizerState);
            Assert.Same(SamplerState.LinearClamp, device.SamplerStates[0]);

            batch.Begin(
                SpriteSortMode.Immediate,
                BlendState.Additive,
                SamplerState.PointWrap,
                DepthStencilState.DepthRead,
                RasterizerState.CullNone);
            Assert.Same(BlendState.Additive, device.BlendState);
            Assert.Same(DepthStencilState.DepthRead, device.DepthStencilState);
            Assert.Same(RasterizerState.CullNone, device.RasterizerState);
            Assert.Same(SamplerState.PointWrap, device.SamplerStates[0]);
            batch.End();
            Assert.Same(BlendState.Additive, device.BlendState);
        });
    }

    /// <summary>
    /// <c>BlendFactor</c>, <c>MultiSampleMask</c> and <c>ReferenceStencil</c> override a field of the
    /// active state object, so -- as in XNA, whose setters mark that state dirty -- assigning the same
    /// object again re-applies it rather than being skipped.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGraphicsDevice_ReassigningAnOverriddenStateReappliesIt()
    {
        InsideACompatFrame(game =>
        {
            GraphicsDevice device = game.GraphicsDevice;
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.Default;

            device.BlendFactor = Color.Red;
            device.MultiSampleMask = 0x0F;
            device.ReferenceStencil = 5;
            Assert.Equal(Color.Red, device.BlendFactor);
            Assert.Equal(5, device.ReferenceStencil);

            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.Default;
            Assert.Equal(BlendState.Opaque.BlendFactor, device.BlendFactor);
            Assert.Equal(BlendState.Opaque.MultiSampleMask, device.MultiSampleMask);
            Assert.Equal(DepthStencilState.Default.ReferenceStencil, device.ReferenceStencil);
        });
    }

    /// <summary>
    /// The state a game assigns after a SpriteBatch reaches the renderer. The device's cache used to
    /// keep the pre-batch <c>Opaque</c> while native held the batch's <c>AlphaBlend</c>, so the
    /// game's own <c>BlendState = Opaque</c> was skipped as already set -- the ColorReplacement
    /// sample's tyres, whose texels carry alpha 0, blended away. A transparent black quad over the
    /// clear colour reads back black under Opaque and as the clear colour under AlphaBlend.
    /// </summary>
    [global::CNA.Integration.Tests.Native3DFact]
    public void CompatSpriteBatch_StateAssignedAfterABatchReachesTheRenderer()
    {
        InsideACompatFrame(game =>
        {
            GraphicsDevice device = game.GraphicsDevice;
            using var target = new RenderTarget2D(device, 4, 4);
            using var batch = new SpriteBatch(device);
            using var effect = new BasicEffect(device) { VertexColorEnabled = true };
            var transparent = new Color(0, 0, 0, 0);
            var quad = new[]
            {
                new VertexPositionColor(new Vector3(-1, -1, 0), transparent),
                new VertexPositionColor(new Vector3(-1, 1, 0), transparent),
                new VertexPositionColor(new Vector3(1, -1, 0), transparent),
                new VertexPositionColor(new Vector3(1, -1, 0), transparent),
                new VertexPositionColor(new Vector3(-1, 1, 0), transparent),
                new VertexPositionColor(new Vector3(1, 1, 0), transparent),
            };

            device.SetRenderTarget(target);
            device.Clear(Color.CornflowerBlue);
            device.BlendState = BlendState.Opaque;
            batch.Begin();
            batch.End();
            device.BlendState = BlendState.Opaque;
            device.RasterizerState = RasterizerState.CullNone;
            effect.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleList, quad, 0, 2);
            device.SetRenderTarget(null);

            var pixels = new Color[16];
            target.GetData(pixels);
            output.WriteLine($"centre {pixels[5]}");
            Assert.All(pixels, pixel => Assert.Equal(transparent, pixel));
        });
    }

    /// <summary>
    /// A stock effect handed to <c>SpriteBatch.Begin</c> places the sprites: its vertex shader
    /// transforms them, with <c>layerDepth</c> as their z. This is XNA 4.0's way to draw sprites in a
    /// 3D world -- DPSF's 3D billboard particles (Kosmic Warz's starfield) and 3D text both use it.
    /// Through an orthographic projection over [-1, 1], a one-unit sprite at the origin covers the
    /// target's top-right quadrant; drawn in pixel space instead it covers one pixel.
    /// </summary>
    [global::CNA.Integration.Tests.Native3DFact]
    public void CompatSpriteBatch_AStockEffectPlacesTheSprites()
    {
        InsideACompatFrame(game =>
        {
            GraphicsDevice device = game.GraphicsDevice;
            const int size = 32;
            using var target = new RenderTarget2D(device, size, size);
            using var batch = new SpriteBatch(device);
            using var white = new Texture2D(device, 1, 1);
            white.SetData(new[] { Color.White });
            using var effect = new AlphaTestEffect(device)
            {
                Projection = Matrix.CreateOrthographicOffCenter(-1, 1, -1, 1, 0, 10),
                VertexColorEnabled = true,
            };

            device.SetRenderTarget(target);
            device.Clear(Color.Black);
            batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, null, DepthStencilState.None, RasterizerState.CullNone, effect);
            batch.Draw(white, Vector2.Zero, null, Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, -1f);
            batch.End();
            device.SetRenderTarget(null);

            var pixels = new Color[size * size];
            target.GetData(pixels);
            var rows = new System.Text.StringBuilder();
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    rows.Append(pixels[(y * size) + x].R > 127 ? '#' : '.');
                }

                rows.AppendLine();
            }

            output.WriteLine(rows.ToString());
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool lit = pixels[(y * size) + x].R > 127;
                    Assert.True(lit == (x >= size / 2 && y < size / 2), $"pixel {x},{y} is {(lit ? "lit" : "dark")}");
                }
            }
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGraphicsResource_DisposingSeesDisposedStateAndFiresOnceAfterHandlerFailure()
    {
        InsideACompatFrame(_ =>
        {
            var state = new BlendState
            {
                Name = "lifecycle-state",
                Tag = new object(),
            };
            object? tag = state.Tag;
            int calls = 0;

            state.Disposing += (_, _) =>
            {
                calls++;
                Assert.True(state.IsDisposed);
                throw new InvalidOperationException("handler failure");
            };

            Assert.Throws<InvalidOperationException>(state.Dispose);
            Assert.True(state.IsDisposed);
            Assert.Equal(1, calls);

            state.Dispose();
            Assert.Equal(1, calls);
            Assert.Equal("lifecycle-state", state.Name);
            Assert.Same(tag, state.Tag);
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGraphicsDevice_PresentOverloadRejectsUnrepresentableArguments()
    {
        InsideACompatFrame(game =>
        {
            GraphicsDevice device = game.GraphicsDevice;

            Assert.Throws<NotSupportedException>(() =>
                device.Present(new Rectangle(0, 0, 1, 1), null, IntPtr.Zero));
            Assert.Throws<NotSupportedException>(() =>
                device.Present(null, new Rectangle(0, 0, 1, 1), IntPtr.Zero));
            Assert.Throws<NotSupportedException>(() =>
                device.Present(null, null, new IntPtr(1)));
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGraphicsDevice_ResourceEventSubscriptionIsSafeWhileAbiCannotReportWrappers()
    {
        InsideACompatFrame(game =>
        {
            int created = 0;
            int destroyed = 0;
            EventHandler<ResourceCreatedEventArgs> onCreated = (_, _) => created++;
            EventHandler<ResourceDestroyedEventArgs> onDestroyed = (_, _) => destroyed++;

            game.GraphicsDevice.ResourceCreated += onCreated;
            game.GraphicsDevice.ResourceDestroyed += onDestroyed;
            using (var texture = new Texture2D(game.GraphicsDevice, 1, 1))
            {
                texture.Name = "resource-event-safety";
            }

            game.GraphicsDevice.ResourceCreated -= onCreated;
            game.GraphicsDevice.ResourceDestroyed -= onDestroyed;

            // ABI 0.6 reports only presence/name bytes and cannot identify the actual facade
            // object required by XNA. The safe fallback is no event, never a null/fake resource or
            // the former callback-signature crash.
            Assert.Equal(0, created);
            Assert.Equal(0, destroyed);
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void CompatTexture_DescriptionRemainsReadableAfterDispose()
    {
        InsideACompatFrame(game =>
        {
            var texture = new Texture2D(game.GraphicsDevice, 2, 3, false, SurfaceFormat.Color);
            texture.Dispose();

            Assert.Equal(2, texture.Width);
            Assert.Equal(3, texture.Height);
            Assert.Equal(1, texture.LevelCount);
            Assert.Equal(SurfaceFormat.Color, texture.Format);
            Assert.Equal(new Rectangle(0, 0, 2, 3), texture.Bounds);
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void CompatSpriteBatch_InvalidOrderPreservesXnaStateMachine()
    {
        InsideACompatFrame(game =>
        {
            using var texture = new Texture2D(game.GraphicsDevice, 1, 1);
            texture.SetData([Color.White]);
            using var batch = new SpriteBatch(game.GraphicsDevice);

            Assert.Throws<InvalidOperationException>(batch.End);
            Assert.Throws<ArgumentNullException>(() =>
                batch.Draw(null!, Vector2.Zero, Color.White));
            Assert.Throws<InvalidOperationException>(() =>
                batch.Draw(texture, Vector2.Zero, Color.White));

            batch.Begin();
            Assert.Throws<InvalidOperationException>(batch.Begin);
            batch.End();
            Assert.Throws<InvalidOperationException>(() =>
                batch.Draw(texture, Vector2.Zero, Color.White));

            batch.Begin();
            batch.End();
        });
    }

    /// <summary>
    /// Where <c>SetDataOptions</c> reaches the ABI, and where it deliberately cannot.
    ///
    /// The vertex half used to assert <c>NotSupportedException</c> for a windowed optioned upload,
    /// because the option reached the ABI only through the built-in typed route. CNA 0.19.0 added
    /// <c>cna_vertex_buffer_set_data_raw_with_options</c> and its windowed twin, so the option is
    /// now forwarded and the bytes are asserted where XNA puts them.
    ///
    /// The index half still refuses, and that is upstream's stated position rather than a gap in
    /// the ABI shape: <c>cna_index_buffer_set_data_at</c> carries an options field and rejects
    /// anything but <c>None</c>, because "a windowed upload preserves the rest of the buffer, so it
    /// accepts no SetDataOptions other than None". XNA does accept them there, so this stays
    /// recorded as a behavioural difference in docs/native-behavior-blockers.md; what this test
    /// pins is that the difference surfaces as a refusal rather than a dropped hint.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatDynamicBuffers_ForwardSetDataOptionsWhereTheAbiCarriesThem()
    {
        InsideACompatFrame(game =>
        {
            if (!global::CNA.XnaCompat.Extensions.CnaGraphicsDeviceExtensions.SupportsCnaCapability(
                    game.GraphicsDevice,
                    global::CNA.XnaCompat.Extensions.CnaGraphicsCapability.ThreeD))
            {
                // The absent branch, asserted rather than skipped. Measured on a SDL_RENDERER
                // build: the dynamic buffer itself is refused with NotSupported, so there is never
                // anything to forward a SetDataOptions to.
                var refusal = Assert.Throws<global::CNA.CnaException>(
                    () => new DynamicVertexBuffer(
                        game.GraphicsDevice,
                        VertexPositionColor.VertexDeclaration,
                        3,
                        BufferUsage.None).Dispose());
                Assert.Equal("NotSupported", refusal.NativeResult);
                output.WriteLine(
                    $"ABSENT BRANCH EXERCISED: DynamicVertexBuffer refused with NotSupported -- {refusal.Message}");
                return;
            }

            var vertices = new[]
            {
                new VertexPositionColor(Vector3.Zero, Color.Red),
                new VertexPositionColor(Vector3.UnitX, Color.Green),
                new VertexPositionColor(Vector3.UnitY, Color.Blue),
            };
            using var vertexBuffer = new DynamicVertexBuffer(
                game.GraphicsDevice,
                VertexPositionColor.VertexDeclaration,
                vertices.Length,
                BufferUsage.None);

            vertexBuffer.SetData(vertices, 0, vertices.Length, SetDataOptions.Discard);
            vertexBuffer.SetData(vertices, 0, vertices.Length, SetDataOptions.NoOverwrite);
            var vertexReadback = new VertexPositionColor[vertices.Length];
            vertexBuffer.GetData(vertexReadback);
            Assert.Equal(vertices[1].Position, vertexReadback[1].Position);

            // The windowed optioned upload. Asserting the neighbours is the part that matters: a
            // route that confused the buffer offset with the caller-array offset would still write
            // plausible data and still round-trip the vertex it was asked about.
            int stride = VertexPositionColor.VertexDeclaration.VertexStride;
            var replacement = new VertexPositionColor(Vector3.UnitZ, Color.White);
            vertexBuffer.SetData(stride, new[] { replacement }, 0, 1, stride, SetDataOptions.NoOverwrite);
            vertexBuffer.GetData(vertexReadback);
            Assert.Equal(vertices[0].Position, vertexReadback[0].Position);
            Assert.Equal(replacement.Position, vertexReadback[1].Position);
            Assert.Equal(vertices[2].Position, vertexReadback[2].Position);

            using var indexBuffer = new DynamicIndexBuffer(
                game.GraphicsDevice,
                IndexElementSize.SixteenBits,
                3,
                BufferUsage.None);
            ushort[] indices = [0, 1, 2];
            indexBuffer.SetData(indices, 0, indices.Length, SetDataOptions.None);
            indexBuffer.SetData(indices, 0, indices.Length, SetDataOptions.Discard);
            indexBuffer.SetData(indices, 0, indices.Length, SetDataOptions.NoOverwrite);

            // A windowed optioned upload. This used to throw, which broke the commonest use of the
            // overload -- a batcher rewriting one slice per frame with NoOverwrite.
            indexBuffer.SetData(2, new ushort[] { 7 }, 0, 1, SetDataOptions.NoOverwrite);

            var indexReadback = new ushort[3];
            indexBuffer.GetData(indexReadback);
            Assert.Equal([(ushort)0, (ushort)7, (ushort)2], indexReadback);

            // And reading back from a nonzero offset, which had no route at all.
            var tail = new ushort[2];
            indexBuffer.GetData(2, tail, 0, 2);
            Assert.Equal([(ushort)7, (ushort)2], tail);
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGraphicsDevice_DrawValidationMatchesXnaBeforeNativeDispatch()
    {
        InsideACompatFrame(game =>
        {
            GraphicsDevice device = game.GraphicsDevice;
            var vertices = new[]
            {
                new VertexPositionColor(Vector3.Zero, Color.Red),
                new VertexPositionColor(Vector3.UnitX, Color.Green),
                new VertexPositionColor(Vector3.UnitY, Color.Blue),
            };
            short[] indices = [0, 1, 2];

            Assert.Equal("vertexData", Assert.Throws<ArgumentNullException>(() =>
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList, (VertexPositionColor[])null!, 0, 1)).ParamName);
            Assert.Equal("vertexOffset", Assert.Throws<ArgumentOutOfRangeException>(() =>
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList, new VertexPositionColor[0], 0, 1)).ParamName);
            Assert.Equal("primitiveCount", Assert.Throws<ArgumentOutOfRangeException>(() =>
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList, vertices, 1, 1)).ParamName);

            Assert.Equal("vertexData", Assert.Throws<ArgumentNullException>(() =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, new VertexPositionColor[0], 0, 3,
                    indices, 0, 1)).ParamName);
            Assert.Equal("indexData", Assert.Throws<ArgumentNullException>(() =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, vertices, 0, 3,
                    Array.Empty<short>(), 0, 1)).ParamName);
            Assert.Equal("numVertices", Assert.Throws<ArgumentOutOfRangeException>(() =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, vertices, 0, 0, indices, 0, 1)).ParamName);
            Assert.Equal("primitiveCount", Assert.Throws<ArgumentOutOfRangeException>(() =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, vertices, 0, 3, new short[2], 0, 1)).ParamName);
            Assert.Equal("vertexData", Assert.Throws<ArgumentOutOfRangeException>(() =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, vertices, 1, 3, indices, 0, 1)).ParamName);

            using var declaration = new VertexDeclaration(
                16,
                new VertexElement(
                    0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0));
            var managedVertices = new[]
            {
                new ManagedReferenceVertex(Vector3.Zero, "a"),
                new ManagedReferenceVertex(Vector3.UnitX, "b"),
                new ManagedReferenceVertex(Vector3.UnitY, "c"),
            };
            ArgumentException managedReference = Assert.Throws<ArgumentException>(() =>
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList, managedVertices, 0, 1, declaration));
            Assert.Equal("vertexData", managedReference.ParamName);

            Assert.Equal("primitiveCount", Assert.Throws<ArgumentOutOfRangeException>(() =>
                device.DrawPrimitives(PrimitiveType.TriangleList, 0, 0)).ParamName);
            Assert.Equal("numVertices", Assert.Throws<ArgumentOutOfRangeException>(() =>
                device.DrawIndexedPrimitives(
                    PrimitiveType.TriangleList, 0, 0, 0, 0, 1)).ParamName);
            Assert.Equal("instanceCount", Assert.Throws<ArgumentOutOfRangeException>(() =>
                device.DrawInstancedPrimitives(
                    PrimitiveType.TriangleList, 0, 0, 3, 0, 1, 0)).ParamName);

            using var effect = new BasicEffect(device);
            effect.CurrentTechnique.Passes[0].Apply();
            Assert.Throws<InvalidOperationException>(() =>
                device.DrawPrimitives(PrimitiveType.TriangleList, 0, 1));
        });
    }

    /// <summary>Compat input, whose Keys and Buttons enums are duplicated per namespace and must
    /// stay numerically identical to the CNA ones for any of this to work.</summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatInput_ReportsState()
    {
        InsideACompatFrame(_ =>
        {
            KeyboardState keyboard = Keyboard.GetState();
            Assert.False(keyboard.IsKeyDown(Keys.Escape));

            MouseState mouse = Mouse.GetState();
            GamePadState pad = GamePad.GetState(PlayerIndex.One);

            output.WriteLine($"mouse ({mouse.X},{mouse.Y}) pad connected={pad.IsConnected}");
        });
    }

    /// <summary>A compat effect: the family that composes its CNA counterpart rather than deriving
    /// from it, so every forwarding member is a place the two halves can drift apart.</summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatBasicEffect_AppliesAndReTypesItsMatrices()
    {
        InsideACompatFrame(game =>
        {
            using var effect = new BasicEffect(game.GraphicsDevice)
            {
                World = Matrix.Identity,
                View = Matrix.CreateLookAt(new Vector3(0f, 0f, 5f), Vector3.Zero, Vector3.Up),
                DiffuseColor = new Vector3(0.5f, 0.25f, 0.125f),
            };

            effect.CurrentTechnique.Passes[0].Apply();

            Assert.Equal(0.5f, effect.DiffuseColor.X, 1e-4f);
            output.WriteLine($"diffuse={effect.DiffuseColor} world={effect.World.M11}");

            using Effect clone = effect.Clone();
            Assert.IsAssignableFrom<BasicEffect>(clone);
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void CompatEffectReflection_ReturnsStableWrappersWithoutOwnedHandleChurn()
    {
        InsideACompatFrame(game =>
        {
            using var effect = new BasicEffect(game.GraphicsDevice);

            Assert.Same(effect.Parameters, effect.Parameters);
            Assert.Same(effect.Techniques, effect.Techniques);

            EffectTechnique technique = effect.CurrentTechnique;
            Assert.Same(technique, effect.CurrentTechnique);
            Assert.Same(technique, effect.Techniques[technique.Name]);
            Assert.Same(technique.Passes, technique.Passes);
            Assert.Same(technique.Annotations, technique.Annotations);
            Assert.Null(effect.Techniques[-1]);
            Assert.Null(effect.Techniques[(string)null!]);

            EffectPass pass = technique.Passes[0];
            Assert.Same(pass, technique.Passes[0]);
            Assert.Same(pass, technique.Passes[pass.Name]);
            Assert.Same(pass.Annotations, pass.Annotations);
            Assert.Null(technique.Passes[-1]);

            if (effect.Parameters.Count > 0)
            {
                EffectParameter parameter = effect.Parameters[0];
                Assert.Same(parameter, effect.Parameters[0]);
                Assert.Same(parameter, effect.Parameters[parameter.Name]);
                Assert.Same(parameter.Elements, parameter.Elements);
                Assert.Same(parameter.StructureMembers, parameter.StructureMembers);
                Assert.Same(parameter.Annotations, parameter.Annotations);
                Assert.Null(effect.Parameters[-1]);
                Assert.Null(effect.Parameters[(string)null!]);
            }

            for (int i = 0; i < 256; i++)
            {
                Assert.Same(technique, effect.CurrentTechnique);
                Assert.Same(pass, technique.Passes[0]);
            }
        });
    }

    /// <summary>Compat render targets, which derive from the compat Texture2D rather than from
    /// CNA's RenderTarget2D -- the divergence that needed GetRenderTargetProperties.</summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatRenderTarget2D_ReportsItsProperties()
    {
        InsideACompatFrame(game =>
        {
            using var target = new RenderTarget2D(game.GraphicsDevice, 32, 16);

            output.WriteLine(
                $"{target.Width}x{target.Height} depth={target.DepthStencilFormat} " +
                $"usage={target.RenderTargetUsage} lost={target.IsContentLost}");

            Assert.Equal(32, target.Width);

            // A compat render target must be usable where a compat Texture2D is expected -- that is
            // the whole reason it derives from this namespace's Texture2D.
            Texture2D asTexture = target;
            Assert.Equal(32, asTexture.Width);
        });
    }

    /// <summary>
    /// gamealgorithms/defense, like many XNA games, reads GraphicsAdapter.DefaultAdapter in its
    /// constructor -- XNA's way to size a back buffer from the desktop's display mode -- before any
    /// lifecycle callback has run. CNA lent its device only inside a callback, so the adapters
    /// failed there; the adapter routes take the game's own handle since C ABI 0.40.0 (CSX-111).
    /// </summary>
    [Fact]
    public void WindowsForms_FromHandleFindsTheGamesOwnForm()
    {
        // The XNA 4.0 Racing Game Kit's BaseGame constructor does exactly this to hide its form.
        using var game = new FormInConstructorGame();
        if (game.Window.Handle == IntPtr.Zero)
        {
            // A windowless run: SDL's offscreen or dummy video driver gives the game no native
            // window -- on macOS that is what a game created off the main thread gets (CNA
            // plans/plan_apple_m4.md AM4-223). There is then nothing for a form to stand for, and
            // Windows Forms answers null for a handle it did not create; the windowed claims below
            // need a window to be about.
            Assert.Null(game.FormInConstructor);
            Assert.Null(System.Windows.Forms.Control.FromHandle(game.Window.Handle));
            game.RunFrameEXT();
            return;
        }
        Assert.NotNull(game.FormInConstructor);
        Assert.Same(game.FormInConstructor, System.Windows.Forms.Control.FromHandle(game.Window.Handle));
        Assert.Equal(0.0, game.FormInConstructor!.Opacity);
        Assert.Equal(System.Windows.Forms.FormBorderStyle.None, game.FormInConstructor.FormBorderStyle);
        Assert.Null(System.Windows.Forms.Control.FromHandle(IntPtr.Zero));
        Assert.Null(System.Windows.Forms.Control.FromHandle(game.Window.Handle + 1));

        // Back to a bordered window, through the same route.
        game.FormInConstructor.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
        Assert.Equal(System.Windows.Forms.FormBorderStyle.Sizable, game.FormInConstructor.FormBorderStyle);

        game.RunFrameEXT();
    }

    [Fact]
    public void WindowsForms_MessageBoxUsesTheNativeDialogService()
    {
        using var game = new FormInConstructorGame();
        CNA.NativeMessageBox.SetTestBackendForTests(true, 1);
        try
        {
            Assert.Equal(
                System.Windows.Forms.DialogResult.Cancel,
                System.Windows.Forms.MessageBox.Show(
                    "Body",
                    "Title",
                    System.Windows.Forms.MessageBoxButtons.OKCancel,
                    System.Windows.Forms.MessageBoxIcon.Warning));
        }
        finally
        {
            CNA.NativeMessageBox.SetTestBackendForTests(false, 0);
        }
    }

    private sealed class FormInConstructorGame : XnaGame
    {
        public FormInConstructorGame()
        {
            var graphics = new GraphicsDeviceManager(this);
            graphics.PreferredBackBufferWidth = 320;
            graphics.PreferredBackBufferHeight = 240;
            FormInConstructor = (System.Windows.Forms.Form?)System.Windows.Forms.Form.FromHandle(Window.Handle);
            if (FormInConstructor is not null)
            {
                FormInConstructor.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                FormInConstructor.Opacity = -1;
            }
        }

        public System.Windows.Forms.Form? FormInConstructor { get; }
    }

    private sealed class AdapterInConstructorGame : XnaGame
    {
        public AdapterInConstructorGame()
        {
            var graphics = new GraphicsDeviceManager(this);
            // XNA (IL): no device until Run creates it -- null, not an exception.
            DeviceInConstructor = graphics.GraphicsDevice ?? GraphicsDevice;
            AdapterCount = GraphicsAdapter.Adapters.Count;
            ModeInConstructor = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            graphics.PreferredBackBufferWidth = Math.Min(ModeInConstructor.Width, 320);
            graphics.PreferredBackBufferHeight = Math.Min(ModeInConstructor.Height, 240);
        }

        public int AdapterCount { get; }

        public GraphicsDevice? DeviceInConstructor { get; }

        public DisplayMode ModeInConstructor { get; }

        public DisplayMode? ModeInUpdate { get; private set; }

        protected override void Update(GameTime gameTime)
        {
            ModeInUpdate = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            Exit();
            base.Update(gameTime);
        }
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGame_ReadsTheGraphicsAdaptersInItsConstructor()
    {
        using var game = new AdapterInConstructorGame();
        Assert.Null(game.DeviceInConstructor);
        Assert.True(game.AdapterCount > 0);
        Assert.True(game.ModeInConstructor.Width > 0 && game.ModeInConstructor.Height > 0);

        game.RunFrameEXT();

        Assert.NotNull(game.ModeInUpdate);
        Assert.NotNull(game.GraphicsDevice);
        Assert.Equal(game.ModeInConstructor.Width, game.ModeInUpdate.Width);
        Assert.Equal(game.ModeInConstructor.Height, game.ModeInUpdate.Height);
    }

    /// <summary>
    /// Kermit/SKraft calls <c>ApplyChanges</c> in its constructor and sizes its menu from
    /// <c>GraphicsDevice.Viewport</c> there (cna-cs CSX-121). XNA's ApplyChanges with no device
    /// creates one (IL: ChangeDevice(false) -> CreateDevice); native CNA lends it before the run
    /// since C ABI 0.42.0. The run then goes on with that device: CNA's game owns one device, where
    /// XNA's Run replaced the constructor's with a new one.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGame_ApplyChangesInItsConstructorCreatesTheDeviceItReads()
    {
        using var game = new ApplyChangesInConstructorGame();
        Assert.NotNull(game.DeviceInConstructor);
        Assert.Same(game.DeviceInConstructor, game.ManagerDeviceInConstructor);
        Assert.Equal(320, game.ViewportInConstructor.Width);
        Assert.Equal(240, game.ViewportInConstructor.Height);

        game.RunFrameEXT();

        Assert.Same(game.DeviceInConstructor, game.DeviceInUpdate);
        Assert.Equal(240, game.ViewportInUpdate.Height);
    }

    /// <summary>
    /// <c>Clear(Color)</c> in a game that asks for XNA's default <see cref="DepthFormat.Depth24"/>
    /// back buffer, on any renderer -- including one without a depth plane.
    ///
    /// SDL_RENDERER has no <c>DepthStencilBuffer</c> but keeps the requested format in its
    /// presentation parameters, and <c>cna_graphics_device_clear_options</c> refuses a plane the
    /// device does not have. The binding chose the planes from the format alone, so the overload
    /// every game calls once per frame threw there and the CNA game template died in its first
    /// <c>Draw</c> (CNA plans/plan_apple_m4.md AM4-231). Where the plane is missing an explicit depth
    /// clear must still be refused, which shows the default chose fewer planes rather than the
    /// device accepting anything.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGame_ClearColorWithTheDefaultDepthFormatOnAnyRenderer()
    {
        using var game = new DefaultDepthClearGame();
        game.RunFrameEXT();

        output.WriteLine(
            $"{game.RendererName}: depth plane {game.HasDepthPlane}, format {game.Format}");
        Assert.Equal(DepthFormat.Depth24, game.Format);
        Assert.Null(game.ClearFailure);
        Assert.True(game.Cleared);
        if (!game.HasDepthPlane)
            Assert.IsType<global::CNA.CnaException>(game.ExplicitDepthClearFailure);
        else
            Assert.Null(game.ExplicitDepthClearFailure);
    }

    /// <summary>
    /// dsplaisted/Disentanglement keeps <c>Keyboard.GetState()</c> as a field initializer, which runs
    /// before the Game constructor and so before any CNA game exists (cna-cs CSX-123). XNA answered
    /// from the thread's key state: nothing pressed. Once the game runs, input reads through it.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGame_ReadsInputInAFieldInitializer()
    {
        using var game = new InputInFieldInitializerGame();
        Assert.Empty(game.KeyboardBeforeConstructor.GetPressedKeys());
        Assert.False(game.PadBeforeConstructor.IsConnected);

        game.RunFrameEXT();

        Assert.True(game.ReadInputInUpdate);
    }

    /// <summary>
    /// A game is activated when its run starts, on the game thread with its device at hand: XNA's
    /// form was activated as it was shown. Charles Petzold's PhreeCell deals its cards and sizes
    /// its table from OnActivated, reading the viewport, and drew nothing while CNA refused the
    /// device there (C ABI 0.44.0, cna-cs CSX-125).
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGame_IsActivatedWhenItsRunStarts_WithItsDevice()
    {
        using var game = new ActivationGame();
        game.Run();

        Assert.Equal(1, game.ActivatedBeforeFirstDraw);
        Assert.True(game.DeviceWhenActivated);
        Assert.Equal(240, game.ViewportHeightWhenActivated);
    }

    /// <summary>
    /// What OnActivated throws leaves Run, as it did from XNA's; the native event's callback has no
    /// error channel, and the failure used to vanish there (CSX-125).
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGame_AnOnActivatedThatThrows_FailsTheRun()
    {
        using var game = new ActivationGame { ThrowWhenActivated = true };
        var thrown = Assert.ThrowsAny<Exception>(() => game.Run());
        Assert.Contains("activation failed", thrown.ToString());
    }

    /// <summary>
    /// CSX-128: a game prints infinity and negative numbers as XNA's Windows did, in characters a
    /// default SpriteFont (32 to 126) holds (CSX-128; CSX-139 for times). Project Mercury's test bench draws its frame rate as
    /// <c>1 / ElapsedGameTime.TotalSeconds</c>, infinite on a frame of no elapsed time, and .NET's
    /// ICU data printed "∞"; Swedish ICU data prints its minus sign as U+2212.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void CompatGame_PrintsInfinityAndMinusAsXnasWindowsDid()
    {
        System.Globalization.CultureInfo thread = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo? threads = System.Globalization.CultureInfo.DefaultThreadCurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "sv-SE" })
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
                System.Globalization.CultureInfo.DefaultThreadCurrentCulture = null;
                using var game = new NumberPrintingGame();
                game.RunFrameEXT();
                if (culture == "en-US")
                {
                    // CSX-139: Windows' en-US long time had a space before AM/PM; ICU's has U+202F.
                    Assert.Equal("1:05:07 PM", game.LongTime);
                }

                string worker = "";
                var thread2 = new Thread(() => worker = (-1.0 / 0.0).ToString());
                thread2.Start();
                thread2.Join();

                Assert.Equal("Infinity", game.FrameRate);
                Assert.Equal("-Infinity", worker);
                Assert.Equal("-5", game.Negative);
                Assert.DoesNotContain('\u202F', game.LongTime);
                Assert.Equal(culture, System.Globalization.CultureInfo.CurrentCulture.Name);
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = thread;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = threads;
        }
    }

    private sealed class NumberPrintingGame : XnaGame
    {
        public NumberPrintingGame() => new GraphicsDeviceManager(this);

        public string FrameRate { get; private set; } = "";

        public string Negative { get; private set; } = "";

        public string LongTime { get; private set; } = "";

        protected override void Update(GameTime gameTime)
        {
            FrameRate = string.Format("{0:#.##}", 1f / TimeSpan.Zero.TotalSeconds);
            Negative = (-5).ToString();
            LongTime = new DateTime(2026, 1, 1, 13, 5, 7).ToLongTimeString();
            base.Update(gameTime);
        }
    }

    private sealed class ActivationGame : XnaGame
    {
        private int _activated;
        private int _draws;

        public ActivationGame()
        {
            var graphics = new GraphicsDeviceManager(this);
            graphics.PreferredBackBufferWidth = 320;
            graphics.PreferredBackBufferHeight = 240;
        }

        public int ActivatedBeforeFirstDraw { get; private set; } = -1;

        public bool DeviceWhenActivated { get; private set; }

        public int ViewportHeightWhenActivated { get; private set; }

        public bool ThrowWhenActivated { get; init; }

        protected override void OnActivated(object sender, EventArgs args)
        {
            _activated++;
            DeviceWhenActivated = GraphicsDevice is not null;
            if (ThrowWhenActivated)
            {
                throw new InvalidOperationException("activation failed");
            }
            ViewportHeightWhenActivated = GraphicsDevice!.Viewport.Height;
            base.OnActivated(sender, args);
        }

        protected override void Draw(GameTime gameTime)
        {
            if (_draws++ == 0)
            {
                ActivatedBeforeFirstDraw = _activated;
            }
            if (_draws > 3 || ThrowWhenActivated && _draws > 30)
            {
                Exit();
            }
            base.Draw(gameTime);
        }
    }

    private sealed class InputInFieldInitializerGame : XnaGame
    {
        public readonly KeyboardState KeyboardBeforeConstructor = Keyboard.GetState();
        public readonly GamePadState PadBeforeConstructor = GamePad.GetState(PlayerIndex.One);
        public readonly MouseState MouseBeforeConstructor = Mouse.GetState();

        public InputInFieldInitializerGame()
        {
            var graphics = new GraphicsDeviceManager(this);
            graphics.PreferredBackBufferWidth = 320;
            graphics.PreferredBackBufferHeight = 240;
        }

        public bool ReadInputInUpdate { get; private set; }

        protected override void Update(GameTime gameTime)
        {
            _ = Keyboard.GetState();
            _ = Mouse.GetState();
            ReadInputInUpdate = true;
            base.Update(gameTime);
        }
    }

    private sealed class DefaultDepthClearGame : XnaGame
    {
        public DefaultDepthClearGame()
        {
            _ = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 64,
                PreferredBackBufferHeight = 64,
                PreferredDepthStencilFormat = DepthFormat.Depth24,
            };
        }

        public string RendererName { get; private set; } = "";

        public bool HasDepthPlane { get; private set; }

        public DepthFormat Format { get; private set; }

        public bool Cleared { get; private set; }

        public Exception? ClearFailure { get; private set; }

        public Exception? ExplicitDepthClearFailure { get; private set; }

        protected override void Draw(GameTime gameTime)
        {
            RendererName = GraphicsDevice.GetCnaRendererName();
            HasDepthPlane = GraphicsDevice.SupportsCnaCapability(CnaGraphicsCapability.DepthStencilBuffer);
            Format = GraphicsDevice.PresentationParameters.DepthStencilFormat;
            ClearFailure = Record.Exception(() => GraphicsDevice.Clear(Color.CornflowerBlue));
            Cleared = ClearFailure is null;
            ExplicitDepthClearFailure = Record.Exception(() => GraphicsDevice.Clear(
                ClearOptions.Target | ClearOptions.DepthBuffer, Color.CornflowerBlue, 1f, 0));
            base.Draw(gameTime);
        }
    }

    private sealed class ApplyChangesInConstructorGame : XnaGame
    {
        public ApplyChangesInConstructorGame()
        {
            var graphics = new GraphicsDeviceManager(this);
            graphics.PreferredBackBufferWidth = 320;
            graphics.PreferredBackBufferHeight = 240;
            graphics.ApplyChanges();
            DeviceInConstructor = GraphicsDevice;
            ManagerDeviceInConstructor = graphics.GraphicsDevice;
            ViewportInConstructor = GraphicsDevice.Viewport;
        }

        public GraphicsDevice? DeviceInConstructor { get; }

        public GraphicsDevice? ManagerDeviceInConstructor { get; }

        public Viewport ViewportInConstructor { get; }

        public GraphicsDevice? DeviceInUpdate { get; private set; }

        public Viewport ViewportInUpdate { get; private set; }

        protected override void Update(GameTime gameTime)
        {
            DeviceInUpdate = GraphicsDevice;
            ViewportInUpdate = GraphicsDevice.Viewport;
            base.Update(gameTime);
        }
    }
}
