using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Xunit;

// NOT under CNA, for the reason CompatLayerIntegrationTests records at length: an enclosing
// namespace's members shadow a using directive's imports, so inside `namespace CNA.XnaCompat.Tests`
// the `Vector2` in `Load<Vector2[]>` binds to CNA.Vector2 rather than the compat type this file is
// testing. The failure is a cast exception from deep inside the reader, naming neither the
// namespace nor the shadowing. It is the same constraint a ported game lives under.
namespace CnaDotnet.XnaCompat.Tests.Content;

/// <summary>
/// XNA's built-in content readers, against hand-built <c>.xnb</c> assets.
///
/// <b>Why hand-built.</b> There is no XNA content pipeline here to compile a real asset with, so
/// the fixtures are written byte by byte from the format the decompiled XNA 4.0 readers define.
/// That is a genuine limit on what these prove -- they prove this reader agrees with that reading
/// of the format, not that a pipeline-produced file round-trips -- and it is why every format below
/// was transcribed from the decompiled reader rather than inferred from the type it produces. Two
/// would have been plausible to infer and wrong: <c>DateTime</c> packs its <c>Kind</c> into the top
/// two bits of the tick count, and <c>Decimal</c> is four <c>Int32</c> bits words.
///
/// <b>What they are actually defending.</b> The collection readers, and specifically the rule that
/// decides how an element is written: a value type inline, a reference type behind its own
/// type-index prefix. Getting that backwards does not fail where the mistake is -- it misreads
/// every byte after the first element, and produces a plausible object graph from garbage. The
/// <c>List&lt;string&gt;</c> case exists only to hold that distinction in place, because it is the
/// one that exercises the branch the value-type cases do not.
/// </summary>
public class BuiltInContentReaderTests
{
    private const string Xna = ", Microsoft.Xna.Framework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553";
    private const string Corlib = ", mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

    [Fact]
    public void ListOfInt32_ReadsElementsInline()
    {
        List<int> value = Load<List<int>>(
            ["Microsoft.Xna.Framework.Content.ListReader`1[[System.Int32" + Corlib + "]]" + Xna,
             "Microsoft.Xna.Framework.Content.Int32Reader" + Xna],
            writer =>
            {
                writer.Write7BitEncodedInt(1);
                writer.Write(3);
                writer.Write(10);
                writer.Write(20);
                writer.Write(30);
            });

        Assert.Equal([10, 20, 30], value);
    }

    /// <summary>
    /// The reference-element case. Each string carries its own type-reader index, and a reader that
    /// treated it like a value type would consume that index as the string's length prefix.
    /// </summary>
    [Fact]
    public void ListOfString_ReadsElementsThroughTheTypeIndex()
    {
        List<string> value = Load<List<string>>(
            ["Microsoft.Xna.Framework.Content.ListReader`1[[System.String" + Corlib + "]]" + Xna,
             "Microsoft.Xna.Framework.Content.StringReader" + Xna],
            writer =>
            {
                writer.Write7BitEncodedInt(1);
                writer.Write(2);
                writer.Write7BitEncodedInt(2);
                writer.Write("alpha");
                writer.Write7BitEncodedInt(2);
                writer.Write("beta");
            });

        Assert.Equal(["alpha", "beta"], value);
    }

    [Fact]
    public void ArrayOfVector2_ReadsElementsInline()
    {
        Vector2[] value = Load<Vector2[]>(
            ["Microsoft.Xna.Framework.Content.ArrayReader`1[[Microsoft.Xna.Framework.Vector2" + Xna + "]]" + Xna,
             "Microsoft.Xna.Framework.Content.Vector2Reader" + Xna],
            writer =>
            {
                writer.Write7BitEncodedInt(1);
                writer.Write(2);
                writer.Write(1f);
                writer.Write(2f);
                writer.Write(3f);
                writer.Write(4f);
            });

        Assert.Equal([new Vector2(1f, 2f), new Vector2(3f, 4f)], value);
    }

