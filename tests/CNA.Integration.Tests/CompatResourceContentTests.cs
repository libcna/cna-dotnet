using CNA.XnaCompat.Extensions;
using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
using XnaGame = Microsoft.Xna.Framework.Game;
using TextureLevelXnb = CnaDotnet.Integration.Tests.Content.TextureLevelXnb;

// NOT under CNA: see CompatWindowAndAudioContentTests.
namespace CnaDotnet.Integration.Tests.Compat;

/// <summary>
/// CSX-129: XNA reads every asset through <c>ContentManager.OpenStream</c>, so a manager that
/// overrides it decides where fonts and textures come from too. raphaelmun/Xen's game base loads
/// its fonts through a <c>ResourceContentManager</c> from <c>.xnb</c> files embedded in a
/// <c>.resx</c>; CNA.NET sent built-in types to CNA's own loader by path, which looked for an
/// <c>Arial.xnb</c> beside the program.
/// </summary>
[Collection(global::CNA.Integration.Tests.OwnGameCollection.Name)]
public class CompatResourceContentTests
{
    private static readonly string Assets = Path.Combine(AppContext.BaseDirectory, "assets", "xnb");

    [global::CNA.Integration.Tests.NativeFact]
    public void ResourceContentManager_LoadsAFontFromItsResources()
    {
        RunInAFrame(game =>
        {
            byte[] xnb = File.ReadAllBytes(Path.Combine(Assets, "lzx", "FontCalibri14.xnb"));
            using var resources = new ResourceContentManager(game.Services, new BytesResourceManager("Arial", xnb));
            SpriteFont font = resources.Load<SpriteFont>("Arial");

            game.Content.RootDirectory = Assets;
            SpriteFont fromFile = game.Content.Load<SpriteFont>("lzx/FontCalibri14");

            Assert.Equal(fromFile.Characters, font.Characters);
            Assert.Equal(fromFile.LineSpacing, font.LineSpacing);
            Assert.Equal(fromFile.Spacing, font.Spacing);
            Assert.Equal(fromFile.DefaultCharacter, font.DefaultCharacter);
            Assert.Equal(fromFile.MeasureString("Xen 2D, 1.5"), font.MeasureString("Xen 2D, 1.5"));

            using var batch = new SpriteBatch(game.GraphicsDevice);
            batch.Begin();
            batch.DrawString(font, "Xen 2D", Vector2.Zero, Color.White);
            batch.End();
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void AManagerOpeningItsOwnStreams_LoadsATextureFromThem()
    {
        RunInAFrame(game =>
        {
            Color[] texels = [new Color(255, 0, 0, 255), new Color(0, 0, 255, 128)];
            using var content = new MemoryContentManager(game.Services, TextureXnb(texels));
            Texture2D texture = content.Load<Texture2D>("not-a-file");

            Assert.Equal(2, texture.Width);
            Assert.Equal(1, texture.Height);
            var read = new Color[2];
            texture.GetData(read);
            Assert.Equal(texels, read);
        });
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void ReachTexture2D_ResourcePreservesNativeConstructorFailure()
    {
        RunInAFrame(game =>
        {
            Assert.Equal(GraphicsProfile.Reach, game.GraphicsDevice.GraphicsProfile);
            string assetName = $"cna-resource-{Guid.NewGuid():N}";
            Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, assetName + ".xnb")));
            using var resources = new ResourceContentManager(game.Services,
                new BytesResourceManager(assetName, TextureLevelXnb.Texture("Texture2D", 3, 2, 36, 4)));
            Assert.Throws<NotSupportedException>(() => resources.Load<Texture2D>(assetName));
        }, reach: true);
    }

    [Fact]
    public async Task ParentRelativeRoot_DefaultOpenStreamUsesTitleDirectoryInAnotherProcess()
    {
        // Only the child gets a different CWD; other tests keep the runner's working directory.
        string workingDirectory = Directory.CreateTempSubdirectory("cna-content-cwd-").FullName;
        try
        {
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("exec");
            start.ArgumentList.Add(typeof(global::CNA.Integration.Tests.Program).Assembly.Location);
            start.ArgumentList.Add(global::CNA.Integration.Tests.Program.ParentRelativeContentRoot);
            using Process child = Process.Start(start)!;
            Task<string> output = child.StandardOutput.ReadToEndAsync();
            Task<string> error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(60_000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                Assert.Fail("The content-root child did not finish.");
            }
            string standardOutput = await output;
            string standardError = await error;
            Assert.True(child.ExitCode == 0, $"exit {child.ExitCode}: {standardOutput} {standardError}");
            Assert.Contains("title-root-ok", standardOutput);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    internal static void CheckParentRelativeContentRoot()
    {
        string title = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        string working = Path.GetFullPath(Directory.GetCurrentDirectory()).TrimEnd(Path.DirectorySeparatorChar);
        Assert.NotEqual(title, working);
        string root = Path.Combine("..", $"cna-content-{Guid.NewGuid():N}");
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, root));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "asset.xnb"), TextureLevelXnb.Integer());
            Assert.False(File.Exists(Path.Combine(root, "asset.xnb")));
            using var content = new ContentManager(new GameServiceContainer(), root);
            Assert.Equal(42, content.Load<int>("asset"));
            ContentLoadException missing = Assert.Throws<ContentLoadException>(() => content.Load<int>("missing"));
            Assert.Equal("Error loading \"missing\". File not found.", missing.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// CSX-133: XNA reports a missing asset as ContentLoadException -- "File not found." -- and
    /// games catch it: AlexMeuer's City Shooter loads building1, building2, ...
    /// until the missing one throws. CNA's own loader threw CnaException for the types it loads.
    /// </summary>
    [global::CNA.Integration.Tests.NativeFact]
    public void AMissingAsset_IsContentLoadException_WhateverLoadsIt()
    {
        RunInAFrame(game =>
        {
            game.Content.RootDirectory = Assets;
            ContentLoadException texture = Assert.Throws<ContentLoadException>(() => game.Content.Load<Texture2D>("building9"));
            Assert.Equal("Error loading \"building9\". File not found.", texture.Message);
            Assert.Throws<ContentLoadException>(() => game.Content.Load<SpriteFont>("no-such-font"));
            Assert.Throws<ContentLoadException>(() => game.Content.Load<Microsoft.Xna.Framework.Audio.SoundEffect>("no-such-sound"));
            ContentLoadException managed = Assert.Throws<ContentLoadException>(() => game.Content.Load<string>("no-such-text"));
            Assert.Equal("Error loading \"no-such-text\". File not found.", managed.Message);
        });
    }

    private static byte[] TextureXnb(Color[] texels)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write7BitEncodedInt(1);
            writer.Write("Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework.Graphics, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553");
            writer.Write(0);
            writer.Write7BitEncodedInt(0);   // no shared resources
            writer.Write7BitEncodedInt(1);   // the root object: reader 1
            writer.Write((int)SurfaceFormat.Color);
            writer.Write(texels.Length);
            writer.Write(1);
            writer.Write(1);                 // one level
            writer.Write(texels.Length * 4);
            foreach (Color texel in texels)
            {
                writer.Write(texel.R);
                writer.Write(texel.G);
                writer.Write(texel.B);
                writer.Write(texel.A);
            }
        }

