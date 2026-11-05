using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using CNA.Interop;
using Xunit;

namespace CNA.Tests;

/// <summary>
/// <see cref="CnaAbi.Decode"/> is pure arithmetic over the encoding <c>abi.h</c> defines, so it is
/// testable without a native library -- and worth testing, because a wrong shift or mask produces a
/// plausible-looking version rather than an obvious failure, which is exactly the situation the
/// check exists to prevent.
///
/// Native admission runs in isolated fixture processes. These tests pin its pure version matrix
/// and prove that its required-symbol set is the complete managed import surface.
/// </summary>
public class CnaAbiTests
{
    /// <summary>The encoding is <c>(major &amp; 0xFFFF) &lt;&lt; 16 | (minor &amp; 0xFF) &lt;&lt; 8 |
    /// (patch &amp; 0xFF)</c> -- 16 bits for major, 8 each for the rest, which is asymmetric and the
    /// easiest part to get wrong.</summary>
    [Theory]
    [InlineData(0u, 0, 0, 0)]
    [InlineData((1u << 8), 0, 1, 0)]
    [InlineData((1u << 16), 1, 0, 0)]
    [InlineData(1u, 0, 0, 1)]
    [InlineData((2u << 16) | (3u << 8) | 4u, 2, 3, 4)]
    [InlineData((0xFFFFu << 16) | (0xFFu << 8) | 0xFFu, 0xFFFF, 0xFF, 0xFF)]
    public void Decode_SplitsTheFieldsAtTheDocumentedWidths(uint encoded, int major, int minor, int patch)
    {
        Assert.Equal((major, minor, patch), CnaAbi.Decode(encoded));
    }

    /// <summary>
    /// The constant and reviewed point matrix advance together; ABI 0.60.0 changes behavior
    /// despite preserving every imported prototype and existing layout.
    /// </summary>
    [Fact]
    public void ExpectedVersion_IsTheAbiThisBindingWasWrittenAgainst()
    {
        Assert.Equal((0, 60, 0), CnaAbi.Decode(CnaAbi.ExpectedVersion));
    }

    /// <summary>Round-trips every field independently, so a mask that swallowed a neighbouring
    /// field's bits would show up rather than cancel out.</summary>
    [Fact]
    public void Decode_FieldsDoNotBleedIntoEachOther()
    {
        Assert.Equal((0, 0, 0xFF), CnaAbi.Decode(0xFFu));
        Assert.Equal((0, 0xFF, 0), CnaAbi.Decode(0xFF00u));
        Assert.Equal((0xFFFF, 0, 0), CnaAbi.Decode(0xFFFF0000u));
    }

    [Theory]
    [InlineData(0, 60, 0, "exact")]
    public void Policy_AcceptsOnlyReviewedAbiGenerations(int major, int minor, int patch, string classification)
    {
        uint version = ((uint)major << 16) | ((uint)minor << 8) | (uint)patch;
        Assert.True(CnaNativeAbiPolicy.TryGetProfile(version, out CnaNativeAbiProfile profile));
        Assert.Equal(classification, profile.Compatibility);
    }

    /// <summary>
    /// Previously accepted generations remain rejected. Immediate neighbors and an unreviewed
    /// patch keep the matrix a point list, even when all required symbols and shapes match.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 6, 0)]
    [InlineData(0, 7, 0)]
    [InlineData(0, 8, 0)]
    [InlineData(0, 19, 0)]
    [InlineData(0, 20, 0)]
    [InlineData(0, 21, 0)]
    [InlineData(0, 34, 0)]
    [InlineData(0, 35, 0)]
    [InlineData(0, 36, 0)]
    [InlineData(0, 37, 0)]
    [InlineData(0, 38, 0)]
    [InlineData(0, 39, 0)]
    [InlineData(0, 40, 0)]
    [InlineData(0, 41, 0)]
    [InlineData(0, 42, 0)]
    [InlineData(0, 43, 0)]
    [InlineData(0, 44, 0)]
    [InlineData(0, 45, 0)]
    [InlineData(0, 46, 0)]
    [InlineData(0, 47, 0)]
    [InlineData(0, 47, 1)]
    [InlineData(0, 59, 0)]
    [InlineData(0, 60, 1)]
    [InlineData(0, 61, 0)]
    [InlineData(0, 48, 0)]
    [InlineData(1, 0, 0)]
    public void Policy_RejectsUnauditedVersions(int major, int minor, int patch)
    {
        uint version = ((uint)major << 16) | ((uint)minor << 8) | (uint)patch;
        Assert.False(CnaNativeAbiPolicy.TryGetProfile(version, out _));
    }

