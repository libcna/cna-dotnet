using CNA.XnaCompat.Extensions;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
using XnaGame = Microsoft.Xna.Framework.Game;

// NOT under CNA, for the reason CompatLayerIntegrationTests records.
namespace CnaDotnet.Integration.Tests.Compat;

/// <summary>A game-defined tag, read by the game's own reader -- what HeightmapCollision's
/// <c>HeightMapInfo</c> is to its terrain.</summary>
public sealed class ElevationTag
{
    public int Height { get; init; }
}

public sealed class ElevationTagReader : ContentTypeReader<ElevationTag>
{
    protected override ElevationTag Read(ContentReader input, ElevationTag existingInstance) =>
        new() { Height = input.ReadInt32() };
}

/// <summary>
/// A <c>Model</c> whose tag is of the game's own type loads through the game's own reader
/// (cna-cs CSX-089), as HeightmapCollision's terrain does.
/// </summary>
[Collection(global::CNA.Integration.Tests.OwnGameCollection.Name)]
public class CompatModelTagTests
{
    private sealed class ModelProbe : XnaGame
    {
        private readonly string _contentRoot;

        public ModelProbe(string contentRoot)
        {
            _contentRoot = contentRoot;
            _ = new GraphicsDeviceManager(this);
        }

        public Model? Loaded { get; private set; }

        public Exception? Failure { get; private set; }

        protected override void LoadContent()
        {
            Content.RootDirectory = _contentRoot;
            try
            {
                Loaded = Content.Load<Model>("tagged");
            }
            catch (Exception ex)
            {
                Failure = ex;
            }
        }

        protected override void Update(GameTime gameTime)
        {
            Exit();
            base.Update(gameTime);
        }
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void Model_WhoseTagIsOfTheGamesOwnType_LoadsThroughTheGamesReader()
    {
        string root = Path.Combine(Path.GetTempPath(), "cna-dotnet-model-tag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "tagged.xnb"), ModelWithElevationTag(1234));
            using var game = new ModelProbe(root);
            game.RunFrameEXT();

            Assert.Null(game.Failure);
            Model model = Assert.IsType<Model>(game.Loaded);
            Assert.Equal(1234, Assert.IsType<ElevationTag>(model.Tag).Height);
            Assert.Equal("Root", Assert.Single(model.Bones).Name);
            Assert.Empty(model.Meshes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void Model_WhoseTagHoldsXnaMathValues_ReturnsThemAsXnaTypes()
    {
        string root = Path.Combine(Path.GetTempPath(), "cna-dotnet-model-tag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "tagged.xnb"), ModelWithPickingTag());
            using var game = new ModelProbe(root);
            game.RunFrameEXT();

            Assert.Null(game.Failure);
            Model model = Assert.IsType<Model>(game.Loaded);

            // TrianglePicking's own reads (cna-cs-samples CSSAMPLE-048).
            var tagData = Assert.IsType<Dictionary<string, object>>(model.Tag);
            BoundingSphere sphere = Assert.IsType<BoundingSphere>(tagData["BoundingSphere"]);
            Assert.Equal(new Vector3(1f, 2f, 3f), sphere.Center);
            Assert.Equal(4f, sphere.Radius);
            Vector3[] vertices = Assert.IsType<Vector3[]>(tagData["Vertices"]);
            Assert.Equal([new Vector3(5f, 6f, 7f), new Vector3(8f, 9f, 10f)], vertices);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A one-bone, meshless model whose own tag uses the game's reader.</summary>
    private static byte[] ModelWithElevationTag(int height) => ModelWithTag(
        [typeof(ElevationTagReader).FullName + ", " + typeof(ElevationTagReader).Assembly.GetName().Name],
        writer =>
        {
            writer.Write7BitEncodedInt(3); writer.Write(height);    // the tag, by the game's reader
        });

    /// <summary>A one-bone, meshless model tagged the way TrianglePicking's processor tags its
    /// models: a <c>Dictionary&lt;string, object&gt;</c> holding a <c>BoundingSphere</c> and a
    /// <c>Vector3[]</c>, with the reader names the real asset declares.</summary>
    private static byte[] ModelWithPickingTag() => ModelWithTag(
        [
            "Microsoft.Xna.Framework.Content.DictionaryReader`2[[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[System.Object, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]",
            "Microsoft.Xna.Framework.Content.ArrayReader`1[[Microsoft.Xna.Framework.Vector3, Microsoft.Xna.Framework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553]]",
            "Microsoft.Xna.Framework.Content.Vector3Reader",
            "Microsoft.Xna.Framework.Content.BoundingSphereReader",
        ],
        writer =>
        {
            const int stringReader = 2, dictionaryReader = 3, arrayReader = 4, sphereReader = 6;
            writer.Write7BitEncodedInt(dictionaryReader);
            writer.Write(2);                                        // two entries
            writer.Write7BitEncodedInt(stringReader); writer.Write("BoundingSphere");
            writer.Write7BitEncodedInt(sphereReader);
            writer.Write(1f); writer.Write(2f); writer.Write(3f); writer.Write(4f);
            writer.Write7BitEncodedInt(stringReader); writer.Write("Vertices");
            writer.Write7BitEncodedInt(arrayReader);
            writer.Write(2);                                        // two elements, raw: a value type
            writer.Write(5f); writer.Write(6f); writer.Write(7f);
            writer.Write(8f); writer.Write(9f); writer.Write(10f);
        });

    /// <summary>A one-bone, meshless model; readers 1 and 2 are the model's and the string's, and
    /// <paramref name="tagReaders"/> follow from 3.</summary>
    private static byte[] ModelWithTag(string[] tagReaders, Action<BinaryWriter> writeTag)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            string[] readers =
            [
                "Microsoft.Xna.Framework.Content.ModelReader",
                "Microsoft.Xna.Framework.Content.StringReader",
                .. tagReaders,
            ];
            writer.Write7BitEncodedInt(readers.Length);
            foreach (string name in readers)
            {
                writer.Write(name);
                writer.Write(0);
            }

            writer.Write7BitEncodedInt(0);                          // no shared resources
            writer.Write7BitEncodedInt(1);                          // the root: a Model
            writer.Write(1u);                                       // one bone
            writer.Write7BitEncodedInt(2); writer.Write("Root");    // its name
            for (int i = 0; i < 16; i++)
            {
                writer.Write(i % 5 == 0 ? 1f : 0f);                 // identity
            }

            writer.Write((byte)0);                                  // no parent
            writer.Write(0u);                                       // no children
            writer.Write(0u);                                       // no meshes
            writer.Write((byte)1);                                  // root bone reference
            writeTag(writer);
        }

        byte[] body = payload.ToArray();
        using var container = new MemoryStream();
        using (var writer = new BinaryWriter(container, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("XNBw"u8);
            writer.Write((byte)5);
            writer.Write((byte)0);
            writer.Write(10 + body.Length);
            writer.Write(body);
        }

        return container.ToArray();
    }
}
