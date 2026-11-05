#if CNA_HOST_DRIVEN_FRAMES
using CNA.XnaCompat.Extensions;
#endif
using System.Globalization;
using System.Reflection;
using CNA.BehaviorProbes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;
using Microsoft.Xna.Framework.Storage;

namespace XnaRuntimeBehaviorProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        LoadXnaRuntimeAssembliesIfRequested();
        using var game = new ProbeGame();
        for (int frame = 0; frame < 8 && !game.Complete; frame++)
        {
#if CNA_HOST_DRIVEN_FRAMES
            game.RunFrameEXT();
#else
            game.RunOneFrame();
#endif
        }

        if (game.Failure is not null)
        {
            Console.Error.WriteLine(game.Failure);
            return 1;
        }

        if (!game.Complete)
        {
            Console.Error.WriteLine("The runtime probe did not complete.");
            return 1;
        }

        ProbeOutput.Write(args, game.Observations);

        game.Dispose();
        return 0;
    }

    private static void LoadXnaRuntimeAssembliesIfRequested()
    {
        string? directory = Environment.GetEnvironmentVariable("XNA_RUNTIME_PATH");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        foreach (string name in new[]
        {
            "Microsoft.Xna.Framework.dll",
            "Microsoft.Xna.Framework.Game.dll",
            "Microsoft.Xna.Framework.Graphics.dll",
            "Microsoft.Xna.Framework.Storage.dll",
            "Microsoft.Xna.Framework.Video.dll",
            "Microsoft.Xna.Framework.Xact.dll",
        })
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                Assembly.LoadFrom(path);
            }
        }
    }

    private sealed class ProbeGame : Game
    {
        private const string ContainerName = "cna-dotnet-runtime-probe";
        private readonly GraphicsDeviceManager _graphics;
        private DynamicSoundEffectInstance? _normalPump;
        private int _normalPumpEvents;
        private int _frame;

        public ProbeGame()
        {
            _graphics = new GraphicsDeviceManager(this);
        }

        public bool Complete { get; private set; }

        public Exception? Failure { get; private set; }

        public List<string> Observations { get; } = new();

        protected override void Update(GameTime gameTime)
        {
            try
            {
                if (_frame == 0)
                {
                    Trace("audio");
                    CaptureAudioAndStartNormalPump();
                    Trace("xact");
                    CaptureXact();
                    Trace("media");
                    CaptureMedia();
                    Trace("video");
                    CaptureVideo();
                    Trace("storage");
                    CaptureStorage();
                    Trace("device-lifecycle");
                    CaptureDeviceLifecycle();
                    Trace("frame-0-complete");
                    _frame++;
                    base.Update(gameTime);
                    return;
                }

                if (_frame == 1)
                {
                    Trace("frame-1");
                    Add("audio.pump.game_update.events", _normalPumpEvents);
                    Add("audio.pump.game_update.pending", _normalPump!.PendingBufferCount);
                    _normalPump.Stop();
                    _normalPump.Dispose();
                    _normalPump = null;
                    CaptureDirectDispatcherPump();
                    Complete = true;
                    Exit();
                    _frame++;
                }

                base.Update(gameTime);
            }
            catch (Exception exception)
            {
                Failure = exception;
                Complete = true;
                Exit();
            }
        }

        private void CaptureAudioAndStartNormalPump()
        {
            byte[] pcm = new byte[16_000];
            using (var effect = new SoundEffect(pcm, 8000, AudioChannels.Mono))
            {
                Add("audio.effect.duration", effect.Duration.Ticks);
                Add("audio.effect.disposed.initial", effect.IsDisposed);
                using SoundEffectInstance instance = effect.CreateInstance();
                Add("audio.instance.initial", $"{instance.State}/{Bits(instance.Volume)}/{Bits(instance.Pitch)}/{Bits(instance.Pan)}/{instance.IsLooped}");
                instance.Volume = 0.25f;
                instance.Pitch = -0.5f;
                instance.Pan = 0.75f;
                instance.IsLooped = true;
                Add("audio.instance.settings", $"{Bits(instance.Volume)}/{Bits(instance.Pitch)}/{Bits(instance.Pan)}/{instance.IsLooped}");
                Observe("audio.instance.volume.low", () => instance.Volume = -0.01f);
                Observe("audio.instance.volume.nan", () => instance.Volume = float.NaN);
                Observe("audio.instance.pitch.high", () => instance.Pitch = 1.01f);
                Observe("audio.instance.pan.high", () => instance.Pan = 1.01f);
                Observe("audio.apply3d.listener.null", () => instance.Apply3D((AudioListener)null!, new AudioEmitter()));
                Observe("audio.apply3d.emitter.null", () => instance.Apply3D(new AudioListener(), null!));
                Observe("audio.apply3d.array.null", () => instance.Apply3D((AudioListener[])null!, new AudioEmitter()));
                Observe("audio.apply3d.array.empty", () => instance.Apply3D(Array.Empty<AudioListener>(), new AudioEmitter()));
                Observe("audio.apply3d.array.element_null", () => instance.Apply3D(new AudioListener[] { null! }, new AudioEmitter()));
                Observe("audio.apply3d.array.one", () => instance.Apply3D(new[] { new AudioListener() }, new AudioEmitter()));
                Observe("audio.apply3d.array.multiple", () => instance.Apply3D(
                    new[] { new AudioListener(), new AudioListener() }, new AudioEmitter()));

                var transitions = new List<SoundState>();
                instance.Play();
                transitions.Add(instance.State);
                instance.Pause();
                transitions.Add(instance.State);
                instance.Resume();
                transitions.Add(instance.State);
                instance.Stop(immediate: false);
                transitions.Add(instance.State);
                instance.Play();
                instance.Stop();
                transitions.Add(instance.State);
                Add("audio.instance.transitions", string.Join(",", transitions));

                instance.Dispose();
                instance.Dispose();
                Add("audio.instance.double_dispose", instance.IsDisposed);
                Add("audio.instance.cached_after_dispose",
                    $"{Bits(instance.Volume)}/{Bits(instance.Pitch)}/{Bits(instance.Pan)}/{instance.IsLooped}");
                Observe("audio.instance.state_after_dispose", () => _ = instance.State);
                Observe("audio.instance.play_after_dispose", instance.Play);
            }

            var parent = new SoundEffect(pcm, 8000, AudioChannels.Mono);
            SoundEffectInstance child = parent.CreateInstance();
            parent.Dispose();
            Add("audio.instance.parent_dispose", child.IsDisposed);
            Observe("audio.effect.play_after_dispose", () => _ = parent.Play());
            Observe("audio.effect.create_after_dispose", () => _ = parent.CreateInstance());
            child.Dispose();
            parent.Dispose();
            Add("audio.effect.double_dispose", parent.IsDisposed);

            _normalPump = new DynamicSoundEffectInstance(8000, AudioChannels.Mono);
            _normalPump.SubmitBuffer(pcm);
            _normalPump.BufferNeeded += (_, _) => _normalPumpEvents++;
            _normalPump.Play();
            Add("audio.dynamic.pending.before_pump", _normalPump.PendingBufferCount);
        }

        private void CaptureDirectDispatcherPump()
        {
            byte[] pcm = new byte[16_000];
            using (var dynamic = new DynamicSoundEffectInstance(8000, AudioChannels.Mono))
            {
                int events = 0;
                dynamic.SubmitBuffer(pcm);
                dynamic.BufferNeeded += (_, _) => events++;
                dynamic.Play();
                FrameworkDispatcher.Update();
                Add("audio.pump.direct.events", events);
                Add("audio.pump.direct.pending", dynamic.PendingBufferCount);
                dynamic.Stop();
            }

            using (var dynamic = new DynamicSoundEffectInstance(8000, AudioChannels.Mono))
            {
                int events = 0;
                dynamic.BufferNeeded += (_, _) => events++;
                dynamic.SubmitBuffer(pcm);
                dynamic.SubmitBuffer(pcm);
                Add("audio.dynamic.multiple.pending_before", dynamic.PendingBufferCount);
                dynamic.Play();
                dynamic.Pause();
                dynamic.Resume();
                Add("audio.dynamic.transitions", dynamic.State);
                FrameworkDispatcher.Update();
                Add("audio.dynamic.multiple.after_pump", $"{events}/{dynamic.PendingBufferCount}");
                dynamic.Stop();
            }

            using (var dynamic = new DynamicSoundEffectInstance(8000, AudioChannels.Mono))
            {
                int events = 0;
                bool submittedFromHandler = false;
                dynamic.BufferNeeded += (_, _) =>
                {
                    events++;
                    if (!submittedFromHandler)
                    {
                        submittedFromHandler = true;
                        dynamic.SubmitBuffer(pcm);
                    }
                };
                dynamic.SubmitBuffer(pcm);
                dynamic.Play();
                FrameworkDispatcher.Update();
                Add("audio.dynamic.reentrant_submit", $"{events}/{submittedFromHandler}/{dynamic.PendingBufferCount}");
                dynamic.Stop();
            }

            using (var dynamic = new DynamicSoundEffectInstance(8000, AudioChannels.Mono))
            {
                int events = 0;
                EventHandler<EventArgs>? handler = null;
                handler = (_, _) =>
                {
                    events++;
                    dynamic.BufferNeeded -= handler;
                };
                dynamic.BufferNeeded += handler;
                dynamic.SubmitBuffer(pcm);
                dynamic.Play();
                FrameworkDispatcher.Update();
                Add("audio.dynamic.self_unsubscribe", events);
                dynamic.Stop();
            }

            var throwing = new DynamicSoundEffectInstance(8000, AudioChannels.Mono);
            throwing.BufferNeeded += (_, _) => throw new InvalidOperationException("probe-handler");
            throwing.SubmitBuffer(pcm);
            throwing.Play();
            Observe("audio.dynamic.handler_throw.pump", FrameworkDispatcher.Update);
            Observe("audio.dynamic.handler_throw.dispose", throwing.Dispose);
        }

        private void CaptureXact()
        {
            Observe("xact.engine.settings_null", () => _ = new AudioEngine(null!));
            Observe("xact.engine.settings_empty", () => _ = new AudioEngine(string.Empty));
            Observe("xact.engine.settings_missing", () => _ = new AudioEngine("__cna_missing__.xgs"));
            Observe("xact.engine.settings_missing_lookahead", () =>
                _ = new AudioEngine("__cna_missing__.xgs", TimeSpan.FromMilliseconds(250), "__missing_renderer__"));

            string temporary = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(temporary, [0x58, 0x47, 0x53, 0x46]);
                Observe("xact.engine.settings_short", () => _ = new AudioEngine(temporary));
                File.WriteAllBytes(temporary, [0x00, 0x01, 0x02, 0x03, 0x04]);
                Observe("xact.engine.settings_signature", () => _ = new AudioEngine(temporary));
            }
            finally
            {
                File.Delete(temporary);
            }

            Add("xact.authored_banks", "not-run(no-redistributable-authored-bank-fixture)");
        }

        private void CaptureMedia()
        {
            Add("media.initial.state", MediaPlayer.State);
            Add("media.initial.position", MediaPlayer.PlayPosition.Ticks);
            Add("media.initial.flags", $"{MediaPlayer.IsMuted}/{MediaPlayer.IsRepeating}/{MediaPlayer.IsShuffled}");
            MediaQueue queue = MediaPlayer.Queue;
            Add("media.queue.identity", ReferenceEquals(queue, MediaPlayer.Queue));
            Add("media.queue.empty", $"{queue.Count}/{queue.ActiveSongIndex}/{(queue.ActiveSong is null)}");
            Observe("media.queue.empty.index", () => _ = queue[0]);
            Observe("media.play.null", () => MediaPlayer.Play((Song)null!));
            Observe("media.pause.stopped", MediaPlayer.Pause);
            Observe("media.resume.stopped", MediaPlayer.Resume);
            Observe("media.stop.stopped", MediaPlayer.Stop);
            Observe("media.move_next.empty", MediaPlayer.MoveNext);
            Observe("media.move_previous.empty", MediaPlayer.MovePrevious);

            var visualization = new VisualizationData();
            MediaPlayer.GetVisualizationData(visualization);
            Add("media.visualization.disabled",
                $"{MediaPlayer.IsVisualizationEnabled}/{visualization.Frequencies.All(value => value == 0f)}/{visualization.Samples.All(value => value == 0f)}");

            MediaPlayer.Volume = -1f;
            Add("media.volume.low", Bits(MediaPlayer.Volume));
            MediaPlayer.Volume = 2f;
            Add("media.volume.high", Bits(MediaPlayer.Volume));
            MediaPlayer.Volume = 1f;
            MediaPlayer.IsMuted = true;
            MediaPlayer.IsRepeating = true;
            MediaPlayer.IsShuffled = true;
            Add("media.flags.true", $"{MediaPlayer.IsMuted}/{MediaPlayer.IsRepeating}/{MediaPlayer.IsShuffled}");
            MediaPlayer.IsMuted = false;
            MediaPlayer.IsRepeating = false;
            MediaPlayer.IsShuffled = false;

            using var library = new MediaLibrary();
            Add("media.library.collection_identity", $"{ReferenceEquals(library.Songs, library.Songs)}/{ReferenceEquals(library.Albums, library.Albums)}");
            Add("media.library.counts", $"{library.Songs.Count}/{library.Albums.Count}/{library.Artists.Count}/{library.Genres.Count}/{library.Playlists.Count}");
            Observe("media.library.songs.index", () => _ = library.Songs[0]);
            Add("media.events", "not-run(no-redistributable-song-fixture)");
        }

        private void CaptureVideo()
        {
            var player = new VideoPlayer();
            Add("video.initial.state", player.State);
            Add("video.initial.video", player.Video is null);
            Add("video.initial.settings", $"{player.IsLooped}/{player.IsMuted}/{Bits(player.Volume)}/{player.PlayPosition.Ticks}");
            Observe("video.texture.before_play", () => _ = player.GetTexture());
            Observe("video.pause.stopped", player.Pause);
            Observe("video.resume.stopped", player.Resume);
            Observe("video.stop.stopped", player.Stop);
            Observe("video.play.null", () => player.Play(null!));
            Observe("video.volume.low", () => player.Volume = -0.01f);
            Observe("video.volume.high", () => player.Volume = 1.01f);
            Observe("video.volume.nan", () => player.Volume = float.NaN);
            player.IsLooped = true;
            player.IsMuted = true;
            player.Volume = 0.25f;
            Add("video.settings.roundtrip", $"{player.IsLooped}/{player.IsMuted}/{Bits(player.Volume)}");
            player.Dispose();
            player.Dispose();
            Add("video.double_dispose", player.IsDisposed);
            Add("video.cached.after_dispose", $"{player.IsLooped}/{player.IsMuted}/{Bits(player.Volume)}");
            Observe("video.state.after_dispose", () => _ = player.State);
            Observe("video.pause.after_dispose", player.Pause);
            Add("video.frame_identity", "not-run(no-redistributable-video-fixture)");
        }

        private void CaptureStorage()
        {
            bool selectorCallback = false;
            IAsyncResult selector = StorageDevice.BeginShowSelector(_ => selectorCallback = true, "selector-state");
            Add("storage.selector.completion", $"{selector.IsCompleted}/{selector.CompletedSynchronously}/{selectorCallback}/{selector.AsyncState}");
            StorageDevice device = StorageDevice.EndShowSelector(selector);
            Add("storage.device.connected", device.IsConnected);
            Add("storage.device.capacity", $"{(device.FreeSpace >= 0)}/{(device.TotalSpace >= 0)}/{(device.TotalSpace >= device.FreeSpace)}");
            if (!device.IsConnected)
            {
                foreach (string name in new[]
                {
                    "storage.container.preclean",
                    "storage.container.null_name",
                    "storage.container.empty_name",
                    "storage.open.completion",
                    "storage.container.display",
                    "storage.container.device_identity",
                    "storage.container.reopen",
                    "storage.path.normalization",
                    "storage.directory.exists",
                    "storage.directory.names",
                    "storage.file.exists",
                    "storage.file.names",
                    "storage.file.read",
                    "storage.delete",
                    "storage.container.double_dispose",
                    "storage.container.after_dispose",
                    "storage.container.cleanup",
                })
                {
                    Add(name, "not-run(device-disconnected)");
                }
                return;
            }

            try
            {
                Observe("storage.container.preclean", () => device.DeleteContainer(ContainerName));
                Observe("storage.container.null_name", () => _ = device.BeginOpenContainer(null!, null, null));
                Observe("storage.container.empty_name", () =>
                {
                    IAsyncResult emptyOpening = device.BeginOpenContainer(string.Empty, null, null);
                    using StorageContainer empty = device.EndOpenContainer(emptyOpening);
                });
                bool openCallback = false;
                IAsyncResult opening = device.BeginOpenContainer(ContainerName, _ => openCallback = true, "open-state");
                Add("storage.open.completion", $"{opening.IsCompleted}/{opening.CompletedSynchronously}/{openCallback}/{opening.AsyncState}");
                using StorageContainer container = device.EndOpenContainer(opening);
                Add("storage.container.display", container.DisplayName);
                Add("storage.container.device_identity", ReferenceEquals(device, container.StorageDevice));
                IAsyncResult reopening = device.BeginOpenContainer(ContainerName, null, null);
                using (StorageContainer reopened = device.EndOpenContainer(reopening))
                {
                    Add("storage.container.reopen",
                        $"{reopened.DisplayName}/{ReferenceEquals(device, reopened.StorageDevice)}/{ReferenceEquals(container, reopened)}");
                }

                bool normalizedPathExists = false;
                string normalizedPathOutcome = CaptureOutcome(() =>
                {
                    container.CreateDirectory("nested/./child");
                    normalizedPathExists = container.DirectoryExists("nested/child");
                });
                Add("storage.path.normalization", $"{normalizedPathOutcome}/{normalizedPathExists}");
                if (normalizedPathExists)
                {
                    container.DeleteDirectory("nested/child");
                    container.DeleteDirectory("nested");
                }

                container.CreateDirectory("b");
                container.CreateDirectory("a");
                Add("storage.directory.exists", $"{container.DirectoryExists("a")}/{container.DirectoryExists("missing")}");
                Add("storage.directory.names", string.Join(",", container.GetDirectoryNames().OrderBy(name => name, StringComparer.Ordinal)));
                using (Stream stream = container.CreateFile("save.bin"))
                {
                    stream.WriteByte(42);
                }

                Add("storage.file.exists", $"{container.FileExists("save.bin")}/{container.FileExists("missing.bin")}");
                Add("storage.file.names", string.Join(",", container.GetFileNames().OrderBy(name => name, StringComparer.Ordinal)));
                using (Stream stream = container.OpenFile("save.bin", FileMode.Open, FileAccess.Read))
                {
                    Add("storage.file.read", stream.ReadByte());
                }

                container.DeleteFile("save.bin");
                container.DeleteDirectory("a");
                container.DeleteDirectory("b");
                Add("storage.delete", $"{container.FileExists("save.bin")}/{container.DirectoryExists("a")}");
                container.Dispose();
                container.Dispose();
                Add("storage.container.double_dispose", container.IsDisposed);
                Observe("storage.container.after_dispose", () => container.FileExists("save.bin"));
            }
            finally
            {
                Observe("storage.container.cleanup", () => device.DeleteContainer(ContainerName));
            }
        }

        private void CaptureDeviceLifecycle()
        {
            GraphicsDevice device = GraphicsDevice;
            var order = new List<string>();
            int resetting = 0;
            int reset = 0;
            int lost = 0;
            bool sender = true;
            EventHandler<EventArgs> onResetting = (value, _) =>
            {
                resetting++;
                sender &= ReferenceEquals(value, device);
                order.Add("resetting");
            };
            EventHandler<EventArgs> onReset = (value, _) =>
            {
                reset++;
                sender &= ReferenceEquals(value, device);
                order.Add("reset");
            };
            EventHandler<EventArgs> onLost = (value, _) =>
            {
                lost++;
                sender &= ReferenceEquals(value, device);
                order.Add("lost");
            };
            device.DeviceResetting += onResetting;
            device.DeviceReset += onReset;
            device.DeviceLost += onLost;
            try
            {
                Observe("devicelifecycle.reset.outcome", device.Reset);
            }
            finally
            {
                device.DeviceResetting -= onResetting;
                device.DeviceReset -= onReset;
                device.DeviceLost -= onLost;
            }

            Add("devicelifecycle.reset.events", $"{resetting}/{reset}/{lost}/{sender}/{string.Join(",", order)}");
            CaptureCrossDevice(device);
            Add("devicelifecycle.device_lost", "not-run(no-deterministic-loss-route)");
        }

        /// <summary>
        /// Two live devices, and a resource from one used on the other.
        ///
        /// This recorded <c>not-run(CNA-ABI-has-one-game-owned-device)</c> until CNA 0.19.0 added
        /// <c>cna_graphics_device_create</c>, and then <c>not-run(destroys-the-game-gl-context)</c>
        /// until 0.21.0: the create made its own GL context current and did not restore the game's,
        /// so on the OPENGLES3 backend the game's next frame died in <c>SwapBuffers</c> with "the
        /// specified window has not been made current". Upstream's own owned-device smoke test
        /// never has a Game in the process, so nothing there covered the mixture the header
        /// describes.
        ///
        /// <b>It runs now, and the <c>CNA_RUNTIME_PROBE_CROSS_DEVICE</c> gate is gone with the
        /// reason for it.</b> Against ABI 0.21.0 both keys report <c>ok</c>, and -- the part that
        /// mattered, since one destroyed frame would have invalidated every observation after it --
        /// the same process still produces all 105 observations afterwards. Two placeholders that
        /// stood through eleven ABI generations are measurements again.
        /// </summary>
        private void CaptureCrossDevice(GraphicsDevice gameDevice)
        {
            GraphicsDevice? second = null;
            try
            {
                second = new GraphicsDevice(
                    GraphicsAdapter.DefaultAdapter,
                    gameDevice.GraphicsProfile,
                    gameDevice.PresentationParameters);
            }
            catch (Exception exception)
            {
                Add("devicelifecycle.cross_device.create", exception.GetType().Name);
                Add("devicelifecycle.cross_device", $"not-run(second-device-create:{exception.GetType().Name})");
                return;
            }

            try
            {
                Add("devicelifecycle.cross_device.create", "ok");

                using var ownedByFirst = new Texture2D(gameDevice, 1, 1);
                ownedByFirst.SetData(new[] { Color.White });

                Observe("devicelifecycle.cross_device", () =>
                {
                    second.Textures[0] = ownedByFirst;
                    second.Textures[0] = null;
                });
            }
            finally
            {
                second.Dispose();
            }
        }

        private void Observe(string name, Action action) => Add(name, CaptureOutcome(action));

        private static string CaptureOutcome(Action action)
        {
            try
            {
                action();
                return "ok";
            }
            catch (Exception exception)
            {
                string result = exception.GetType().Name;
                if (exception is ArgumentException argument && argument.ParamName is not null)
                {
                    result += "(param=" + argument.ParamName + ")";
                }

                return result;
            }
        }

        private static string Bits(float value) =>
            unchecked((uint)BitConverter.ToInt32(BitConverter.GetBytes(value), 0))
                .ToString("X8", CultureInfo.InvariantCulture);

        private void Add(string name, object? value)
        {
            string normalized = value switch
            {
                null => "null",
                bool flag => flag ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "null",
            };
            Observations.Add(name + "=" + normalized);
        }

        private static void Trace(string stage)
        {
            if (Environment.GetEnvironmentVariable("XNA_RUNTIME_PROBE_TRACE") == "1")
            {
                Console.Error.WriteLine("runtime-probe:" + stage);
            }
        }
    }
}
