using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using CNA.BehaviorProbes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace XnaGraphicsBehaviorProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        LoadXnaRuntimeAssembliesIfRequested();

        var game = new ProbeGame();
        try
        {
            for (int frame = 0; frame < 4 && !game.Ran; frame++)
            {
                game.RunOneFrame();
            }

            if (game.Failure is not null)
            {
                Console.Error.WriteLine(game.Failure);
                return 1;
            }

            if (!game.Ran)
            {
                Console.Error.WriteLine("The graphics probe never reached Update.");
                return 1;
            }

            game.CaptureDeviceDisposal();
            ProbeOutput.Write(args, game.Observations);

            return 0;
        }
        finally
        {
            game.Dispose();
        }
    }

    private static void LoadXnaRuntimeAssembliesIfRequested()
    {
        string? directory = Environment.GetEnvironmentVariable("XNA_RUNTIME_PATH");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        foreach (string assemblyName in new[]
        {
            "Microsoft.Xna.Framework.dll",
            "Microsoft.Xna.Framework.Game.dll",
            "Microsoft.Xna.Framework.Graphics.dll",
        })
        {
            string assemblyPath = Path.Combine(directory, assemblyName);
            if (File.Exists(assemblyPath))
            {
                Assembly.LoadFrom(assemblyPath);
            }
        }
    }

    private sealed class ProbeGame : Game
    {
        private readonly GraphicsDeviceManager _graphics;
        private readonly bool _trace =
            Environment.GetEnvironmentVariable("XNA_GRAPHICS_PROBE_TRACE") == "1";
        private readonly bool _captureDrawValidation =
            Environment.GetEnvironmentVariable("XNA_GRAPHICS_PROBE_DRAW_VALIDATION") == "1";
        private readonly bool _captureDestructiveLifecycle =
            Environment.GetEnvironmentVariable("XNA_GRAPHICS_PROBE_DESTRUCTIVE_LIFECYCLE") == "1";
        private readonly bool _captureUnsafeConstructors =
            Environment.GetEnvironmentVariable("XNA_GRAPHICS_PROBE_UNSAFE_CONSTRUCTORS") == "1";

        private static readonly string[] DrawValidationObservationNames =
        [
            "graphics.draw.user.null_vertices",
            "graphics.draw.user.empty_vertices",
            "graphics.draw.user.negative_offset",
            "graphics.draw.user.zero_count",
            "graphics.draw.user.beyond_vertices",
            "graphics.draw.user.invalid_topology",
            "graphics.draw.user.managed_reference",
            "graphics.draw.user.custom_wrong_stride",
            "graphics.draw.user.disposed_declaration",
            "graphics.draw.user_indexed.null_vertices",
            "graphics.draw.user_indexed.empty_vertices",
            "graphics.draw.user_indexed.null_indices",
            "graphics.draw.user_indexed.empty_indices",
            "graphics.draw.user_indexed.zero_vertices",
            "graphics.draw.user_indexed.zero_count",
            "graphics.draw.user_indexed.negative_vertex_offset",
            "graphics.draw.user_indexed.negative_index_offset",
            "graphics.draw.user_indexed.indices_too_small",
            "graphics.draw.user_indexed.vertex_window_too_large",
            "graphics.draw.user_indexed.invalid_topology",
            "graphics.draw.bound.zero_count",
            "graphics.draw.indexed.zero_vertices",
            "graphics.draw.indexed.zero_count",
            "graphics.draw.instanced.zero_instances",
            "graphics.draw.bind.disposed_vertex_buffer",
            "graphics.draw.bind.disposed_index_buffer",
            "graphics.draw.bound.missing_vertex_buffer",
            "graphics.draw.indexed.missing_indices",
            "graphics.draw.indexed.missing_vertex_buffer",
        ];

        public ProbeGame()
        {
            // The probe measures HiDef behaviour (separate alpha blending, volume textures), which
            // XNA refuses under Reach -- the profile a game whose assembly carries no RuntimeProfile
            // resource starts on, as this project's does. Asked for in code, it holds for every
            // CompatibilityTarget alike.
            _graphics = new GraphicsDeviceManager(this) { GraphicsProfile = GraphicsProfile.HiDef };
        }

        public bool Ran { get; private set; }

        public Exception? Failure { get; private set; }

        public List<string> Observations { get; } = [];

        private int _deviceDisposingCount;
        private bool _deviceDisposingSender;
        private bool _deviceDisposedInside;
        private EventHandler<EventArgs>? _deviceDisposingHandler;

        protected override void Update(GameTime gameTime)
        {
            if (!Ran)
            {
                Ran = true;
                try
                {
                    Capture(GraphicsDevice);
                }
                catch (Exception exception)
                {
                    Failure = exception;
                }
            }

            Exit();
            base.Update(gameTime);
        }

        public void CaptureDeviceDisposal()
        {
            Observe("resource.device.dispose", Dispose);
            Observe("resource.device.dispose_second", Dispose);
            Add("resource.device.disposing_count", _deviceDisposingCount);
            Add("resource.device.disposing_sender", _deviceDisposingSender);
            Add("resource.device.disposed_inside", _deviceDisposedInside);
        }

        private void Capture(GraphicsDevice device)
        {
            _deviceDisposingHandler = (sender, _) =>
            {
                _deviceDisposingCount++;
                _deviceDisposingSender |= ReferenceEquals(sender, device);
                _deviceDisposedInside |= device.IsDisposed;
                device.Disposing -= _deviceDisposingHandler;
            };
            device.Disposing += _deviceDisposingHandler;

            Add("graphics.device.blend.repeated", ReferenceEquals(device.BlendState, device.BlendState));
            Add("graphics.device.depth.repeated", ReferenceEquals(device.DepthStencilState, device.DepthStencilState));
            Add("graphics.device.raster.repeated", ReferenceEquals(device.RasterizerState, device.RasterizerState));
            Add("graphics.device.samplers.repeated", ReferenceEquals(device.SamplerStates, device.SamplerStates));
            Add("graphics.device.vertex_samplers.repeated", ReferenceEquals(device.VertexSamplerStates, device.VertexSamplerStates));
            Add("graphics.device.textures.repeated", ReferenceEquals(device.Textures, device.Textures));
            Add("graphics.device.vertex_textures.repeated", ReferenceEquals(device.VertexTextures, device.VertexTextures));
            Add("graphics.stock.blend.repeated", ReferenceEquals(BlendState.Opaque, BlendState.Opaque));
            Add("graphics.stock.depth.repeated", ReferenceEquals(DepthStencilState.Default, DepthStencilState.Default));
            Add("graphics.stock.raster.repeated", ReferenceEquals(
                RasterizerState.CullCounterClockwise, RasterizerState.CullCounterClockwise));
            Add("graphics.stock.sampler.repeated", ReferenceEquals(SamplerState.LinearWrap, SamplerState.LinearWrap));
            Add("graphics.device.blend.stock", ReferenceEquals(device.BlendState, BlendState.Opaque));
            Add("graphics.device.depth.stock", ReferenceEquals(device.DepthStencilState, DepthStencilState.Default));
            Add("graphics.device.raster.stock", ReferenceEquals(device.RasterizerState, RasterizerState.CullCounterClockwise));
            Add("graphics.device.sampler.stock", ReferenceEquals(device.SamplerStates[0], SamplerState.LinearWrap));
            Add("graphics.stock.blend.name", BlendState.Opaque.Name);
            Add("graphics.stock.sampler.name", SamplerState.LinearWrap.Name);
            Observe("graphics.stock.blend.mutate", () => BlendState.Opaque.ColorSourceBlend = Blend.Zero);

            using (var blend = new BlendState { ColorSourceBlend = Blend.SourceAlpha })
            {
                device.BlendState = blend;
                Add("graphics.state.blend.bound_identity", ReferenceEquals(blend, device.BlendState));
                Observe("graphics.state.blend.mutate_after_bind", () => blend.ColorSourceBlend = Blend.One);
                device.BlendState = BlendState.Opaque;
            }

            using (var sampler = new SamplerState { Filter = TextureFilter.Point })
            {
                device.SamplerStates[0] = sampler;
                Add("graphics.state.sampler.bound_identity", ReferenceEquals(sampler, device.SamplerStates[0]));
                Observe("graphics.state.sampler.mutate_after_bind", () => sampler.Filter = TextureFilter.Linear);
                device.SamplerStates[0] = SamplerState.LinearWrap;
            }

            using (var texture = new Texture2D(device, 1, 1))
            {
                texture.SetData(new[] { Color.White });
                device.Textures[0] = texture;
                Add("graphics.texture_slot.identity", ReferenceEquals(texture, device.Textures[0]));
                device.Textures[0] = null;
            }

            Observe("graphics.sampler.index.negative", () => _ = device.SamplerStates[-1]);
            Observe("graphics.sampler.index.upper", () => _ = device.SamplerStates[16]);
            Observe("graphics.texture.index.negative", () => _ = device.Textures[-1]);
            Observe("graphics.texture.index.upper", () => _ = device.Textures[16]);
            Observe("graphics.state.blend.null", () => device.BlendState = null!);
            Observe("graphics.state.depth.null", () => device.DepthStencilState = null!);
            Observe("graphics.state.raster.null", () => device.RasterizerState = null!);
            Observe("graphics.state.sampler.null", () => device.SamplerStates[0] = null!);

            CaptureStateBehavior(device);

            CaptureTransfers(device);
            CaptureSpriteBatch(device);
            CaptureEffectIdentity(device);
            if (_captureDrawValidation)
            {
                CaptureDrawValidation(device);
            }
            else
            {
                foreach (string name in DrawValidationObservationNames)
                {
                    Add(name, "not-run(opt-in-required)");
                }
            }
            CaptureConstructorValidation(device);
            CaptureResourceEvents(device);
            CaptureResourceLifecycle(device);

            Observe("graphics.present.source_rectangle", () => InvokePresentWithSourceRectangle(device));
        }

        private void CaptureStateBehavior(GraphicsDevice device)
        {
            using (var blend = new BlendState())
            {
                Add(
                    "graphics.state.blend.defaults",
                    $"{blend.ColorSourceBlend}/{blend.ColorDestinationBlend}/" +
                    $"{blend.AlphaSourceBlend}/{blend.AlphaDestinationBlend}/" +
                    $"{blend.ColorBlendFunction}/{blend.MultiSampleMask}");
            }

            using (var depth = new DepthStencilState())
            {
                Add(
                    "graphics.state.depth.defaults",
                    $"{depth.DepthBufferEnable}/{depth.DepthBufferWriteEnable}/" +
                    $"{depth.DepthBufferFunction}/{depth.StencilEnable}/{depth.ReferenceStencil}");
            }

            using (var raster = new RasterizerState())
            {
                Add(
                    "graphics.state.raster.defaults",
                    $"{raster.CullMode}/{raster.FillMode}/{raster.ScissorTestEnable}/" +
                    $"{raster.MultiSampleAntiAlias}");
            }

            using (var sampler = new SamplerState())
            {
                Add(
                    "graphics.state.sampler.defaults",
                    $"{sampler.Filter}/{sampler.AddressU}/{sampler.AddressV}/{sampler.AddressW}/" +
                    $"{sampler.MaxAnisotropy}/{sampler.MaxMipLevel}/" +
                    sampler.MipMapLevelOfDetailBias.ToString("R", CultureInfo.InvariantCulture));
            }

            var disposedUnbound = new BlendState();
            disposedUnbound.Dispose();
            Observe("graphics.state.disposed_unbound.mutate", () =>
                disposedUnbound.ColorSourceBlend = Blend.SourceAlpha);
            Observe("graphics.state.disposed_blend.assign", () => device.BlendState = disposedUnbound);

            var disposedSampler = new SamplerState();
            disposedSampler.Dispose();
            Observe("graphics.state.disposed_sampler.assign", () => device.SamplerStates[0] = disposedSampler);

            var boundThenDisposed = new BlendState();
            device.BlendState = boundThenDisposed;
            boundThenDisposed.Dispose();
            Observe("graphics.state.disposed_same_blend.assign", () => device.BlendState = boundThenDisposed);
            Add("graphics.state.disposed_same_blend.identity", ReferenceEquals(boundThenDisposed, device.BlendState));
            device.BlendState = BlendState.Opaque;

            using (var disposedTexture = new Texture2D(device, 1, 1))
            {
                disposedTexture.Dispose();
                Observe("graphics.texture_slot.disposed.assign", () => device.Textures[0] = disposedTexture);
            }

            Observe("graphics.vertex_sampler.index.upper", () => _ = device.VertexSamplerStates[4]);
            Observe("graphics.vertex_texture.index.upper", () => _ = device.VertexTextures[4]);

            string vertexSamplerZero = CaptureOutcome(() => _ = device.VertexSamplerStates[0]);
            string vertexTextureZero = CaptureOutcome(() => _ = device.VertexTextures[0]);
            Add("graphics.vertex_sampler.index.zero", vertexSamplerZero);
            Add("graphics.vertex_texture.index.zero", vertexTextureZero);

            if (vertexSamplerZero == "ok")
            {
                using var pixelSampler = new SamplerState { Filter = TextureFilter.Point };
                SamplerState before = device.VertexSamplerStates[0];
                device.SamplerStates[1] = pixelSampler;
                Add("graphics.sampler.stages.independent", ReferenceEquals(before, device.VertexSamplerStates[0]) &&
                    ReferenceEquals(pixelSampler, device.SamplerStates[1]));
                device.SamplerStates[1] = SamplerState.LinearWrap;
            }
            else
            {
                Add("graphics.sampler.stages.independent", "vertex-stage-unavailable");
            }

            if (vertexTextureZero == "ok")
            {
                using var texture = new Texture2D(device, 1, 1);
                Texture? before = device.VertexTextures[0];
                device.Textures[1] = texture;
                Add("graphics.texture.stages.independent", ReferenceEquals(before, device.VertexTextures[0]) &&
                    ReferenceEquals(texture, device.Textures[1]));
                device.Textures[1] = null;
            }
            else
            {
                Add("graphics.texture.stages.independent", "vertex-stage-unavailable");
            }
        }

        private void CaptureTransfers(GraphicsDevice device)
        {
            using (var texture = new Texture2D(device, 1, 1, false, SurfaceFormat.Color))
            {
                texture.SetData(new[] { new Color(1, 2, 3, 4) });
                Observe("graphics.readback.texture2d.uint", () => texture.GetData(new uint[1]));
            }

            Observe("graphics.readback.backbuffer.uint", () =>
                device.GetBackBufferData(new Rectangle(0, 0, 1, 1), new uint[1], 0, 1));

            Observe("graphics.readback.texture3d.uint", () =>
            {
                using var texture = new Texture3D(device, 1, 1, 1, false, SurfaceFormat.Color);
                texture.SetData(new[] { Color.White });
                texture.GetData(new uint[1]);
            });

            Observe("graphics.readback.texturecube.uint", () =>
            {
                using var texture = new TextureCube(device, 1, false, SurfaceFormat.Color);
                texture.SetData(CubeMapFace.PositiveX, new[] { Color.White });
                texture.GetData(CubeMapFace.PositiveX, new uint[1]);
            });

            var vertices = new[]
            {
                new VertexPositionColor(Vector3.Zero, Color.Red),
                new VertexPositionColor(Vector3.UnitX, Color.Green),
                new VertexPositionColor(Vector3.UnitY, Color.Blue),
            };
            using (var buffer = new DynamicVertexBuffer(
                       device, VertexPositionColor.VertexDeclaration, 3, BufferUsage.None))
            {
                Observe("graphics.dynamic_vertex.discard", () =>
                    buffer.SetData(vertices, 0, 3, SetDataOptions.Discard));
                Observe("graphics.dynamic_vertex.no_overwrite", () =>
                    buffer.SetData(vertices, 0, 3, SetDataOptions.NoOverwrite));
                Observe("graphics.dynamic_vertex.offset_no_overwrite", () =>
                    buffer.SetData(
                        VertexPositionColor.VertexDeclaration.VertexStride,
                        vertices,
                        0,
                        1,
                        VertexPositionColor.VertexDeclaration.VertexStride,
                        SetDataOptions.NoOverwrite));
            }

            using (var buffer = new VertexBuffer(
                       device, VertexPositionColor.VertexDeclaration, 2, BufferUsage.None))
            {
                byte[] source = Enumerable.Range(0, 2 * VertexPositionColor.VertexDeclaration.VertexStride)
                    .Select(static value => (byte)value)
                    .ToArray();
                byte[] destination = new byte[source.Length];
                string roundtrip = CaptureOutcome(() =>
                {
                    buffer.SetData(source);
                    buffer.GetData(destination);
                });
                Add("graphics.vertex.raw_bytes.roundtrip", $"{roundtrip}/{source.SequenceEqual(destination)}");
                Observe("graphics.vertex.partial_stride_set", () =>
                    buffer.SetData(0, new[] { 1, 2 }, 0, 2, VertexPositionColor.VertexDeclaration.VertexStride));
            }

            using (var buffer = new DynamicIndexBuffer(device, IndexElementSize.SixteenBits, 3, BufferUsage.None))
            {
                ushort[] indices = [0, 1, 2];
                Observe("graphics.dynamic_index.none", () =>
                    buffer.SetData(indices, 0, 3, SetDataOptions.None));
                Observe("graphics.dynamic_index.discard", () =>
                    buffer.SetData(indices, 0, 3, SetDataOptions.Discard));
                Observe("graphics.dynamic_index.no_overwrite", () =>
                    buffer.SetData(indices, 0, 3, SetDataOptions.NoOverwrite));
                Observe("graphics.dynamic_index.offset_no_overwrite", () =>
                    buffer.SetData(2, new ushort[] { 7 }, 0, 1, SetDataOptions.NoOverwrite));
            }
        }

        private void CaptureSpriteBatch(GraphicsDevice device)
        {
            using var texture = new Texture2D(device, 1, 1);
            texture.SetData(new[] { Color.White });
            using var batch = new SpriteBatch(device);

            Observe("graphics.sprite.end_before_begin", batch.End);
            Observe("graphics.sprite.null_texture_before_begin", () =>
                batch.Draw(null!, Vector2.Zero, Color.White));
            Observe("graphics.sprite.draw_before_begin", () =>
                batch.Draw(texture, Vector2.Zero, Color.White));

            batch.Begin();
            Observe("graphics.sprite.begin_twice", () => batch.Begin());
            batch.End();
            Observe("graphics.sprite.draw_after_end", () =>
                batch.Draw(texture, Vector2.Zero, Color.White));
            Observe("graphics.sprite.recover", () =>
            {
                batch.Begin();
                batch.End();
            });
            Observe("graphics.sprite.null_font", () =>
                batch.DrawString(null!, "text", Vector2.Zero, Color.White));
            Observe("graphics.sprite.null_font_and_string", () =>
                batch.DrawString(null!, (string)null!, Vector2.Zero, Color.White));

#pragma warning disable SYSLIB0050
            var nonNullFont = (SpriteFont)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
                typeof(SpriteFont));
#pragma warning restore SYSLIB0050
            Observe("graphics.sprite.null_string", () =>
                batch.DrawString(nonNullFont, (string)null!, Vector2.Zero, Color.White));
            Observe("graphics.sprite.null_string_builder", () =>
                batch.DrawString(nonNullFont, (StringBuilder)null!, Vector2.Zero, Color.White));

            ObserveSpriteDrawCase(
                "graphics.sprite.null_texture_during_begin",
                device,
                current => current.Draw(null!, Vector2.Zero, Color.White));
            ObserveSpriteDrawCase(
                "graphics.sprite.source_outside",
                device,
                current => current.Draw(
                    texture, Vector2.Zero, new Rectangle(-1, -1, 3, 3), Color.White));
            ObserveSpriteDrawCase(
                "graphics.sprite.source_negative_size",
                device,
                current => current.Draw(
                    texture, Vector2.Zero, new Rectangle(0, 0, -1, 1), Color.White));
            ObserveSpriteDrawCase(
                "graphics.sprite.destination_zero",
                device,
                current => current.Draw(texture, new Rectangle(0, 0, 0, 0), Color.White));
            ObserveSpriteDrawCase(
                "graphics.sprite.destination_negative",
                device,
                current => current.Draw(texture, new Rectangle(0, 0, -1, -1), Color.White));
            ObserveSpriteDrawCase(
                "graphics.sprite.nan_rotation",
                device,
                current => current.Draw(
                    texture, Vector2.Zero, null, Color.White, float.NaN, Vector2.Zero,
                    Vector2.One, SpriteEffects.None, 0f));
            ObserveSpriteDrawCase(
                "graphics.sprite.infinity_scale",
                device,
                current => current.Draw(
                    texture, Vector2.Zero, null, Color.White, 0f, Vector2.Zero,
                    new Vector2(float.PositiveInfinity, 1f), SpriteEffects.None, 0f));
            ObserveSpriteDrawCase(
                "graphics.sprite.nan_layer_depth",
                device,
                current => current.Draw(
                    texture, Vector2.Zero, null, Color.White, 0f, Vector2.Zero,
                    Vector2.One, SpriteEffects.None, float.NaN));

            using (var invalidSort = new SpriteBatch(device))
            {
                string begin = CaptureOutcome(() => invalidSort.Begin((SpriteSortMode)int.MaxValue, null));
                string draw = begin == "ok"
                    ? CaptureOutcome(() => invalidSort.Draw(texture, Vector2.Zero, Color.White))
                    : "not-run";
                string end = begin == "ok" ? CaptureOutcome(invalidSort.End) : "not-run";
                string recovery = CaptureOutcome(() =>
                {
                    invalidSort.Begin();
                    invalidSort.End();
                });
                Add("graphics.sprite.invalid_sort", $"begin:{begin}/draw:{draw}/end:{end}/recover:{recovery}");
            }

            using (var nullStates = new SpriteBatch(device))
            {
                Observe("graphics.sprite.null_states", () =>
                {
                    nullStates.Begin(
                        SpriteSortMode.Deferred, null, null, null, null, null, Matrix.Identity);
                    nullStates.Draw(texture, Vector2.Zero, Color.White);
                    nullStates.End();
                });
            }

            using (var effect = new BasicEffect(device))
            using (var customEffect = new SpriteBatch(device))
            {
                Observe("graphics.sprite.custom_effect", () =>
                {
                    customEffect.Begin(
                        SpriteSortMode.Deferred, null, null, null, null, effect, Matrix.Identity);
                    customEffect.Draw(texture, Vector2.Zero, Color.White);
                    customEffect.End();
                });
            }

            var disposedBlend = new BlendState();
            disposedBlend.Dispose();
            using (var disposedStateBatch = new SpriteBatch(device))
            {
                string begin = CaptureOutcome(() =>
                    disposedStateBatch.Begin(SpriteSortMode.Deferred, disposedBlend));
                string draw = begin == "ok"
                    ? CaptureOutcome(() => disposedStateBatch.Draw(texture, Vector2.Zero, Color.White))
                    : "not-run";
                string end = begin == "ok" ? CaptureOutcome(() => disposedStateBatch.End()) : "not-run";
                string recovery = CaptureOutcome(() =>
                {
                    disposedStateBatch.Begin();
                    disposedStateBatch.End();
                });
                Add("graphics.sprite.disposed_state", $"begin:{begin}/draw:{draw}/end:{end}/recover:{recovery}");
            }

            if (_captureDestructiveLifecycle)
            {
                var disposedDuringPair = new SpriteBatch(device);
                string activeBegin = CaptureOutcome(() => disposedDuringPair.Begin());
                string dispose = CaptureOutcome(() => disposedDuringPair.Dispose());
                string endAfterDispose = CaptureOutcome(() => disposedDuringPair.End());
                string beginAfterDispose = CaptureOutcome(() => disposedDuringPair.Begin());
                Add(
                    "graphics.sprite.dispose_during_pair",
                    $"begin:{activeBegin}/dispose:{dispose}/end:{endAfterDispose}/begin:{beginAfterDispose}");
                disposedDuringPair.Dispose();

                using var disposedBeforeBegin = new SpriteBatch(device);
                disposedBeforeBegin.Dispose();
                Observe("graphics.sprite.begin_after_dispose", () => disposedBeforeBegin.Begin());
            }
            else
            {
                Add("graphics.sprite.dispose_during_pair", "not-run(opt-in-required)");
                Add("graphics.sprite.begin_after_dispose", "not-run(opt-in-required)");
            }
        }

        private void ObserveSpriteDrawCase(
            string name,
            GraphicsDevice device,
            Action<SpriteBatch> draw)
        {
            using var batch = new SpriteBatch(device);
            string begin = CaptureOutcome(() => batch.Begin());
            string drawResult = begin == "ok" ? CaptureOutcome(() => draw(batch)) : "not-run";
            string end = begin == "ok" ? CaptureOutcome(() => batch.End()) : "not-run";
            string recovery = CaptureOutcome(() =>
            {
                batch.Begin();
                batch.End();
            });
            Add(name, $"begin:{begin}/draw:{drawResult}/end:{end}/recover:{recovery}");
        }

        private void CaptureEffectIdentity(GraphicsDevice device)
        {
            using var effect = new BasicEffect(device);
            EffectTechnique technique = effect.CurrentTechnique;
            EffectPass pass = technique.Passes[0];

            Add("graphics.effect.current.repeated", ReferenceEquals(technique, effect.CurrentTechnique));
            Add("graphics.effect.current.collection", ReferenceEquals(technique, effect.Techniques[technique.Name]));
            Add("graphics.effect.passes.repeated", ReferenceEquals(technique.Passes, technique.Passes));
            Add("graphics.effect.pass.repeated", ReferenceEquals(pass, technique.Passes[0]));
            Add("graphics.effect.pass.by_name", ReferenceEquals(pass, technique.Passes[pass.Name]));
            Observe("graphics.effect.technique.out_of_range", () => _ = effect.Techniques[-1]);
            Observe("graphics.effect.technique.null_name", () => _ = effect.Techniques[(string)null!]);
            Add("graphics.effect.parameters.repeated", ReferenceEquals(effect.Parameters, effect.Parameters));

            if (effect.Parameters.Count > 0)
            {
                EffectParameter parameter = effect.Parameters[0];
                Add("graphics.effect.parameter.repeated", ReferenceEquals(parameter, effect.Parameters[0]));
                Add("graphics.effect.parameter.by_name", ReferenceEquals(parameter, effect.Parameters[parameter.Name]));
                Observe("graphics.effect.parameter.out_of_range", () => _ = effect.Parameters[-1]);
            }
            else
            {
                Add("graphics.effect.parameter.repeated", "empty");
                Add("graphics.effect.parameter.by_name", "empty");
                Observe("graphics.effect.parameter.out_of_range", () => _ = effect.Parameters[-1]);
            }
        }

        private void CaptureDrawValidation(GraphicsDevice device)
        {
            var vertices = new[]
            {
                new VertexPositionColor(Vector3.Zero, Color.Red),
                new VertexPositionColor(Vector3.UnitX, Color.Green),
                new VertexPositionColor(Vector3.UnitY, Color.Blue),
            };
            short[] triangle = [0, 1, 2];

            Observe("graphics.draw.user.null_vertices", () =>
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList, (VertexPositionColor[])null!, 0, 1));
            Observe("graphics.draw.user.empty_vertices", () =>
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList, new VertexPositionColor[0], 0, 1));
            Observe("graphics.draw.user.negative_offset", () =>
                device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, -1, 1));
            Observe("graphics.draw.user.zero_count", () =>
                device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, 0));
            Observe("graphics.draw.user.beyond_vertices", () =>
                device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 1, 1));
            Observe("graphics.draw.user.invalid_topology", () =>
                device.DrawUserPrimitives((PrimitiveType)int.MaxValue, vertices, 0, 1));

            using var effectForCustomVertices = new BasicEffect(device);
            effectForCustomVertices.CurrentTechnique.Passes[0].Apply();
            using (var managedDeclaration = new VertexDeclaration(
                       16,
                       new VertexElement(
                           0, VertexElementFormat.Vector4, VertexElementUsage.Position, 0)))
            {
                Observe("graphics.draw.user.managed_reference", () =>
                    device.DrawUserPrimitives(
                        PrimitiveType.TriangleList,
                        new ManagedReferenceVertex[3],
                        0,
                        1,
                        managedDeclaration));
            }

            using (var mismatchedDeclaration = new VertexDeclaration(
                       16,
                       new VertexElement(
                           0, VertexElementFormat.Vector4, VertexElementUsage.Position, 0)))
            {
                Observe("graphics.draw.user.custom_wrong_stride", () =>
                    device.DrawUserPrimitives(
                        PrimitiveType.TriangleList,
                        new Vector3[4],
                        0,
                        1,
                        mismatchedDeclaration));
            }

            var disposedDeclaration = new VertexDeclaration(
                new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0));
            disposedDeclaration.Dispose();
            Observe("graphics.draw.user.disposed_declaration", () =>
                device.DrawUserPrimitives(
                    PrimitiveType.TriangleList,
                    new Vector3[3],
                    0,
                    1,
                    disposedDeclaration));

            Observe("graphics.draw.user_indexed.null_vertices", () =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, (VertexPositionColor[])null!, 0, 3,
                    triangle, 0, 1));
            Observe("graphics.draw.user_indexed.empty_vertices", () =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, new VertexPositionColor[0], 0, 3,
                    triangle, 0, 1));
            Observe("graphics.draw.user_indexed.null_indices", () =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, vertices, 0, 3, (short[])null!, 0, 1));
            Observe("graphics.draw.user_indexed.empty_indices", () =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, vertices, 0, 3, new short[0], 0, 1));
            Observe("graphics.draw.user_indexed.zero_vertices", () =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, vertices, 0, 0, triangle, 0, 1));
            Observe("graphics.draw.user_indexed.zero_count", () =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, vertices, 0, 3, triangle, 0, 0));
            Observe("graphics.draw.user_indexed.negative_vertex_offset", () =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, vertices, -1, 3, triangle, 0, 1));
            Observe("graphics.draw.user_indexed.negative_index_offset", () =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, vertices, 0, 3, triangle, -1, 1));
            Observe("graphics.draw.user_indexed.indices_too_small", () =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, vertices, 0, 3, new short[2], 0, 1));
            Observe("graphics.draw.user_indexed.vertex_window_too_large", () =>
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, vertices, 1, 3, triangle, 0, 1));
            Observe("graphics.draw.user_indexed.invalid_topology", () =>
                device.DrawUserIndexedPrimitives(
                    (PrimitiveType)int.MaxValue, vertices, 0, 3, triangle, 0, 1));

            Observe("graphics.draw.bound.zero_count", () =>
                device.DrawPrimitives(PrimitiveType.TriangleList, 0, 0));
            Observe("graphics.draw.indexed.zero_vertices", () =>
                device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 0, 0, 1));
            Observe("graphics.draw.indexed.zero_count", () =>
                device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 3, 0, 0));
            Observe("graphics.draw.instanced.zero_instances", () =>
                device.DrawInstancedPrimitives(
                    PrimitiveType.TriangleList, 0, 0, 3, 0, 1, 0));

            var disposedVertexBuffer = new VertexBuffer(
                device, VertexPositionColor.VertexDeclaration, 3, BufferUsage.None);
            disposedVertexBuffer.Dispose();
            Observe("graphics.draw.bind.disposed_vertex_buffer", () =>
                device.SetVertexBuffer(disposedVertexBuffer));

            var disposedIndexBuffer = new IndexBuffer(
                device, IndexElementSize.SixteenBits, 3, BufferUsage.None);
            disposedIndexBuffer.Dispose();
            Observe("graphics.draw.bind.disposed_index_buffer", () =>
                device.Indices = disposedIndexBuffer);

            using var effect = new BasicEffect(device);
            effect.CurrentTechnique.Passes[0].Apply();
            Observe("graphics.draw.bound.missing_vertex_buffer", () =>
                device.DrawPrimitives(PrimitiveType.TriangleList, 0, 1));

            using (var vertexBuffer = new VertexBuffer(
                       device, VertexPositionColor.VertexDeclaration, 3, BufferUsage.None))
            {
                vertexBuffer.SetData(vertices);
                device.SetVertexBuffer(vertexBuffer);
                Observe("graphics.draw.indexed.missing_indices", () =>
                    device.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList, 0, 0, 3, 0, 1));
                device.SetVertexBuffer(null);
            }

            using (var indexBuffer = new IndexBuffer(
                       device, IndexElementSize.SixteenBits, 3, BufferUsage.None))
            {
                indexBuffer.SetData(triangle);
                device.Indices = indexBuffer;
                Observe("graphics.draw.indexed.missing_vertex_buffer", () =>
                    device.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList, 0, 0, 3, 0, 1));
                device.Indices = null;
            }
        }

        private struct ManagedReferenceVertex
        {
            public string? Text { get; set; }
            public Vector3 Position { get; set; }
        }

        private void CaptureConstructorValidation(GraphicsDevice device)
        {
            Observe("graphics.validation.texture2d.null_device", () =>
            {
                using var texture = new Texture2D(null!, 1, 1);
            });
            if (_captureUnsafeConstructors)
            {
                Observe("graphics.validation.texture2d.zero_width", () =>
                {
                    using var texture = new Texture2D(device, 0, 1);
                });
                Observe("graphics.validation.texture2d.negative_height", () =>
                {
                    using var texture = new Texture2D(device, 1, -1);
                });
                Observe("graphics.validation.texture2d.invalid_format", () =>
                {
                    using var texture = new Texture2D(
                        device, 1, 1, false, (SurfaceFormat)int.MaxValue);
                });
                Observe("graphics.validation.texture3d.zero_depth", () =>
                {
                    using var texture = new Texture3D(device, 1, 1, 0, false, SurfaceFormat.Color);
                });
                Observe("graphics.validation.texturecube.zero_size", () =>
                {
                    using var texture = new TextureCube(device, 0, false, SurfaceFormat.Color);
                });
                Observe("graphics.validation.rendertarget2d.zero_width", () =>
                {
                    using var target = new RenderTarget2D(device, 0, 1);
                });
                Observe("graphics.validation.rendertarget2d.negative_multisample", () =>
                {
                    using var target = new RenderTarget2D(
                        device, 1, 1, false, SurfaceFormat.Color, DepthFormat.None,
                        -1, RenderTargetUsage.DiscardContents);
                });
                Observe("graphics.validation.rendertargetcube.zero_size", () =>
                {
                    using var target = new RenderTargetCube(
                        device, 0, false, SurfaceFormat.Color, DepthFormat.None);
                });
            }
            else
            {
                foreach (string name in new[]
                {
                    "graphics.validation.texture2d.zero_width",
                    "graphics.validation.texture2d.negative_height",
                    "graphics.validation.texture2d.invalid_format",
                    "graphics.validation.texture3d.zero_depth",
                    "graphics.validation.texturecube.zero_size",
                    "graphics.validation.rendertarget2d.zero_width",
                    "graphics.validation.rendertarget2d.negative_multisample",
                    "graphics.validation.rendertargetcube.zero_size",
                })
                {
                    Add(name, "not-run(opt-in-required)");
                }
            }
            Observe("graphics.validation.vertex_buffer.null_declaration", () =>
            {
                using var buffer = new VertexBuffer(device, (VertexDeclaration)null!, 1, BufferUsage.None);
            });
            if (_captureUnsafeConstructors)
            {
                Observe("graphics.validation.vertex_buffer.zero_count", () =>
                {
                    using var buffer = new VertexBuffer(
                        device, VertexPositionColor.VertexDeclaration, 0, BufferUsage.None);
                });
                Observe("graphics.validation.index_buffer.zero_count", () =>
                {
                    using var buffer = new IndexBuffer(
                        device, IndexElementSize.SixteenBits, 0, BufferUsage.None);
                });
            }
            else
            {
                Add("graphics.validation.vertex_buffer.zero_count", "not-run(opt-in-required)");
                Add("graphics.validation.index_buffer.zero_count", "not-run(opt-in-required)");
            }
            Observe("graphics.validation.index_buffer.byte_type", () =>
            {
                using var buffer = new IndexBuffer(device, typeof(byte), 3, BufferUsage.None);
            });
            Observe("graphics.validation.vertex_declaration.zero_stride", () =>
            {
                using var declaration = new VertexDeclaration(
                    0,
                    new VertexElement(
                        0, VertexElementFormat.Single, VertexElementUsage.Position, 0));
            });
            Observe("graphics.validation.vertex_declaration.unaligned_stride", () =>
            {
                using var declaration = new VertexDeclaration(
                    6,
                    new VertexElement(
                        0, VertexElementFormat.Single, VertexElementUsage.Position, 0));
            });
            Observe("graphics.validation.vertex_declaration.outside_stride", () =>
            {
                using var declaration = new VertexDeclaration(
                    4,
                    new VertexElement(
                        0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0));
            });

            if (_captureUnsafeConstructors)
            {
                using (var texture = new Texture2D(device, 2, 2, false, SurfaceFormat.Color))
                {
                    texture.SetData(new[] { Color.Red, Color.Green, Color.Blue, Color.White });
                    Observe("graphics.validation.texture2d.invalid_level", () =>
                        texture.GetData(1, null, new Color[4], 0, 4));
                    Observe("graphics.validation.texture2d.rect_outside", () =>
                        texture.GetData(0, new Rectangle(1, 1, 2, 2), new Color[4], 0, 4));
                    Observe("graphics.validation.texture2d.array_too_small", () =>
                        texture.GetData(0, null, new Color[3], 0, 3));
                    Observe("graphics.validation.texture2d.start_index_overflow", () =>
                        texture.SetData(0, null, new Color[4], 4, 1));
                }

                using (var cube = new TextureCube(device, 1, false, SurfaceFormat.Color))
                {
                    Observe("graphics.validation.texturecube.invalid_face", () =>
                        cube.SetData((CubeMapFace)int.MaxValue, new[] { Color.White }));
                    Observe("graphics.validation.texturecube.invalid_level", () =>
                        cube.GetData(CubeMapFace.PositiveX, 1, null, new Color[1], 0, 1));
                }

                using (var volume = new Texture3D(device, 1, 1, 1, false, SurfaceFormat.Color))
                {
                    Observe("graphics.validation.texture3d.invalid_box", () =>
                        volume.SetData(0, 0, 0, 2, 1, 0, 1, new[] { Color.White }, 0, 1));
                }
            }
            else
            {
                foreach (string name in new[]
                {
                    "graphics.validation.texture2d.invalid_level",
                    "graphics.validation.texture2d.rect_outside",
                    "graphics.validation.texture2d.array_too_small",
                    "graphics.validation.texture2d.start_index_overflow",
                    "graphics.validation.texturecube.invalid_face",
                    "graphics.validation.texturecube.invalid_level",
                    "graphics.validation.texture3d.invalid_box",
                })
                {
                    Add(name, "not-run(opt-in-required)");
                }
            }
        }

        private void CaptureResourceLifecycle(GraphicsDevice device)
        {
            var state = new BlendState { Name = "lifecycle", Tag = "tag" };
            int eventCount = 0;
            bool disposedInside = false;
            state.Disposing += (_, _) =>
            {
                eventCount++;
                disposedInside = state.IsDisposed;
                throw new InvalidOperationException("handler");
            };

            Observe("resource.dispose.handler_exception", state.Dispose);
            Add("resource.dispose.disposed_inside", disposedInside);
            Add("resource.dispose.event_count", eventCount);
            Observe("resource.dispose.second", state.Dispose);
            Add("resource.dispose.name_tag_after", $"{state.Name}/{state.Tag}/{state.IsDisposed}");

            var texture = new Texture2D(device, 1, 1) { Name = "disposed-texture", Tag = "tag" };
            texture.Dispose();
            Observe("resource.texture.width_after_dispose", () => _ = texture.Width);
            Add("resource.texture.name_tag_after_dispose", $"{texture.Name}/{texture.Tag}/{texture.IsDisposed}");

            WeakReference abandoned = AllocateAbandonedTexture(device);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            Add("resource.texture.finalized", !abandoned.IsAlive);
        }

        private void CaptureResourceEvents(GraphicsDevice device)
        {
            int createdCount = 0;
            int destroyedCount = 0;
            bool createdSender = false;
            string createdType = "none";
            bool destroyedSender = false;
            string destroyedPayload = "none";

            EventHandler<ResourceCreatedEventArgs> created = (sender, args) =>
            {
                createdCount++;
                createdSender |= ReferenceEquals(sender, device);
                createdType = args.Resource?.GetType().Name ?? "null";
            };
            EventHandler<ResourceDestroyedEventArgs> destroyed = (sender, args) =>
            {
                destroyedCount++;
                destroyedSender |= ReferenceEquals(sender, device);
                destroyedPayload = $"{args.Name}/{args.Tag}";
            };

            device.ResourceCreated += created;
            device.ResourceDestroyed += destroyed;
            try
            {
                var texture = new Texture2D(device, 1, 1) { Name = "event-texture", Tag = "tag" };
                texture.Dispose();
            }
            finally
            {
                device.ResourceCreated -= created;
                device.ResourceDestroyed -= destroyed;
            }

            Add("graphics.events.resource.count", $"{createdCount}/{destroyedCount}");
            Add("graphics.events.resource.created_sender", createdSender);
            Add("graphics.events.resource.created_type", createdType);
            Add("graphics.events.resource.destroyed_sender", destroyedSender);
            Add("graphics.events.resource.destroyed_payload", destroyedPayload);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference AllocateAbandonedTexture(GraphicsDevice device)
        {
            var texture = new Texture2D(device, 1, 1);
            texture.SetData(new[] { Color.White });
            return new WeakReference(texture);
        }

        private void Observe(string name, Action action)
        {
            Add(name, CaptureOutcome(action));
        }

        private static string CaptureOutcome(Action action)
        {
            try
            {
                action();
                return "ok";
            }
            catch (Exception exception)
            {
                if (exception is TargetInvocationException { InnerException: { } inner })
                {
                    exception = inner;
                }

                string value = exception.GetType().Name;
                if (exception is ArgumentException argument && argument.ParamName is not null)
                {
                    value += $"(param={argument.ParamName})";
                }

                return value;
            }
        }

        private void Add(string name, object? value)
        {
            string normalized = value switch
            {
                null => "null",
                bool boolean => boolean ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "null",
            };
            string observation = $"{name}={normalized}";
            Observations.Add(observation);
            if (_trace)
            {
                Console.Error.WriteLine(observation);
            }
        }

        private static void InvokePresentWithSourceRectangle(GraphicsDevice device)
        {
            MethodInfo? present = typeof(GraphicsDevice).GetMethod(
                "Present",
                [typeof(Rectangle?), typeof(Rectangle?), typeof(IntPtr)]);
            if (present is null)
            {
                throw new MissingMethodException(typeof(GraphicsDevice).FullName, "Present(Rectangle?, Rectangle?, IntPtr)");
            }

            present.Invoke(device, [new Rectangle(0, 0, 1, 1), null, IntPtr.Zero]);
        }
    }
}
