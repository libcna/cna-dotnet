using CNA.XnaCompat.Extensions;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
using Xunit.Abstractions;
using XnaGame = Microsoft.Xna.Framework.Game;

// NOT under CNA, for the reason CompatLayerIntegrationTests records.
namespace CnaDotnet.Integration.Tests.Content;

/// <summary>
/// The four stock effects beyond <c>BasicEffect</c>, read through the public
/// <see cref="ContentReader"/> protocol (cna-cs CSX-092). SkinningSample's <c>dude</c> reaches this
/// path because its tag is the game's own <c>SkinningData</c> (CSX-089), and its mesh parts carry a
/// <c>SkinnedEffectReader</c> the table did not have. Each payload below is written in the field
/// order of XNA 4.0's reader IL, with values that differ field to field, so a transposed read
/// shows as a wrong value rather than as a plausible one.
/// </summary>
[Collection(global::CNA.Integration.Tests.OwnGameCollection.Name)]
public class CompatStockEffectReaderTests(ITestOutputHelper output)
{
    public sealed class Holder
    {
        public SkinnedEffect Skinned { get; set; } = null!;

        public AlphaTestEffect AlphaTest { get; set; } = null!;

        public DualTextureEffect DualTexture { get; set; } = null!;

        public EnvironmentMapEffect EnvironmentMap { get; set; } = null!;
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void StockEffects_ReadInTheirXnaFieldOrder()
    {
        using var game = new HolderProbe(BuildAsset());
        for (int frame = 0; frame < 4 && !game.Ran; frame++)
        {
            game.RunFrameEXT();
        }

        Assert.True(game.Ran, "The frame never ran, so nothing was exercised.");
        if (!game.SupportsThreeD)
        {
            var refusal = Assert.IsAssignableFrom<global::CNA.CnaException>(game.Failure);
            output.WriteLine($"ABSENT BRANCH EXERCISED: a 2D-only renderer refuses the effects -- {refusal.Message}");
            return;
        }

        if (game.Failure is { } failure)
        {
            throw new Xunit.Sdk.XunitException($"Loading the stock effects threw: {failure}");
        }

        Holder loaded = game.Loaded!;

        SkinnedEffect skinned = loaded.Skinned;
        Assert.Null(skinned.Texture);
        Assert.Equal(2, skinned.WeightsPerVertex);
        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), skinned.DiffuseColor);
        Assert.Equal(new Vector3(0.4f, 0.5f, 0.6f), skinned.EmissiveColor);
        Assert.Equal(new Vector3(0.7f, 0.8f, 0.9f), skinned.SpecularColor);
        Assert.Equal(12f, skinned.SpecularPower);
        Assert.Equal(0.5f, skinned.Alpha);

        AlphaTestEffect alphaTest = loaded.AlphaTest;
        Assert.Null(alphaTest.Texture);
        Assert.Equal(CompareFunction.GreaterEqual, alphaTest.AlphaFunction);
        Assert.Equal(96, alphaTest.ReferenceAlpha);
        Assert.Equal(new Vector3(0.25f, 0.5f, 0.75f), alphaTest.DiffuseColor);
        Assert.Equal(0.75f, alphaTest.Alpha);
        Assert.True(alphaTest.VertexColorEnabled);

        DualTextureEffect dualTexture = loaded.DualTexture;
        Assert.Null(dualTexture.Texture);
        Assert.Null(dualTexture.Texture2);
        Assert.Equal(new Vector3(0.3f, 0.6f, 0.9f), dualTexture.DiffuseColor);
        Assert.Equal(0.25f, dualTexture.Alpha);
        Assert.True(dualTexture.VertexColorEnabled);

