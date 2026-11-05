using CNA.XnaCompat.Extensions;
using System.Text;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Xunit;
using XnaGame = Microsoft.Xna.Framework.Game;

// NOT under CNA, for the reason CompatLayerIntegrationTests records: an enclosing namespace's
// members shadow a using directive's imports, so inside `namespace CNA.*` the names below would
// bind to CNA's own types rather than the compat ones under test.
namespace CnaDotnet.Integration.Tests.Compat;

/// <summary>
/// Two things a real game depends on that no unit test can reach: the window's default title, which
/// needs a real window, and the managed <c>SoundEffectReader</c>, which builds a
/// <c>SoundEffect</c> and so needs a native game to build it against.
/// </summary>
[Collection(global::CNA.Integration.Tests.OwnGameCollection.Name)]
public class CompatWindowAndAudioContentTests
{
    private sealed class TitleProbe : XnaGame
    {
        public string? TitleSeenInInitialize { get; private set; }

        public string? TitleAssignedByTheGame { get; init; }

        public string? TitleAfterAFrame { get; private set; }

        public TitleProbe(string? assign = null)
        {
            TitleAssignedByTheGame = assign;
            if (assign is not null)
            {
                Window.Title = assign;
            }
        }

        protected override void Initialize()
        {
            TitleSeenInInitialize = Window.Title;
            base.Initialize();
        }

        protected override void Update(Microsoft.Xna.Framework.GameTime gameTime)
        {
            TitleAfterAFrame = Window.Title;
            Exit();
            base.Update(gameTime);
        }
    }

    /// <summary>
    /// XNA names the window before the game's <c>Initialize</c> runs, and almost no game sets a
    /// title itself -- Speedy Blupi does not, and its window came up with an empty title, which is
    /// what surfaced this. An untitled window is not only wrong-looking: window managers, taskbars
    /// and automation tools all address a window by its name.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void Window_IsNamedBeforeInitialize_WhenTheGameSetsNoTitle()
    {
        using var game = new TitleProbe();

        game.RunFrameEXT();

        Assert.False(
            string.IsNullOrEmpty(game.TitleSeenInInitialize),
            "The window still had no title by the time Initialize ran.");
        Assert.Equal(game.TitleSeenInInitialize, game.TitleAfterAFrame);
    }

    /// <summary>
    /// The default must not overwrite a title the game chose. A game that sets it in its
    /// constructor does so before the window exists, so the default -- which arrives later -- is
    /// the one that has to yield.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void Window_KeepsATitleTheGameSetItself()
    {
        using var game = new TitleProbe("Chosen By The Game");

        game.RunFrameEXT();

        Assert.Equal("Chosen By The Game", game.TitleSeenInInitialize);
        Assert.Equal("Chosen By The Game", game.TitleAfterAFrame);
    }

    private sealed class SoundProbe(Action<SoundProbe> body) : XnaGame
    {
        public Exception? Failure { get; private set; }

        public bool Ran { get; private set; }

        protected override void Update(Microsoft.Xna.Framework.GameTime gameTime)
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
    /// A sound nested inside another asset -- a game's own audio bank, a settings type holding its
    /// cues -- reaches the managed reader, because only a top-level <c>Load&lt;SoundEffect&gt;</c>
    /// is routed to CNA's own loader. A <c>List&lt;SoundEffect&gt;</c> is the smallest asset shaped
    /// like that.
    ///
    /// The duration in the file is deliberately wrong here. It is the pipeline's own measurement of
    /// the same PCM, and preferring it would hide exactly the mistake worth catching: a reader that
    /// took the header at its word would report a plausible duration for a buffer it had truncated.
    /// The assertion is that the data wins.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void NestedSoundEffect_ReadsThePcmRatherThanTheDeclaredDuration()
    {
        const int sampleRate = 22050;
        const int sampleCount = 11025;   // exactly half a second

        RunInAFrame(game =>
        {
            WithLoaded<List<SoundEffect>>(
                game,
                [
                    "Microsoft.Xna.Framework.Content.ListReader`1[[Microsoft.Xna.Framework.Audio.SoundEffect" + Xna + "]]" + Xna,
                    "Microsoft.Xna.Framework.Content.SoundEffectReader" + Xna,
                ],
                writer =>
                {
                    writer.Write7BitEncodedInt(1);
                    writer.Write(1);                    // one element
                    writer.Write7BitEncodedInt(2);      // a reference element carries its reader index
                    WriteWaveFormat(writer, formatTag: 1, channels: 1, sampleRate, bitsPerSample: 16);
                    writer.Write(sampleCount * 2);
                    writer.Write(new byte[sampleCount * 2]);
                    writer.Write(0);
                    writer.Write(sampleCount);
                    writer.Write(9999);                 // a duration the data contradicts
                },
                sounds =>
                {
                    SoundEffect only = Assert.Single(sounds);
                    Assert.InRange(
                        only.Duration,
                        TimeSpan.FromMilliseconds(499),
                        TimeSpan.FromMilliseconds(501));
                });
        });
    }

