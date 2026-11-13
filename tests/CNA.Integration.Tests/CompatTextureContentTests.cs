using CNA.XnaCompat.Extensions;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
using XnaGame = Microsoft.Xna.Framework.Game;

namespace CnaDotnet.Integration.Tests.Content;

// RV-CONTENT-LEVELS: real constructors and uploads on both sides of the native/managed loader split.
[Collection(global::CNA.Integration.Tests.OwnGameCollection.Name)]
public class CompatTextureContentTests
{
    [global::CNA.Integration.Tests.NativeFact]
    public void Texture2D_FileAndStreamLoadsPreserveLevelFailures()
    {
        RunInAFrame(game => CheckLevels<Texture2D>(game, "Texture2D", native: true));
        RunInAFrame(game => CheckLevels<Texture2D>(game, "Texture2D", native: false));
    }

    [global::CNA.Integration.Tests.NativeFactRequiring(global::CNA.Graphics.GraphicsCapability.ThreeD)]
    public void TextureCube_FileAndStreamLoadsPreserveLevelFailures()
    {
        RunInAFrame(game =>
        {
            if (!game.GraphicsDevice.SupportsCnaCapability(CnaGraphicsCapability.ThreeD))
            {
                var refusal = Assert.Throws<global::CNA.CnaException>(() =>
                {
                    using var texture = new TextureCube(game.GraphicsDevice, 1, false, SurfaceFormat.Color);
                    texture.SetData(CubeMapFace.PositiveX, new[] { Color.White });
                });
                Assert.Equal("NotSupported", refusal.NativeResult);
                return;
            }

            CheckLevels<TextureCube>(game, "TextureCube", native: true);
            CheckLevels<TextureCube>(game, "TextureCube", native: false);
        });
    }

