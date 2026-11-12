using CNA.Graphics;
using Xunit;

namespace CNA.Tests;

/// <summary>Same testability limitation as VertexBufferTests -- only constructor argument
/// validation is reachable without a real cna-native.</summary>
public class IndexBufferTests
{
    private static GraphicsDevice CreateDummyDevice() => new(nativeGameHandleValue: 0);

    [Fact]
    public void Constructor_NullGraphicsDevice_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new IndexBuffer(null!, IndexElementSize.SixteenBits, 3, BufferUsage.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveIndexCount_Throws(int indexCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new IndexBuffer(CreateDummyDevice(), IndexElementSize.SixteenBits, indexCount, BufferUsage.None));
    }

    [Theory]
    [InlineData(typeof(short), IndexElementSize.SixteenBits)]
    [InlineData(typeof(ushort), IndexElementSize.SixteenBits)]
    [InlineData(typeof(int), IndexElementSize.ThirtyTwoBits)]
    [InlineData(typeof(uint), IndexElementSize.ThirtyTwoBits)]
    [InlineData(typeof(float), IndexElementSize.ThirtyTwoBits)]
    [InlineData(typeof(bool), IndexElementSize.ThirtyTwoBits)]
    public void SizeForType_ValidIndexType_ReturnsMatchingElementSize(Type indexType, IndexElementSize expected)
    {
        Assert.Equal(expected, IndexBuffer.SizeForType(indexType));
    }

    [Fact]
    public void SizeForType_UnsupportedType_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IndexBuffer.SizeForType(typeof(double)));
    }

    [Fact]
    public void SizeForType_NullType_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => IndexBuffer.SizeForType(null!));
    }

    [Fact]
    public void Constructor_InvalidIndexType_Throws()
    {
        // SizeForType(indexType) runs in the constructor initializer, before the chained
        // constructor's own body (and thus before any native call), so this is testable the same
        // way VertexBuffer's Type-taking constructor is.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new IndexBuffer(CreateDummyDevice(), typeof(double), 3, BufferUsage.None));
    }
}
