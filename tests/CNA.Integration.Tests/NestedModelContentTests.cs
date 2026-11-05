using CNA.XnaCompat.Extensions;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
using Xunit.Abstractions;
using XnaGame = Microsoft.Xna.Framework.Game;

// NOT under CNA: an enclosing CNA namespace shadows the `using Microsoft.Xna.Framework` imports, so
// `Vector3` and friends would bind to the CNA types. CompatLayerIntegrationTests records the same
// constraint at length; a ported game lives under it too.
namespace CnaDotnet.Integration.Tests.Content;

/// <summary>
/// A model nested inside another asset, loaded through the public <see cref="ContentReader"/>
/// protocol against a real device.
///
/// <b>Why this needs a device and a hand-built asset.</b> A top-level <c>Load&lt;Model&gt;</c> goes
/// to CNA's own content loader; only a nested one reaches the managed model readers, and nothing
/// in this repository ships an asset shaped that way. The fixture is written byte by byte from the
/// format the decompiled XNA 4.0 readers define -- which is a real limit on what it proves, and the
/// reason each reader was transcribed rather than inferred.
///
/// <b>What it is actually defending.</b> The shared-resource indirection. A mesh part names its
/// vertex buffer, index buffer and effect by index into a table written after the root object, so
/// the references are fix-ups resolved later; the fixture deliberately has two mesh parts naming
/// the <em>same</em> vertex buffer, because sharing is the reason the format works that way and an
/// implementation that resolved eagerly, or that captured the loop variable rather than the part,
/// would still produce a model-shaped object.
/// </summary>
[Collection(global::CNA.Integration.Tests.OwnGameCollection.Name)]
public class NestedModelContentTests(ITestOutputHelper output)
{
    [global::CNA.Integration.Tests.NativeFact]
    public void NestedModel_ResolvesSharedBuffersAndEffects()
    {
        using var game = new HolderProbe(BuildNestedModelAsset());

        for (int frame = 0; frame < 4 && !game.Ran; frame++)
        {
            game.RunFrameEXT();
        }

        Assert.True(game.Ran, "The frame never ran, so nothing was exercised.");

        // A model is vertex and index buffers, so on a renderer with no 3D pipeline the load cannot
        // succeed and must not be reported as a defect in the readers this test is about. Measured
        // on a SDL_RENDERER build: the load fails at the first VertexBuffer with NotSupported.
        // Asserted rather than skipped -- "this asset needs a 3D renderer" is a claim, and a silent
        // return would make this test prove nothing there.
        if (!game.SupportsThreeD)
        {
            var refusal = Assert.IsAssignableFrom<global::CNA.CnaException>(game.Failure);
            Assert.Equal("NotSupported", refusal.NativeResult);
            output.WriteLine(
                $"ABSENT BRANCH EXERCISED: a 2D-only renderer refuses the model's buffers -- {refusal.Message}");
            return;
        }

        if (game.Failure is { } failure)
        {
            throw new Xunit.Sdk.XunitException($"Loading the nested model threw: {failure}");
        }

        Model model = game.Loaded!.Model;
        output.WriteLine(
            $"{model.Bones.Count} bone(s), {model.Meshes.Count} mesh(es), " +
            $"{model.Meshes[0].MeshParts.Count} part(s), tag={model.Tag}");

        Assert.Equal(2, model.Bones.Count);
        Assert.Equal("root", model.Bones[0].Name);
        Assert.Equal("child", model.Bones[1].Name);
        Assert.Same(model.Bones[0], model.Bones[1].Parent);
        Assert.Equal("model-tag", model.Tag);

        ModelMesh mesh = Assert.Single(model.Meshes);
        Assert.Equal("cube", mesh.Name);
        Assert.Same(model.Bones[1], mesh.ParentBone);
        Assert.Equal(2.5f, mesh.BoundingSphere.Radius, 1e-4f);

        Assert.Equal(2, mesh.MeshParts.Count);
        foreach (ModelMeshPart part in mesh.MeshParts)
        {
            Assert.NotNull(part.VertexBuffer);
            Assert.NotNull(part.IndexBuffer);
            Assert.IsType<BasicEffect>(part.Effect);
        }

        // The shared vertex buffer: one resource, named twice.
        Assert.Same(mesh.MeshParts[0].VertexBuffer, mesh.MeshParts[1].VertexBuffer);

        // Each part kept its own offsets, which a closure capturing the loop variable would not.
        Assert.Equal(0, mesh.MeshParts[0].StartIndex);
        Assert.Equal(3, mesh.MeshParts[1].StartIndex);

        Assert.True(((BasicEffect)mesh.MeshParts[0].Effect!).VertexColorEnabled);
    }