        EnvironmentMapEffect environmentMap = loaded.EnvironmentMap;
        Assert.Null(environmentMap.Texture);
        Assert.Null(environmentMap.EnvironmentMap);
        Assert.Equal(0.8f, environmentMap.EnvironmentMapAmount);
        Assert.Equal(new Vector3(0.05f, 0.15f, 0.25f), environmentMap.EnvironmentMapSpecular);
        Assert.Equal(0.6f, environmentMap.FresnelFactor);
        Assert.Equal(new Vector3(0.9f, 0.8f, 0.7f), environmentMap.DiffuseColor);
        Assert.Equal(new Vector3(0.35f, 0.45f, 0.55f), environmentMap.EmissiveColor);
        Assert.Equal(0.4f, environmentMap.Alpha);
    }

    private sealed class HolderProbe : XnaGame
    {
        private readonly byte[] _asset;
        private ContentManager? _content;

        public HolderProbe(byte[] asset)
        {
            _asset = asset;
            _ = new GraphicsDeviceManager(this);
        }

        public bool Ran { get; private set; }

        public bool SupportsThreeD { get; private set; }

        public Exception? Failure { get; private set; }

        public Holder? Loaded { get; private set; }

        protected override void Update(GameTime gameTime)
        {
            if (!Ran)
            {
                Ran = true;
                SupportsThreeD = global::CNA.XnaCompat.Extensions.CnaGraphicsDeviceExtensions
                    .SupportsCnaCapability(
                        GraphicsDevice, global::CNA.XnaCompat.Extensions.CnaGraphicsCapability.ThreeD);
                try
                {
                    _content = new MemoryContentManager(Services, _asset);
                    Loaded = _content.Load<Holder>("effects");
                }
                catch (Exception exception)
                {
                    Failure = exception;
                }
            }

            Exit();
            base.Update(gameTime);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _content?.Dispose();
                _content = null;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class MemoryContentManager(IServiceProvider services, byte[] asset)
        : ContentManager(services)
    {
        // Only its own asset, as a manager reading files would: XNA reads an asset's external
        // references through OpenStream too, and one answering every name with the referring asset
        // would load it inside itself without end.
        protected override Stream OpenStream(string assetName) => assetName == "effects"
            ? new MemoryStream(asset, writable: false)
            : throw new ContentLoadException($"No asset '{assetName}'.");
    }

    private static byte[] BuildAsset()
    {
        // The assembly XNA's own pipeline names for these readers.
        const string graphics =
            ", Microsoft.Xna.Framework.Graphics, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553";

        string[] readers =
        [
            "Microsoft.Xna.Framework.Content.ReflectiveReader`1[[" + typeof(Holder).AssemblyQualifiedName + "]]" + graphics,
            "Microsoft.Xna.Framework.Content.SkinnedEffectReader" + graphics,
            "Microsoft.Xna.Framework.Content.AlphaTestEffectReader" + graphics,
            "Microsoft.Xna.Framework.Content.DualTextureEffectReader" + graphics,
            "Microsoft.Xna.Framework.Content.EnvironmentMapEffectReader" + graphics,
        ];

        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write7BitEncodedInt(readers.Length);
            foreach (string name in readers)
            {
                writer.Write(name);
                writer.Write(0);
            }

            writer.Write7BitEncodedInt(0);  // no shared resources
            writer.Write7BitEncodedInt(1);  // the holder

            writer.Write7BitEncodedInt(2);  // SkinnedEffectReader
            writer.Write(string.Empty);     // texture: none
            writer.Write(2);                // weights per vertex
            WriteVector3(writer, 0.1f, 0.2f, 0.3f);   // diffuse
            WriteVector3(writer, 0.4f, 0.5f, 0.6f);   // emissive
            WriteVector3(writer, 0.7f, 0.8f, 0.9f);   // specular
            writer.Write(12f);              // specular power
            writer.Write(0.5f);             // alpha

            writer.Write7BitEncodedInt(3);  // AlphaTestEffectReader
            writer.Write(string.Empty);     // texture: none
            writer.Write((int)CompareFunction.GreaterEqual);
            writer.Write(96);               // reference alpha
            WriteVector3(writer, 0.25f, 0.5f, 0.75f); // diffuse
            writer.Write(0.75f);            // alpha
            writer.Write(true);             // vertex colour enabled

            writer.Write7BitEncodedInt(4);  // DualTextureEffectReader
            writer.Write(string.Empty);     // texture: none
            writer.Write(string.Empty);     // texture 2: none
            WriteVector3(writer, 0.3f, 0.6f, 0.9f);   // diffuse
            writer.Write(0.25f);            // alpha
            writer.Write(true);             // vertex colour enabled

            writer.Write7BitEncodedInt(5);  // EnvironmentMapEffectReader
            writer.Write(string.Empty);     // texture: none
            writer.Write(string.Empty);     // environment map: none
            writer.Write(0.8f);             // environment map amount
            WriteVector3(writer, 0.05f, 0.15f, 0.25f); // environment map specular
            writer.Write(0.6f);             // fresnel factor
            WriteVector3(writer, 0.9f, 0.8f, 0.7f);   // diffuse
            WriteVector3(writer, 0.35f, 0.45f, 0.55f); // emissive
            writer.Write(0.4f);             // alpha
        }

        byte[] bytes = payload.ToArray();
        using var container = new MemoryStream();
        using (var writer = new BinaryWriter(container, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("XNBw"u8);
            writer.Write((byte)5);
            writer.Write((byte)0);
            writer.Write(10 + bytes.Length);
            writer.Write(bytes);
        }

        return container.ToArray();

        static void WriteVector3(BinaryWriter writer, float x, float y, float z)
        {
            writer.Write(x);
            writer.Write(y);
            writer.Write(z);
        }
    }
}