    [global::CNA.Integration.Tests.NativeFactRequiring(global::CNA.Graphics.GraphicsCapability.Texture3D)]
    public void Texture3D_StreamLoadsPreserveLevelFailures()
    {
        RunInAFrame(game =>
        {
            if (!game.GraphicsDevice.SupportsCnaCapability(CnaGraphicsCapability.Texture3D))
            {
                var refusal = Assert.Throws<global::CNA.CnaException>(() =>
                    new Texture3D(game.GraphicsDevice, 1, 1, 1, false, SurfaceFormat.Color).Dispose());
                Assert.Equal("NotSupported", refusal.NativeResult);
                return;
            }

            CheckLevels<Texture3D>(game, "Texture3D", native: false);
        }, hiDef: true);
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void SpriteFont_FileLoadDoesNotRetryAnInvalidAtlas()
    {
        RunInAFrame(game =>
        {
            TextureLevelXnb.WithFile(TextureLevelXnb.Font(4), root =>
            {
                using var content = new ContentManager(game.Services, root);
                Assert.Equal(1, content.Load<SpriteFont>("asset").LineSpacing);
            });
            TextureLevelXnb.WithFile(TextureLevelXnb.Font(1), root =>
            {
                using var content = new ContentManager(game.Services, root);
                // Level 0 is complete; accepting only it in the fallback would incorrectly load this font.
                Assert.Throws<ArgumentException>(() => content.Load<SpriteFont>("asset"));
            });
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void Texture2D_FileAndStreamLoadsPreserveNotSupported()
    {
        RunInAFrame(game =>
        {
            foreach (bool native in new[] { true, false })
            {
                WithContent(game, TextureLevelXnb.UnsupportedTexture(), native,
                    content => Assert.Throws<NotSupportedException>(() => content.Load<Texture2D>("asset")));
            }
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void ReachTexture2D_OverriddenStreamPreservesNativeConstructorFailure()
    {
        RunInAFrame(game =>
        {
            Assert.Equal(GraphicsProfile.Reach, game.GraphicsDevice.GraphicsProfile);
            using var content = new MemoryContentManager(game.Services,
                TextureLevelXnb.Texture("Texture2D", 3, 2, 36, 4));
            // Color and dimensions pass managed validation; native rejects Reach NPOT mipmaps.
            string assetName = $"cna-virtual-{Guid.NewGuid():N}";
            Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, assetName + ".xnb")));
            Assert.Throws<NotSupportedException>(() => content.Load<Texture2D>(assetName));
        }, reach: true);
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void SpriteFont_FileAndStreamLoadsPreserveNotSupportedAtlas()
    {
        RunInAFrame(game =>
        {
            foreach (bool native in new[] { true, false })
            {
                WithContent(game, TextureLevelXnb.Font(4, format: 20, size: 1), native,
                    content => Assert.Throws<NotSupportedException>(() => content.Load<SpriteFont>("asset")));
            }
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void ExternalTextureBufferStaysAliveUntilTheOutermostReadEnds()
    {
        RunInAFrame(game =>
        {
            using var content = new NestedMemoryContentManager(game.Services);
            Assert.Equal("elementCount", Assert.Throws<ArgumentOutOfRangeException>(() =>
                content.Load<NestedTextureAsset>("parent")).ParamName);
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void ProtectedReadAssetOpensTheStreamOnTheGameThread()
    {
        RunInAFrame(game =>
        {
            using var content = new UncachedMemoryContentManager(game.Services);
            int gameThread = Environment.CurrentManagedThreadId;
            Exception? failure = null;
            int answer = 0;
            var worker = new Thread(() =>
            {
                try { answer = content.ReadUncached(); }
                catch (Exception exception) { failure = exception; }
            }) { IsBackground = true };
            worker.Start();
            // The game's wait context pumps dispatched reads while the game thread joins.
            Assert.True(worker.Join(TimeSpan.FromSeconds(5)), "The protected read did not finish.");
            Assert.Null(failure);
            Assert.Equal(42, answer);
            Assert.Equal(gameThread, content.OpenThread);
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void StreamLoad_FirstZeroLevelKeepsTheEmptyArrayGuard()
    {
        RunInAFrame(game =>
        {
            // XNA's fresh empty-array guard. CNA's native file loader has a separate discrepancy.
            using var content = new MemoryContentManager(game.Services,
                TextureLevelXnb.Texture("Texture2D", 1, 1, 0));
            Assert.Equal("data", Assert.Throws<ArgumentNullException>(() =>
                content.Load<Texture2D>("asset")).ParamName);
            using var texture = new Texture2D(game.GraphicsDevice, 1, 1);
            Assert.Equal("data", Assert.Throws<ArgumentNullException>(() =>
                texture.SetData(Array.Empty<byte>())).ParamName);
        });
    }

    private static void CheckLevels<T>(XnaGame game, string kind, bool native)
    {
        WithContent(game, TextureLevelXnb.Texture(kind, 2, 1, 1), native,
            content => Assert.Throws<ArgumentException>(() => content.Load<T>("asset")));
        int levelZeroBytes = kind == "Texture3D" ? 32 : 16;
        WithContent(game, TextureLevelXnb.Texture(kind, 2, 2, levelZeroBytes, 0), native,
            content => Assert.Equal("elementCount", Assert.Throws<ArgumentOutOfRangeException>(() =>
                content.Load<T>("asset")).ParamName));
        // Only two payloads per face: SetData at level 1 must fail before reading level 2.
        WithContent(game, TextureLevelXnb.Texture(kind, 1, 33, 4, 4), native,
            content => Assert.Throws<InvalidOperationException>(() => content.Load<T>("asset")));
        WithContent(game, TextureLevelXnb.Texture(kind, 2, 2, levelZeroBytes, 4), native, content =>
        {
            // A smaller *valid* mip must not upload the reused level-0 capacity.
            Assert.IsType<T>(content.Load<T>("asset"));
        });
    }

    private static void WithContent(XnaGame game, byte[] asset, bool native, Action<ContentManager> body)
    {
        if (native)
        {
            TextureLevelXnb.WithFile(asset, root =>
            {
                using var content = new ContentManager(game.Services, root);
                body(content);
            });
        }
        else
        {
            using var content = new MemoryContentManager(game.Services, asset);
            body(content);
        }
    }

    private static void RunInAFrame(Action<XnaGame> body, bool hiDef = false, bool reach = false)
    {
        using var game = new FrameProbe(body, hiDef, reach);
        for (int frame = 0; frame < 4 && !game.Ran; frame++)
        {
            game.RunFrameEXT();
        }

        Assert.True(game.Ran, "The frame never ran, so nothing was exercised.");
        if (game.Failure is { } failure)
        {
            throw new Xunit.Sdk.XunitException($"The body threw inside the frame: {failure}");
        }
    }

    private sealed class FrameProbe : XnaGame
    {
        private readonly Action<XnaGame> _body;

        public FrameProbe(Action<XnaGame> body, bool hiDef, bool reach)
        {
            _body = body;
            var graphics = new GraphicsDeviceManager(this);
            if (hiDef)
            {
                graphics.GraphicsProfile = GraphicsProfile.HiDef;
            }
            else if (reach)
            {
                graphics.GraphicsProfile = GraphicsProfile.Reach;
            }
        }

        public bool Ran { get; private set; }

        public Exception? Failure { get; private set; }

        protected override void Update(GameTime gameTime)
        {
            if (!Ran)
            {
                Ran = true;
                try
                {
                    _body(this);
                }
                catch (Exception exception)
                {
                    Failure = exception;
                }
            }

            Exit();
            base.Update(gameTime);
        }
    }

    private sealed class NestedTextureAsset
    {
    }

    private sealed class NestedTextureReader : ContentTypeReader<NestedTextureAsset>
    {
        protected override NestedTextureAsset Read(ContentReader input, NestedTextureAsset existingInstance)
        {
            ReadChild(input);
            // The child's disposed reader and its upload stack are gone before collection.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            _ = input.ReadObject<Texture2D>();
            return new NestedTextureAsset();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadChild(ContentReader input) =>
            Assert.NotNull(input.ReadExternalReference<Texture2D>());
    }

    private sealed class NestedMemoryContentManager(IServiceProvider services) : ContentManager(services)
    {
        protected override Stream OpenStream(string assetName) => new MemoryStream(
            assetName == "parent"
                ? TextureLevelXnb.Nested(typeof(NestedTextureReader).AssemblyQualifiedName!)
                : TextureLevelXnb.Texture("Texture2D", 1, 1, 4), writable: false);
    }

    private sealed class UncachedMemoryContentManager(IServiceProvider services) : ContentManager(services)
    {
        public int OpenThread { get; private set; }

        public int ReadUncached() => ReadAsset<int>("asset", null);

        protected override Stream OpenStream(string assetName)
        {
            OpenThread = Environment.CurrentManagedThreadId;
            return new MemoryStream(TextureLevelXnb.Integer(), writable: false);
        }
    }

    private sealed class MemoryContentManager(IServiceProvider services, byte[] asset) : ContentManager(services)
    {
        protected override Stream OpenStream(string assetName) => new MemoryStream(asset, writable: false);
    }
}

[Collection(global::CNA.Integration.Tests.NativeGameCollection.Name)]
public class FrameworkSpriteFontLevelTests(global::CNA.Integration.Tests.NativeGameFixture fixture)
{
    [global::CNA.Integration.Tests.NativeFact]
    public void SpriteFont_FileLoadPreservesCanonicalNotSupported()
    {
        fixture.InsideAFrameWithDevice(device =>
        {
            TextureLevelXnb.WithFile(TextureLevelXnb.Font(4, format: 20, size: 1), root =>
            {
                using var content = global::CNA.Content.ContentManager.CreateOwned(device, root);
                var failure = Assert.Throws<global::CNA.CnaException>(() =>
                    content.Load<global::CNA.Graphics.SpriteFont>("asset"));
                Assert.Equal("System.NotSupportedException", failure.CanonicalExceptionType);
            });
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void SpriteFont_FileLoadPreservesCanonicalAtlasFailure()
    {
        fixture.InsideAFrameWithDevice(device =>
        {
            TextureLevelXnb.WithFile(TextureLevelXnb.Font(4), root =>
            {
                using var content = global::CNA.Content.ContentManager.CreateOwned(device, root);
                Assert.Equal(1, content.Load<global::CNA.Graphics.SpriteFont>("asset").LineSpacing);
            });
            TextureLevelXnb.WithFile(TextureLevelXnb.Font(1), root =>
            {
                using var content = global::CNA.Content.ContentManager.CreateOwned(device, root);
                var failure = Assert.Throws<global::CNA.CnaException>(() =>
                    content.Load<global::CNA.Graphics.SpriteFont>("asset"));
                Assert.Equal("System.ArgumentException", failure.CanonicalExceptionType);
            });
        });
    }
}

internal static class TextureLevelXnb
{
    private const string Xna = ", Microsoft.Xna.Framework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553";
    private const string Graphics = ", Microsoft.Xna.Framework.Graphics, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553";

    internal static byte[] Texture(string kind, int size, int levels, params int[] counts) => Build(
        ["Microsoft.Xna.Framework.Content." + kind + "Reader" + Graphics], writer =>
        {
            writer.Write7BitEncodedInt(1);
            WriteLevels(writer, kind, size, levels, counts);
        });

    internal static byte[] UnsupportedTexture() => Build(
        ["Microsoft.Xna.Framework.Content.Texture2DReader" + Graphics], writer =>
        {
            writer.Write7BitEncodedInt(1);
            WriteLevels(writer, "Texture2D", 1, 1, [4], format: 20);
        });

    internal static byte[] Integer() => Build(
        ["Microsoft.Xna.Framework.Content.Int32Reader" + Xna], writer =>
        {
            writer.Write7BitEncodedInt(1);
            writer.Write(42);
        });

    internal static byte[] Nested(string parentReader) => Build(
        [parentReader, "Microsoft.Xna.Framework.Content.Texture2DReader" + Graphics], writer =>
        {
            writer.Write7BitEncodedInt(1);
            writer.Write("child"); // ReadExternalReference<Texture2D>
            writer.Write7BitEncodedInt(2);
            WriteLevels(writer, "Texture2D", 1, 1, [0]);
        });

    internal static byte[] Font(int secondLevelBytes, int format = 0, int size = 2) => Build(
        ["Microsoft.Xna.Framework.Content.SpriteFontReader" + Graphics,
         "Microsoft.Xna.Framework.Content.Texture2DReader" + Graphics,
         "Microsoft.Xna.Framework.Content.ListReader`1[[Microsoft.Xna.Framework.Rectangle" + Xna + "]]" + Xna,
         "Microsoft.Xna.Framework.Content.ListReader`1[[System.Char, mscorlib]]" + Xna,
         "Microsoft.Xna.Framework.Content.ListReader`1[[Microsoft.Xna.Framework.Vector3" + Xna + "]]" + Xna], writer =>
        {
            writer.Write7BitEncodedInt(1);
            writer.Write7BitEncodedInt(2);
            WriteLevels(writer, "Texture2D", size, size == 1 ? 1 : 2,
                size == 1 ? [4] : [16, secondLevelBytes], format);
            writer.Write7BitEncodedInt(3); // glyph bounds
            writer.Write(0);
            writer.Write7BitEncodedInt(3); // cropping
            writer.Write(0);
            writer.Write7BitEncodedInt(4); // characters
            writer.Write(0);
            writer.Write(1);              // line spacing
            writer.Write(0f);             // spacing
            writer.Write7BitEncodedInt(5); // kerning
            writer.Write(0);
            writer.Write(false);          // no default character
        });

    private static void WriteLevels(BinaryWriter writer, string kind, int size, int levels, int[] counts, int format = 0)
    {
        writer.Write(format);
        writer.Write(size);
        if (kind != "TextureCube")
        {
            writer.Write(size);
        }
        if (kind == "Texture3D")
        {
            writer.Write(size);
        }
        writer.Write(levels);
        for (int face = 0; face < (kind == "TextureCube" ? 6 : 1); face++)
        {
            foreach (int count in counts)
            {
                writer.Write(count);
                writer.Write(new byte[count]);
            }
        }
    }

    internal static void WithFile(byte[] asset, Action<string> body)
    {
        string root = Path.Combine(Path.GetTempPath(), $"cna-levels-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "asset.xnb"), asset);
            body(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Build(string[] readers, Action<BinaryWriter> body)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write7BitEncodedInt(readers.Length);
            foreach (string reader in readers)
            {
                writer.Write(reader);
                writer.Write(0);
            }

            writer.Write7BitEncodedInt(0);
            body(writer);
        }

        using var container = new MemoryStream();
        using (var writer = new BinaryWriter(container, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("XNBw"u8.ToArray());
            writer.Write((byte)5);
            writer.Write((byte)0);
            writer.Write(10 + (int)payload.Length);
            writer.Write(payload.ToArray());
        }

        return container.ToArray();
    }
}
