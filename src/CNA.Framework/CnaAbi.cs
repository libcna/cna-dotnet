using CNA.Interop;

namespace CNA;

/// <summary>
/// The native library's ABI version, and the check that the loaded one is usable.
///
/// <c>cna_get_abi_version</c> (<c>abi.h:114</c>) exists precisely so a binding can refuse a
/// mismatched library, and it was bound but never called -- so a native library from a different
/// ABI generation would have been used anyway, and failed later as a garbled struct or a wrong
/// handle rather than as a clear message. A sweep of every declaration against the headers turned
/// it up alongside two fabricated ones.
/// </summary>
public static class CnaAbi
{
    /// <summary>
    /// The ABI this binding was written against
    /// (<c>CNA_ABI_VERSION_MAJOR</c>/<c>_MINOR</c>/<c>_PATCH</c> = 0.60.0).
    ///
    /// 0.1.0 -> 0.2.0 was the content-reader registration, SpriteFont and launch-parameter routes.
    /// 0.3.0 -> 0.4.0 added the <c>.cnj</c> loader registration and 0.4.0 -> 0.5.0 the native-window
    /// accessor, both additively. 0.5.0 -> 0.6.0 is <em>not</em> additive: an empty shader source is
    /// now refused with <c>INVALID_ARGUMENT</c> before any renderer sees it, where SOFTWARE used to
    /// throw (reported as <c>INTERNAL</c>, blaming CNA for the caller's input) and SDL_RENDERER
    /// used to return a handle for an effect with no source at all.
    /// 0.2.0 -> 0.3.0 is <em>not</em> additive: every route taking a <c>CNA_Bool</c> now refuses a
    /// byte outside {0, 1} with <c>INVALID_ARGUMENT</c>. Sixty-six of ninety-four used to accept
    /// one and then disagree about what it meant -- read as <c>!= CNA_FALSE</c> in some places and
    /// <c>== CNA_TRUE</c> in others, so 9 was true in one route and false in another.
    ///
    /// This binding is unaffected, and that was checked rather than assumed: every Bool it emits
    /// comes from <c>value ? (byte)1 : (byte)0</c> or a literal, and every Bool it reads is
    /// compared <c>!= 0</c>. Compatibility is not inferred from the shared major: CNA's
    /// experimental 0.x contract allows a minor to identify an incompatible generation. The
    /// resolver admits only exact versions in the reviewed matrix and then verifies the complete
    /// imported symbol set plus side-effect-free signature and versioned-structure canaries.
    ///
    /// Not to be confused with the ELF symbol version the library exports, which is
    /// <c>CNA_C_API_0.1</c> and deliberately does <em>not</em> track this. Moving a version node on
    /// a minor bump would break every already-linked consumer.
    /// </summary>
    public const uint ExpectedVersion = CnaNativeAbiPolicy.ConsumerVersion;

    private static bool _checked;

    /// <summary>The encoded version the loaded native library reports.</summary>
    public static uint NativeVersion => Native.cna_get_abi_version();

    public static (int Major, int Minor, int Patch) Decode(uint encoded) =>
        ((int)((encoded >> 16) & 0xFFFF), (int)((encoded >> 8) & 0xFF), (int)(encoded & 0xFF));

    /// <summary>
    /// Throws unless the loaded library's ABI is compatible with what this binding expects.
    ///
    /// Compatibility is established by <see cref="NativeLibraryResolver"/> before the first native
    /// import returns. The version selects a reviewed profile; the resolver also requires every
    /// imported symbol and executes core-signature and structure-shape canaries. Experimental 0.x
    /// minor or patch values outside that matrix are rejected.
    ///
    /// Runs once. Every subsequent call is free, so <see cref="Game"/> can call it on construction
    /// without paying for it per game.
    /// </summary>
    public static void EnsureCompatible()
    {
        if (_checked)
        {
            return;
        }

        uint native = NativeVersion;
        if (!CnaNativeAbiPolicy.TryGetProfile(native, out _))
        {
            // The assembly-local resolver normally makes this unreachable. Retain the assertion at
            // the public check boundary so alternate hosting cannot turn registration or ordering
            // into a silent version-only bypass.
            throw new CnaException(
                $"The loaded cna-native library implements C ABI {Format(native)}, which is not " +
                $"accepted by {CnaNativeAbiPolicy.PolicyVersion} for consumer ABI {Format(ExpectedVersion)}. " +
                NativeLibraryResolver.DescribeSelection(ExpectedVersion));
        }

        _checked = true;
    }

    private static string Format(uint version)
    {
        (int major, int minor, int patch) = Decode(version);
        return $"{major}.{minor}.{patch}";
    }
}