        byte[] body = payload.ToArray();
        using var container = new MemoryStream();
        using (var writer = new BinaryWriter(container, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("XNBw"u8.ToArray());
            writer.Write((byte)5);
            writer.Write((byte)0);
            writer.Write(10 + body.Length);
            writer.Write(body);
        }

        return container.ToArray();
    }

    private static void RunInAFrame(Action<XnaGame> body, bool reach = false)
    {
        using var game = new FrameProbe(body, reach);
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

        public FrameProbe(Action<XnaGame> body, bool reach)
        {
            _body = body;
            var graphics = new GraphicsDeviceManager(this);
            if (reach)
            {
                graphics.GraphicsProfile = GraphicsProfile.Reach;
            }
        }

        public bool Ran { get; private set; }

        public Exception? Failure { get; private set; }

        protected override void Update(GameTime gameTime)
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

            Exit();
            base.Update(gameTime);
        }
    }

    private sealed class BytesResourceManager(string name, byte[] bytes) : ResourceManager
    {
        public override object? GetObject(string resourceName) => GetObject(resourceName, null);

        public override object? GetObject(string resourceName, CultureInfo? culture) =>
            resourceName == name ? bytes : null;
    }

    private sealed class MemoryContentManager(IServiceProvider services, byte[] asset)
        : ContentManager(services)
    {
        protected override Stream OpenStream(string assetName) => new MemoryStream(asset, writable: false);
    }
}