    [Fact]
    public void RequiredSymbols_AreEveryDeclaredNativeImport()
    {
        string[] declared = typeof(Native)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Select(method => (Method: method, Import: method.GetCustomAttribute<LibraryImportAttribute>()))
            .Where(item => string.Equals(item.Import?.LibraryName, "cna-native", StringComparison.Ordinal))
            .Select(item => string.IsNullOrWhiteSpace(item.Import!.EntryPoint)
                ? item.Method.Name
                : item.Import.EntryPoint!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // 1384: the 1002 the 0.21.0 binding imported, less the 44 engine-layer routes CNA retired in
        // 0.30.0 and cna_graphics_ext_is_available, whose only caller described that layer (957),
        // plus the 239 routes of gamer_services.h and the 187 of net.h, net_gamers.h and
        // net_sessions.h that the XNA GamerServices/Avatar/Net surface is built on, plus 0.36.0's
        // cna_packet_reader_copy_data_ext, which XNA's managed PacketReader receives through (1384),
        // plus the 21 devices.h/sensors.h routes of the opt-in phone assembly CNA.PhoneCompat (1405),
        // plus 0.37.0's four routes naming the canonical exception behind a failure (1409),
        // plus 0.38.0's cna_game_run_frame_ext, the run a browser host drives (1410),
        // plus the touch panel's mouse-as-touch bridge a phone title turns on (1412, CSX-098),
        // plus 0.39.0's cna_game_set_foreign_thread_calls_ext, for XNA's loading threads (1413),
        // plus 0.40.0's cna_game_window_get/set_is_borderless_ext, the border a Windows Forms game
        // removes with FormBorderStyle.None (1415, CSX-114), plus 0.41.0's
        // cna_game_run_foreign_thread_calls_ext, which the game thread runs while it waits (1416, CSX-118),
        // plus the three native message-box routes CNA.WindowsFormsCompat uses (1419, CSX-147).
        // The literal is a tripwire, not a fact about CNA -- it exists so that adding an import is
        // a deliberate act rather than something that happens on the way to something else.
        Assert.Equal(1419, declared.Length);
        Assert.Equal(declared, CnaNativeAbiPolicy.RequiredSymbols);
    }

    [Fact]
    public void MachineReadablePolicy_MatchesExecutableMatrix()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cna-native-abi-policy.json")));
        JsonElement root = document.RootElement;

        Assert.Equal(CnaNativeAbiPolicy.PolicyVersion, root.GetProperty("policyVersion").GetString());
        Assert.Equal("0.60.0", root.GetProperty("consumerAbi").GetString());
        JsonElement[] entries = root.GetProperty("acceptedVersions").EnumerateArray().ToArray();
        string[] versions = entries.Select(item => item.GetProperty("libraryAbi").GetString()!).ToArray();
        Assert.Equal(
            CnaNativeAbiPolicy.AcceptedProfiles.Select(profile => Format(profile.Version)),
            versions);
        Assert.Equal(
            CnaNativeAbiPolicy.AcceptedProfiles.Select(profile => profile.Compatibility),
            entries.Select(item => item.GetProperty("classification").GetString()));
        Assert.Equal(15, root.GetProperty("fixtures").GetArrayLength());
    }

    private static string Format(uint version)
    {
        (int major, int minor, int patch) = CnaAbi.Decode(version);
        return $"{major}.{minor}.{patch}";
    }

    /// <summary>
    /// No import takes a function-pointer parameter. Mono's WebAssembly interpreter builds its
    /// native-call trampolines from each parameter's C shape and has none for a function-pointer
    /// type, so such a call aborts the browser runtime -- which is what creating a
    /// <c>GraphicsDeviceManager</c> did. Callbacks cross as <see cref="nint"/>; their shape is
    /// checked by the ABI verifier's callback pairing.
    /// </summary>
    [Fact]
    public void Imports_TakeNoFunctionPointerParameter()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        MethodInfo[] imports = typeof(CnaHandle).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(all | BindingFlags.DeclaredOnly))
            .Where(method => method.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .ToArray();
        string[] offenders = imports
            .Where(method => method.GetParameters().Any(parameter => parameter.ParameterType.IsFunctionPointer))
            .Select(method => method.Name)
            .ToArray();

        Assert.NotEmpty(imports);
        Assert.Empty(offenders);
    }
}