    /// <summary>A dictionary is the mixed case: reference keys tagged, value-type values inline.</summary>
    [Fact]
    public void DictionaryOfStringToInt32_ReadsBothSidesByTheirOwnRule()
    {
        Dictionary<string, int> value = Load<Dictionary<string, int>>(
            ["Microsoft.Xna.Framework.Content.DictionaryReader`2[[System.String" + Corlib + "],[System.Int32" + Corlib + "]]" + Xna,
             "Microsoft.Xna.Framework.Content.StringReader" + Xna,
             "Microsoft.Xna.Framework.Content.Int32Reader" + Xna],
            writer =>
            {
                writer.Write7BitEncodedInt(1);
                writer.Write(2);
                writer.Write7BitEncodedInt(2);
                writer.Write("one");
                writer.Write(1);
                writer.Write7BitEncodedInt(2);
                writer.Write("two");
                writer.Write(2);
            });

        Assert.Equal(2, value.Count);
        Assert.Equal(1, value["one"]);
        Assert.Equal(2, value["two"]);
    }

    [Fact]
    public void NullableOfInt32_ReadsTheFlagThenTheRawValue()
    {
        string[] readers =
        [
            "Microsoft.Xna.Framework.Content.NullableReader`1[[System.Int32" + Corlib + "]]" + Xna,
            "Microsoft.Xna.Framework.Content.Int32Reader" + Xna,
        ];

        Assert.Equal(42, Load<int?>(readers, writer =>
        {
            writer.Write7BitEncodedInt(1);
            writer.Write(true);
            writer.Write(42);
        }));

        Assert.Null(Load<int?>(readers, writer =>
        {
            writer.Write7BitEncodedInt(1);
            writer.Write(false);
        }));
    }

    /// <summary>
    /// An enum reads as its underlying type, which the file never records. The reader is closed
    /// over the real CLR enum, so it can ask -- and this fixture uses a <c>byte</c>-backed enum
    /// precisely because a reader that assumed <c>Int32</c> would read three bytes too many and
    /// pass anyway on an Int32-backed one.
    /// </summary>
    [Fact]
    public void EnumOfAByteBackedEnum_ReadsItsUnderlyingWidth()
    {
        ByteBacked value = Load<ByteBacked>(
            ["Microsoft.Xna.Framework.Content.EnumReader`1[[" + typeof(ByteBacked).AssemblyQualifiedName + "]]" + Xna,
             "Microsoft.Xna.Framework.Content.ByteReader" + Xna],
            writer =>
            {
                writer.Write7BitEncodedInt(1);
                writer.Write((byte)2);
            });

        Assert.Equal(ByteBacked.Third, value);
    }

    [Fact]
    public void MathAndTimeValues_MatchTheDecompiledFormats()
    {
        Assert.Equal(new Rectangle(1, 2, 3, 4), Load<Rectangle>(
            ["Microsoft.Xna.Framework.Content.RectangleReader" + Xna],
            writer =>
            {
                writer.Write7BitEncodedInt(1);
                writer.Write(1);
                writer.Write(2);
                writer.Write(3);
                writer.Write(4);
            }));

        Assert.Equal(new Point(5, 6), Load<Point>(
            ["Microsoft.Xna.Framework.Content.PointReader" + Xna],
            writer =>
            {
                writer.Write7BitEncodedInt(1);
                writer.Write(5);
                writer.Write(6);
            }));

        Assert.Equal(TimeSpan.FromSeconds(90), Load<TimeSpan>(
            ["Microsoft.Xna.Framework.Content.TimeSpanReader" + Xna],
            writer =>
            {
                writer.Write7BitEncodedInt(1);
                writer.Write(TimeSpan.FromSeconds(90).Ticks);
            }));

        Assert.Equal(12.34m, Load<decimal>(
            ["Microsoft.Xna.Framework.Content.DecimalReader" + Xna],
            writer =>
            {
                writer.Write7BitEncodedInt(1);
                foreach (int part in decimal.GetBits(12.34m))
                {
                    writer.Write(part);
                }
            }));

        var utc = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(utc, Load<DateTime>(
            ["Microsoft.Xna.Framework.Content.DateTimeReader" + Xna],
            writer =>
            {
                writer.Write7BitEncodedInt(1);
                writer.Write(utc.Ticks | ((long)DateTimeKind.Utc << 62));
            }));
    }

