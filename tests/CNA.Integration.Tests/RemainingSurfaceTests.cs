using CNA.Content;
using CNA.Graphics;
using CNA.Media;
using Xunit;
using Xunit.Abstractions;

namespace CNA.Integration.Tests;

/// <summary>
/// The last native-backed types that can be reached headless.
///
/// Written after measuring what "runtime coverage" honestly means: of the compat surface, only
/// about ninety types cross the ABI at all -- the rest are managed math, packed vectors, enums and
/// interfaces, which have no native side to verify and are covered by the unit suite and the enum
/// parity tests instead. Of those ninety, these are the ones still reachable without a device,
/// a user profile, or authored XACT banks.
/// </summary>
[Collection(NativeGameCollection.Name)]
public class RemainingSurfaceTests(ITestOutputHelper output, NativeGameFixture fixture)
{
    /// <summary>The dynamic buffer pair, which are separate native resources from their static
    /// counterparts rather than a flag on them.</summary>
    [Native3DFact]
    public void DynamicBuffers_CreateAndAcceptData()
    {
        fixture.InsideAFrameWithDevice(device =>
        {
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
                device, VertexPositionColor.VertexDeclaration, 3, BufferUsage.WriteOnly);
            vertices.SetData(
            [
                new VertexPositionColor(Vector3.Zero, Color.Red),
                new VertexPositionColor(Vector3.UnitX, Color.Green),
                new VertexPositionColor(Vector3.UnitY, Color.Blue),
            ]);

            using var indices = new DynamicIndexBuffer(
                device, IndexElementSize.SixteenBits, 3, BufferUsage.WriteOnly);
            indices.SetData<ushort>([0, 1, 2]);

            output.WriteLine($"{vertices.VertexCount} vertices, {indices.IndexCount} indices");

            Assert.Equal(3, vertices.VertexCount);
            Assert.Equal(3, indices.IndexCount);
        });
    }

    /// <summary>The three directional lights a stock effect owns. Each is an independently owned
    /// native handle that outlives its parent effect -- confirmed upstream in BasicEffectSmoke.c --
    /// which is why they are released explicitly rather than with the effect.</summary>
    [NativeFact]
    public void DirectionalLights_RoundTripTheirProperties()
    {
        fixture.InsideAFrameWithDevice(device =>
        {
            using var effect = new BasicEffect(device);

            DirectionalLight light = effect.DirectionalLight0;

            light.Enabled = true;
            light.Direction = new Vector3(0f, -1f, 0f);
            light.DiffuseColor = new Vector3(1f, 0.5f, 0.25f);

            output.WriteLine($"enabled={light.Enabled} dir={light.Direction} diffuse={light.DiffuseColor}");

            Assert.True(light.Enabled);
            Assert.Equal(-1f, light.Direction.Y, 1e-4f);
            Assert.Equal(0.5f, light.DiffuseColor.Y, 1e-4f);
        });
    }

    /// <summary>Effect annotations. Stock effects carry none, so an empty collection is the
    /// expected answer -- what is being established is that the collection is reachable and
    /// answers a count rather than failing, since a custom effect's annotations arrive the same
    /// way.</summary>
    [NativeFact]
    public void EffectAnnotations_AreReachableFromParametersAndTechniques()
    {
        fixture.InsideAFrameWithDevice(device =>
        {
            using var effect = new BasicEffect(device);

            EffectAnnotationCollection techniqueAnnotations = effect.CurrentTechnique.Annotations;
            output.WriteLine($"technique annotations: {techniqueAnnotations.Count}");

            Assert.True(techniqueAnnotations.Count >= 0);

            if (effect.Parameters.Count > 0)
            {
                EffectAnnotationCollection parameterAnnotations = effect.Parameters[0].Annotations;
                output.WriteLine($"parameter '{effect.Parameters[0].Name}' annotations: {parameterAnnotations.Count}");
                Assert.True(parameterAnnotations.Count >= 0);
            }
        });
    }

    [NativeFact]
    public void EffectMaterial_PreservesParametersAndClonesAsBaseEffect()
    {
        fixture.InsideAFrameWithDevice(device =>
        {
            using var source = new BasicEffect(device);
            source.Parameters["DiffuseColor"]!.SetValue(new Vector4(0.25f, 0.5f, 0.75f, 0.5f));
            using var material = new EffectMaterial(source);
            Assert.Equal(source.Techniques.Count, material.Techniques.Count);
            Assert.Equal(source.Techniques[0].Name, material.Techniques[0].Name);
            Assert.Equal(new Vector4(0.25f, 0.5f, 0.75f, 0.5f), material.Parameters["DiffuseColor"]!.GetValueVector4());
            using Effect clone = material.Clone();
            Assert.IsType<Effect>(clone);
            Assert.Equal(material.Parameters["DiffuseColor"]!.GetValueVector4(), clone.Parameters["DiffuseColor"]!.GetValueVector4());
            material.Parameters["DiffuseColor"]!.SetValue(Vector4.One);
            Assert.Equal(new Vector4(0.25f, 0.5f, 0.75f, 0.5f), clone.Parameters["DiffuseColor"]!.GetValueVector4());
        });
    }

    /// <summary>
    /// A drawable component, which uses a different native create route from a plain one
    /// (<c>cna_drawable_game_component_create</c>) and carries Draw and Visible on top.
    /// </summary>
    [NativeFact]
    public void DrawableGameComponent_ReportsItsDrawState()
    {
        fixture.InsideAFrame(game =>
        {
            using var component = new ProbeDrawable(game);

            component.Visible = true;
            component.DrawOrder = 3;

            output.WriteLine($"visible={component.Visible} order={component.DrawOrder} enabled={component.Enabled}");

            Assert.True(component.Visible);
            Assert.Equal(3, component.DrawOrder);
        });
    }

    private sealed class ProbeDrawable(CNA.Game game) : DrawableGameComponent(game)
    {
    }

    /// <summary>The component collection as a collection: add, count, contains, remove. Native owns
    /// the list, so each of those is a separate route rather than a managed list operation.</summary>
    [NativeFact]
    public void GameComponentCollection_AddsCountsAndRemoves()
    {
        fixture.InsideAFrame(game =>
        {
            int before = game.Components.Count;

            using var component = new ProbeDrawable(game);
            game.Components.Add(component);

            Assert.Equal(before + 1, game.Components.Count);
            Assert.Contains(component, game.Components);

            Assert.True(game.Components.Remove(component));
            Assert.Equal(before, game.Components.Count);
        });
    }

    /// <summary>
    /// The media library's picture side, which is the one collection with real content on this
    /// machine -- the library reported seventeen. Songs are empty here, so this is the only place
    /// the collection machinery gets exercised against actual elements rather than an empty set.
    /// </summary>
    [NativeFact]
    public void MediaLibrary_PictureCollection_EnumeratesRealElements()
    {
        fixture.InsideAFrame(_ =>
        {
            using var library = new MediaLibrary();

            PictureCollection pictures = library.Pictures;
            output.WriteLine($"{pictures.Count} picture(s)");

            if (pictures.Count == 0)
            {
                output.WriteLine("no pictures on this machine; the count route still answered");
                return;
            }

            Picture first = pictures[0];
            output.WriteLine($"first: '{first.Name}' album='{first.Album?.Name}' width={first.Width}");

            Assert.NotNull(first.Name);

            // Indexing twice must hand back the same wrapper -- the per-index cache is what stops a
            // library-owned element being disposed twice.
            Assert.Same(first, pictures[0]);
        });
    }

    /// <summary>Media sources, the enumeration a library can be opened from.</summary>
    [NativeFact]
    public void MediaSource_EnumeratesAvailableSources()
    {
        fixture.InsideAFrame(_ =>
        {
            IReadOnlyList<MediaSource> sources = MediaSource.GetAvailableMediaSources();

            output.WriteLine($"{sources.Count} source(s)");

            foreach (MediaSource source in sources)
            {
                output.WriteLine($"  '{source.Name}' {source.MediaSourceType}");
                Assert.NotNull(source.Name);
            }

            Assert.NotEmpty(sources);
        });
    }

    /// <summary>GraphicsResource's shared surface -- Name and Tag -- on a concrete resource. Both
    /// cross the ABI, and Name is a two-call string copy.</summary>
    [NativeFact]
    public void GraphicsResource_NameAndTagRoundTrip()
    {
        fixture.InsideAFrameWithDevice(device =>
        {
            using var texture = new Texture2D(device, 1, 1);

            texture.Name = "probe-texture";
            Assert.Equal("probe-texture", texture.Name);

            var tag = new object();
            texture.Tag = tag;
            Assert.Same(tag, texture.Tag);

            output.WriteLine($"name='{texture.Name}' disposed={texture.IsDisposed}");
            Assert.False(texture.IsDisposed);
        });
    }

    /// <summary>
    /// The picture album tree, which is the one media hierarchy with real content here: this
    /// machine has no music but seventeen pictures, and the first reported album 'Camera'.
    ///
    /// Reaching it exercises the shared collection machinery -- ReadOnlyMediaCollection and
    /// MediaLibraryObject underneath every media collection -- against actual elements rather than
    /// an empty set, which is the only place that happens.
    /// </summary>
    [NativeFact]
    public void MediaLibrary_PictureAlbums_WalkTheTree()
    {
        fixture.InsideAFrame(_ =>
        {
            using var library = new MediaLibrary();

            PictureCollection pictures = library.Pictures;
            if (pictures.Count == 0)
            {
                output.WriteLine("no pictures on this machine; nothing to walk");
                return;
            }

            PictureAlbum? album = pictures[0].Album;
            if (album is null)
            {
                output.WriteLine("pictures exist but report no album");
                return;
            }

            output.WriteLine(
                $"album '{album.Name}' has {album.Pictures.Count} picture(s) and {album.Albums.Count} sub-album(s); " +
                $"parent={album.Parent?.Name ?? "none"}");

            Assert.NotNull(album.Name);
            Assert.True(album.Pictures.Count >= 0);

            // Same wrapper twice: the per-index cache is what stops a library-owned element being
            // disposed by two owners.
            Assert.Same(album.Pictures, album.Pictures);
        });
    }

    /// <summary>
    /// A content type reader built from the registry by canonical name. This is the read-only view
    /// of a reader the registry already holds -- distinct from ManagedContentTypeReader, which is
    /// the derivable one, and reachable because the built-in readers register themselves.
    /// </summary>
    [NativeFact]
    public void ContentTypeReader_IsCreatableForABuiltInReader()
    {
        fixture.InsideAFrame(_ =>
        {
            const string TextureReader =
                "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework.Graphics, " +
                "Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553";

            if (!ContentTypeReaderManager.IsRegistered(TextureReader))
            {
                output.WriteLine("the Texture2D reader is not registered in this build; nothing to create");
                return;
            }

            using ContentTypeReader reader = ContentTypeReaderManager.CreateReader(TextureReader);

            output.WriteLine($"target='{reader.TargetTypeName}' version={reader.TypeVersion}");
            Assert.False(string.IsNullOrEmpty(reader.TargetTypeName));
        });
    }

    /// <summary>
    /// Every cached collection property answers the same wrapper twice.
    ///
    /// One line per property, and it is the only thing that distinguishes a cache from a leak from
    /// the outside. Each of these getters mints a new owned native handle, so a property that
    /// answers a different object each read leaks one per call -- which stops the game being
    /// destroyed, because these are game-child resources, and then surfaces as an unrelated game
    /// failing to create.
    ///
    /// Written after exactly that was found in the media family by an almost-afterthought
    /// Assert.Same on two reads of album.Pictures. Cheap enough to apply to every family rather
    /// than wait for the next one to be found by accident.
    /// </summary>
    [NativeFact]
    public void CachedCollectionProperties_AnswerTheSameWrapperTwice()
    {
        fixture.InsideAFrameWithDevice(device =>
        {
            using var library = new MediaLibrary();

            Assert.Same(library.Songs, library.Songs);
            Assert.Same(library.Albums, library.Albums);
            Assert.Same(library.Artists, library.Artists);
            Assert.Same(library.Genres, library.Genres);
            Assert.Same(library.Playlists, library.Playlists);
            Assert.Same(library.Pictures, library.Pictures);

            if (library.Pictures.Count > 0 && library.Pictures[0].Album is { } album)
            {
                Assert.Same(album.Pictures, album.Pictures);
                Assert.Same(album.Albums, album.Albums);
            }

            using var effect = new BasicEffect(device);
            Assert.Same(effect.Parameters, effect.Parameters);
            Assert.Same(effect.Techniques, effect.Techniques);
            Assert.Same(effect.CurrentTechnique, effect.CurrentTechnique);
            Assert.Same(effect.CurrentTechnique.Passes, effect.CurrentTechnique.Passes);

            output.WriteLine("every cached collection property answered one wrapper");
        });
    }
}