    public sealed class Holder
    {
        public Model Model { get; set; } = null!;
    }

    private sealed class HolderProbe : XnaGame
    {
        private readonly byte[] _asset;

        // The graphics device reaches a content reader as a *service*, and it is
        // GraphicsDeviceManager that registers it. A game that never constructs one has a
        // GraphicsDevice and no IGraphicsDeviceService, which is exactly the shape that made the
        // first run of this test fail -- and exactly the mistake a unit test makes.
        private readonly GraphicsDeviceManager _manager;

        public HolderProbe(byte[] asset)
        {
            _asset = asset;
            _manager = new GraphicsDeviceManager(this);
        }

        public bool Ran { get; private set; }

        public Exception? Failure { get; private set; }

        public Holder? Loaded { get; private set; }

        // Held, not disposed at the end of the frame. A ContentManager owns what it loaded, so
        // disposing it there leaves the assertions reading a disposed effect -- which is what the
        // first run of this test did, and the failure named a native handle rather than the
        // lifetime mistake behind it.
        private ContentManager? _content;

        /// <summary>
        /// Whether the renderer has a 3D pipeline, captured <b>during</b> the frame.
        ///
        /// Not asked afterwards: the device may be borrowed only inside a lifecycle callback, and
        /// asking outside one answers <c>InvalidState</c>. That is the ABI's rule and this class is
        /// the only place in the test that is inside a callback at all.
        /// </summary>
        public bool SupportsThreeD { get; private set; }

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
                    Loaded = _content.Load<Holder>("nested");
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
        protected override Stream OpenStream(string assetName) => assetName == "nested"
            ? new MemoryStream(asset, writable: false)
            : throw new ContentLoadException($"No asset '{assetName}'.");
    }