    /// <summary>
    /// The reader a real game's level file uses: a plain data class the pipeline serialized from
    /// XML, with no reader of its own.
    ///
    /// The shape here is deliberately the awkward one. It has a base class whose members are read
    /// first, a private field carrying <c>ContentSerializerAttribute</c> that must be included, a
    /// public field carrying <c>ContentSerializerIgnoreAttribute</c> that must not be, a read-only
    /// <c>List&lt;T&gt;</c> that is read *into* rather than assigned, and a nested collection.
    /// Every one of those changes the byte layout, so a reader that got any of them wrong would
    /// not merely lose a field -- it would misread everything after it.
    /// </summary>
    [Fact]
    public void ReflectiveReader_ReadsBaseThenPropertiesThenFields()
    {
        LevelData value = Load<LevelData>(
            ["Microsoft.Xna.Framework.Content.ReflectiveReader`1[[" + typeof(LevelData).AssemblyQualifiedName + "]]" + Xna,
             "Microsoft.Xna.Framework.Content.ReflectiveReader`1[[" + typeof(LevelHeader).AssemblyQualifiedName + "]]" + Xna,
             "Microsoft.Xna.Framework.Content.StringReader" + Xna,
             "Microsoft.Xna.Framework.Content.Int32Reader" + Xna,
             "Microsoft.Xna.Framework.Content.ListReader`1[[Microsoft.Xna.Framework.Point" + Xna + "]]" + Xna,
             "Microsoft.Xna.Framework.Content.PointReader" + Xna],
            writer =>
            {
                writer.Write7BitEncodedInt(1);

                // The base type's members come first: Name is a public property.
                writer.Write7BitEncodedInt(3);
                writer.Write("caverns");

                // LevelData's own property, then its two included fields in declaration order.
                writer.Write(7);                    // Difficulty (public property, inline value type)
                writer.Write7BitEncodedInt(5);      // Spawns (read-only List<Point>, tagged reference)
                writer.Write(2);
                writer.Write(1);
                writer.Write(2);
                writer.Write(3);
                writer.Write(4);
                writer.Write(99);                   // _secret (private, opted in)
            });

        Assert.Equal("caverns", value.Name);
        Assert.Equal(7, value.Difficulty);
        Assert.Equal([new Point(1, 2), new Point(3, 4)], value.Spawns);
        Assert.Equal(99, value.Secret);
        Assert.Equal(0, value.Ignored);
    }

    /// <summary>
    /// Every platform byte CNA accepts loads here too.
    ///
    /// This path used to accept three -- XNA's own 'w', 'm' and 'x' -- while the native loader
    /// accepted all sixteen, so one binding gave two answers for one file: an asset MonoGame built
    /// for DesktopGL ('d') loaded natively and was refused here. Six assets in CNA's own MonoGame
    /// test corpus are exactly that.
    ///
    /// The list is asserted element by element rather than by count, so adding a byte to one side
    /// without the other fails here and names which byte.
    /// </summary>
    [Theory]
    [InlineData('w')] [InlineData('x')] [InlineData('m')] [InlineData('i')]
    [InlineData('a')] [InlineData('d')] [InlineData('X')] [InlineData('W')]
    [InlineData('n')] [InlineData('u')] [InlineData('p')] [InlineData('M')]
    [InlineData('r')] [InlineData('P')] [InlineData('g')] [InlineData('l')]
    public void EveryPlatformCnaAcceptsIsLoadable(char platform)
    {
        int value = Load<int>(
            ["Microsoft.Xna.Framework.Content.Int32Reader" + Corlib],
            writer =>
            {
                writer.Write7BitEncodedInt(1);
                writer.Write(1234);
            },
            platform);

        Assert.Equal(1234, value);
    }

