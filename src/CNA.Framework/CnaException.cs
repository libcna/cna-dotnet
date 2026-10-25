using CNA.Interop;

namespace CNA;

/// <summary>
/// The managed exception every failing native CNA call is converted to. No <c>CnaResult</c> and
/// no native exception ever crosses out of <c>CNA.Interop</c> unconverted -- see
/// libcna/cna's docs/c-api/ERRORS.md and plan.md invariant #2.
/// </summary>
public class CnaException : Exception
{
    public CnaException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// The name of the native result that caused this, or null when it did not come from one.
    ///
    /// A caller frequently needs to tell "this renderer cannot do that" from "this went wrong",
    /// and until now the only way was to read the message. The *name* rather than the value,
    /// because <c>CnaResult</c> is a CNA.Interop type and no CNA.Interop type may appear in a
    /// public signature -- the invariant this class exists to serve in the first place.
    /// </summary>
    public string? NativeResult { get; private init; }

    /// <summary>
    /// The .NET type name of the exception the canonical call threw natively
    /// (<c>System.ArgumentOutOfRangeException</c>, <c>Microsoft.Xna.Framework.Content.ContentLoadException</c>),
    /// or null when the native layer raised the failure itself. A layer that re-raises CNA failures
    /// as their canonical exceptions -- the XNA facade does -- reads it here.
    /// </summary>
    public string? CanonicalExceptionType { get; private init; }

    /// <summary>The parameter an argument exception named, or null.</summary>
    public string? CanonicalParamName { get; private init; }

    /// <summary>The native diagnostic alone, without the operation prefix, for a re-raise.</summary>
    internal string? NativeMessage { get; private init; }

    public CnaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal static void ThrowIfFailed(CnaResult result, string operation)
    {
        if (result.IsSuccess())
        {
            return;
        }

        string detail = CnaError.GetLastErrorMessage();
        string canonicalType = CnaCanonicalException.LastType();
        string canonicalParam = CnaCanonicalException.LastParamName();
        throw new CnaException(string.IsNullOrEmpty(detail)
            ? $"{operation} failed with native result {result}."
            : $"{operation} failed with native result {result}: {detail}")
        {
            NativeResult = result.ToString(),
            CanonicalExceptionType = canonicalType.Length == 0 ? null : canonicalType,
            CanonicalParamName = canonicalParam.Length == 0 ? null : canonicalParam,
            NativeMessage = detail,
        };
    }
}