    /// <summary>
    /// One reflective holder whose single property is a model: two bones, one mesh, two parts, one
    /// shared vertex buffer, one index buffer and one BasicEffect.
    /// </summary>
    private static byte[] BuildNestedModelAsset()
    {
        const string xna = ", Microsoft.Xna.Framework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553";
        const string corlib = ", mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

        string[] readers =
        [
            "Microsoft.Xna.Framework.Content.ReflectiveReader`1[[" + typeof(Holder).AssemblyQualifiedName + "]]" + xna,
            "Microsoft.Xna.Framework.Content.ModelReader" + xna,
            "Microsoft.Xna.Framework.Content.StringReader" + corlib.Replace("mscorlib", "mscorlib"),
            "Microsoft.Xna.Framework.Content.VertexBufferReader" + xna,
            "Microsoft.Xna.Framework.Content.VertexDeclarationReader" + xna,
            "Microsoft.Xna.Framework.Content.IndexBufferReader" + xna,
            "Microsoft.Xna.Framework.Content.BasicEffectReader" + xna,
        ];

        const int reflective = 1, modelReader = 2, stringReader = 3;
        const int vertexBufferReader = 4, indexBufferReader = 6, basicEffectReader = 7;

        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write7BitEncodedInt(readers.Length);
            foreach (string name in readers)
            {
                writer.Write(name);
                writer.Write(0);
            }

            // Three shared resources: the vertex buffer, the index buffer, the effect.
            writer.Write7BitEncodedInt(3);

            // -- the root object: Holder, whose one property is the model ------------------------
            writer.Write7BitEncodedInt(reflective);
            writer.Write7BitEncodedInt(modelReader);

            // bones
            writer.Write(2);
            writer.Write7BitEncodedInt(stringReader);
            writer.Write("root");
            WriteMatrix(writer, Matrix.Identity);
            writer.Write7BitEncodedInt(stringReader);
            writer.Write("child");
            WriteMatrix(writer, Matrix.CreateTranslation(1f, 0f, 0f));

            // hierarchy: root has one child, the child has none
            writer.Write((byte)0);          // root's parent: none
            writer.Write(1);                // one child
            writer.Write((byte)2);          // the child, one-based
            writer.Write((byte)1);          // child's parent: root
            writer.Write(0);                // no children

            // meshes
            writer.Write(1);
            writer.Write7BitEncodedInt(stringReader);
            writer.Write("cube");
            writer.Write((byte)2);          // parent bone: child
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(2.5f);             // bounding sphere radius
            writer.Write7BitEncodedInt(0);  // mesh tag: null

            // two mesh parts, both naming shared resource 1 for their vertex buffer
            writer.Write(2);
            WriteMeshPart(writer, startIndex: 0, vertexBufferIndex: 1);
            WriteMeshPart(writer, startIndex: 3, vertexBufferIndex: 1);

            writer.Write((byte)1);          // model root bone
            writer.Write7BitEncodedInt(stringReader);
            writer.Write("model-tag");

            // -- shared resource 1: the vertex buffer --------------------------------------------
            writer.Write7BitEncodedInt(vertexBufferReader);
            WriteVertexDeclaration(writer);
            writer.Write(6);                // six vertices
            for (int vertex = 0; vertex < 6; vertex++)
            {
                writer.Write((float)vertex);
                writer.Write(0f);
                writer.Write(0f);
                writer.Write((uint)0xFF0000FFu);
            }

            // -- shared resource 2: the index buffer ---------------------------------------------
            writer.Write7BitEncodedInt(indexBufferReader);
            writer.Write(true);             // sixteen-bit
            writer.Write(12);               // twelve bytes, so six indices
            for (ushort index = 0; index < 6; index++)
            {
                writer.Write(index);
            }

            // -- shared resource 3: the effect ---------------------------------------------------
            writer.Write7BitEncodedInt(basicEffectReader);
            writer.Write(string.Empty);     // no texture reference
            writer.Write(1f); writer.Write(1f); writer.Write(1f);   // diffuse
            writer.Write(0f); writer.Write(0f); writer.Write(0f);   // emissive
            writer.Write(0f); writer.Write(0f); writer.Write(0f);   // specular
            writer.Write(16f);              // specular power
            writer.Write(1f);               // alpha
            writer.Write(true);             // vertex colour enabled
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
            writer.Write((byte)0);
            writer.Write(10 + bytes.Length);
            writer.Write(bytes);
        }

        return container.ToArray();

        static void WriteMeshPart(BinaryWriter writer, int startIndex, int vertexBufferIndex)
        {
            writer.Write(0);                        // vertex offset
            writer.Write(6);                        // vertex count
            writer.Write(startIndex);
            writer.Write(1);                        // primitive count
            writer.Write7BitEncodedInt(0);          // part tag: null
            writer.Write7BitEncodedInt(vertexBufferIndex);
            writer.Write7BitEncodedInt(2);          // index buffer
            writer.Write7BitEncodedInt(3);          // effect
        }

        static void WriteVertexDeclaration(BinaryWriter writer)
        {
            writer.Write(16);                       // stride: Vector3 + packed colour
            writer.Write(2);                        // two elements
            writer.Write(0);
            writer.Write((int)VertexElementFormat.Vector3);
            writer.Write((int)VertexElementUsage.Position);
            writer.Write(0);
            writer.Write(12);
            writer.Write((int)VertexElementFormat.Color);
            writer.Write((int)VertexElementUsage.Color);
            writer.Write(0);
        }

        static void WriteMatrix(BinaryWriter writer, Matrix value)
        {
            writer.Write(value.M11); writer.Write(value.M12); writer.Write(value.M13); writer.Write(value.M14);
            writer.Write(value.M21); writer.Write(value.M22); writer.Write(value.M23); writer.Write(value.M24);
            writer.Write(value.M31); writer.Write(value.M32); writer.Write(value.M33); writer.Write(value.M34);
            writer.Write(value.M41); writer.Write(value.M42); writer.Write(value.M43); writer.Write(value.M44);
        }
    }
}