    /// <summary>
    /// A byte outside that set is refused, and the message names it.
    ///
    /// 'b' is the real case: KNI stamps it on web builds, 140 assets on this machine carry it, and
    /// neither FNA nor CNA accepts it. Matching CNA is a fix; going past CNA would be a decision
    /// about a format nothing else here supports, so this asserts the refusal rather than leaving
    /// the boundary untested.
    /// </summary>
    [Fact]
    public void APlatformCnaRejects_IsRefusedByName()
    {
        ContentLoadException thrown = Assert.Throws<ContentLoadException>(() => Load<int>(
            ["Microsoft.Xna.Framework.Content.Int32Reader" + Corlib],
            writer =>
            {
                writer.Write7BitEncodedInt(1);
                writer.Write(1234);
            },
            'b'));

        Assert.Contains("'b'", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnresolvableElementType_FailsByNameRatherThanMisreading()
    {
        ContentLoadException thrown = Assert.Throws<ContentLoadException>(() => Load<List<int>>(
            ["Microsoft.Xna.Framework.Content.ListReader`1[[Nowhere.NoSuchType, NoSuchAssembly]]" + Xna],
            writer => writer.Write7BitEncodedInt(1)));

        Assert.Contains("ListReader", thrown.Message, StringComparison.Ordinal);
    }

    public class LevelHeader
    {
        public string Name { get; set; } = string.Empty;
    }

    public sealed class LevelData : LevelHeader
    {
#pragma warning disable CS0649 // Assigned by the reflective reader, which the compiler cannot see.
        // Private, so it is serialized only because it opts in. Without the attribute the reader
        // must skip it -- and the four bytes written for it would then be read as whatever comes
        // next, which is why the fixture puts it last.
        [ContentSerializer]
        private int _secret;
#pragma warning restore CS0649

        public int Difficulty { get; set; }

        // Read-only, so it is read into rather than assigned -- which is only allowed because a
        // List<T> reader can deserialize into an existing object.
        public List<Point> Spawns { get; } = [];

        [ContentSerializerIgnore]
        public int Ignored;

        internal int Secret => _secret;
    }

    // No device or native library: an impossible count must not bypass the device lookup.
    [Theory]
    [InlineData("Texture2D")]
    [InlineData("TextureCube")]
    [InlineData("Texture3D")]
    public void TextureHeader_DoesNotRejectALargeLevelCountBeforeLookingUpTheDevice(string kind)
    {
        ContentLoadException failure = Assert.Throws<ContentLoadException>(() => Load<object>(
            ["Microsoft.Xna.Framework.Content." + kind + "Reader" + Xna],
            writer =>
            {
                writer.Write7BitEncodedInt(1);
                writer.Write(0); // Color
                writer.Write(1);
                if (kind != "TextureCube")
                {
                    writer.Write(1);
                }
                if (kind == "Texture3D")
                {
                    writer.Write(1);
                }
                writer.Write(33);
            }));

        Assert.Equal("Content asset 'asset' contains a graphics resource, but its ContentManager's service " +
            "provider supplies no IGraphicsDeviceService with a GraphicsDevice.", failure.Message);
    }

    private enum ByteBacked : byte
    {
        First = 0,
        Second = 1,
        Third = 2,
    }

    private static T Load<T>(string[] readerNames, Action<BinaryWriter> writeBody, char platform = 'w')
    {
        using var content = new MemoryContentManager(BuildAsset(readerNames, writeBody, platform));
        return content.Load<T>("asset");
    }

    private static byte[] BuildAsset(string[] readerNames, Action<BinaryWriter> writeBody, char platform = 'w')
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
            writer.Write((byte)platform);
            writer.Write((byte)5);
            writer.Write((byte)0);
            writer.Write(10 + bytes.Length);
            writer.Write(bytes);
        }

        return container.ToArray();
    }

    private sealed class MemoryContentManager(byte[] asset) : ContentManager(new NullServiceProvider())
    {
        protected override Stream OpenStream(string assetName) => new MemoryStream(asset, writable: false);
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