    /// <summary>
    /// A format this reader cannot represent is refused by name. Reinterpreting ADPCM as PCM
    /// produces noise that sounds like a decoder fault somewhere else entirely, which is the most
    /// expensive possible way to report this.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void NestedSoundEffect_RefusesAFormatItCannotRepresent()
    {
        RunInAFrame(game =>
        {
            ContentLoadException thrown = Assert.Throws<ContentLoadException>(() => WithLoaded<List<SoundEffect>>(
                game,
                [
                    "Microsoft.Xna.Framework.Content.ListReader`1[[Microsoft.Xna.Framework.Audio.SoundEffect" + Xna + "]]" + Xna,
                    "Microsoft.Xna.Framework.Content.SoundEffectReader" + Xna,
                ],
                writer =>
                {
                    writer.Write7BitEncodedInt(1);
                    writer.Write(1);
                    writer.Write7BitEncodedInt(2);
                    WriteWaveFormat(writer, formatTag: 2, channels: 1, 22050, bitsPerSample: 4);
                    writer.Write(4);
                    writer.Write(new byte[4]);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(1);
                },
                _ => { }));

            Assert.Contains("wave format 2", thrown.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A material reaches its reader at all.
    ///
    /// Eleven assets in the cna-samples corpus name <c>EffectMaterialReader</c>, and until now the
    /// reader table had none -- an asset naming it failed at resolution, before a single byte of the
    /// material was read. Proving the reader is *selected* needs no real compiled effect: the asset
    /// here names an external reference that does not exist, so the failure has to be about that
    /// missing effect. A complaint about the reader table instead would mean resolution never got
    /// as far as running the reader.
    ///
    /// What this deliberately does not prove is that a real material's parameters land correctly.
    /// That needs a pipeline-built effect, which this repository cannot produce; the parameter rule
    /// is transcribed from the decompiled reader and reasoned about in its own comment instead.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void EffectMaterial_ReachesItsReader()
    {
        RunInAFrame(game =>
        {
            ContentLoadException thrown = Assert.Throws<ContentLoadException>(() => WithLoaded<object>(
                game,
                ["Microsoft.Xna.Framework.Content.EffectMaterialReader" + Xna],
                writer =>
                {
                    writer.Write7BitEncodedInt(1);
                    writer.Write("effects/no-such-effect");   // the external reference
                },
                _ => { }));

            Assert.DoesNotContain("Could not find ContentTypeReader", thrown.ToString(), StringComparison.Ordinal);

            // The control, and it earned its place: the phrase first asserted on appears nowhere in
            // this path, so the assertion above passed for any exception at all and would have kept
            // passing with the reader removed again. This pins down what an unresolved reader
            // actually says, so the assertion above is about resolution and not about luck.
            ContentLoadException unresolved = Assert.Throws<ContentLoadException>(() => WithLoaded<object>(
                game,
                ["Microsoft.Xna.Framework.Content.NoSuchReader" + Xna],
                writer => writer.Write7BitEncodedInt(1),
                _ => { }));

            Assert.Contains("Could not find ContentTypeReader", unresolved.ToString(), StringComparison.Ordinal);
        });
    }

    private const string Xna =
        ", Microsoft.Xna.Framework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553";

    private static void RunInAFrame(Action<SoundProbe> body)
    {
        using var game = new SoundProbe(body);
        game.RunFrameEXT();

        Assert.True(game.Ran, "The frame never ran, so nothing was exercised.");
        if (game.Failure is { } failure)
        {
            throw new Xunit.Sdk.XunitException($"The body threw inside the frame: {failure}");
        }
    }

    /// <summary>
    /// Loads the asset and hands it to <paramref name="assert"/> while its <c>ContentManager</c> is
    /// still alive.
    ///
    /// Returning the asset instead would not work: disposing a ContentManager unloads what it
    /// loaded, and a SoundEffect's native handle dies with it. The assertion would then fail on an
    /// invalid handle -- which looks exactly like a reader that produced a broken object, and is
    /// the more expensive of the two to chase.
    /// </summary>
    private static void WithLoaded<T>(
        XnaGame game, string[] readerNames, Action<BinaryWriter> writeBody, Action<T> assert)
    {
        using var content = new MemoryContentManager(game.Services, BuildAsset(readerNames, writeBody));
        assert(content.Load<T>("asset"));
    }

    private static void WriteWaveFormat(
        BinaryWriter writer, int formatTag, int channels, int sampleRate, int bitsPerSample)
    {
        int blockAlign = channels * (bitsPerSample / 8);
        writer.Write(18);                                  // WAVEFORMATEX with a zero extension
        writer.Write((ushort)formatTag);
        writer.Write((ushort)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * blockAlign);             // average bytes per second
        writer.Write((ushort)blockAlign);
        writer.Write((ushort)bitsPerSample);
        writer.Write((ushort)0);                           // cbSize
    }

    private static byte[] BuildAsset(string[] readerNames, Action<BinaryWriter> writeBody)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write7BitEncodedInt(readerNames.Length);
            foreach (string name in readerNames)
            {
                writer.Write(name);
                writer.Write(0);
            }

            writer.Write7BitEncodedInt(0); // no shared resources
            writeBody(writer);
        }

        byte[] bytes = payload.ToArray();
        using var container = new MemoryStream();
        using (var writer = new BinaryWriter(container, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)'X');
            writer.Write((byte)'N');
            writer.Write((byte)'B');
            writer.Write((byte)'w');
            writer.Write((byte)5);
            writer.Write((byte)0);                    // uncompressed
            writer.Write(10 + bytes.Length);
            writer.Write(bytes);
        }

        return container.ToArray();
    }

    private sealed class MemoryContentManager(IServiceProvider services, byte[] asset)
        : ContentManager(services)
    {
        // Only its own asset, as a manager reading files would: XNA reads an asset's external
        // references through OpenStream too, and one answering every name with the referring asset
        // would load it inside itself without end.
        protected override Stream OpenStream(string assetName) => assetName == "asset"
            ? new MemoryStream(asset, writable: false)
            : throw new ContentLoadException($"No asset '{assetName}'.");
    }
}
