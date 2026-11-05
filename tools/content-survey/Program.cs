using CNA.XnaCompat.Extensions;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework.Content;

// Reports how much of a directory of compiled XNA content this binding can read.
//
// The question it answers is the one that decides whether a ported game starts: an .xnb file names
// the content type readers it needs, and a reader this binding cannot resolve fails the whole
// asset. Counting resolvable readers across a real game's Content folder is a direct measure of
// content compatibility, and it needs no graphics device, no native library and no copy of the
// content -- it reads headers in place.
//
// What it does not measure: whether the bytes after the table are read correctly. A resolvable
// table is necessary, not sufficient.
//
// Compressed assets are decompressed through the loader's own container code rather than skipped.
// Skipping them was quietly excluding every LZX-compressed font and texture from the number.

string? directory = null;
string? jsonOutput = null;
bool verbose = false;
bool load = false;

for (int index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--json":
            jsonOutput = Next(args, ref index, "--json");
            break;
        case "--verbose":
            verbose = true;
            break;
        case "--load":
            load = true;
            break;
        default:
            if (directory is not null)
            {
                return Usage($"Unexpected argument '{args[index]}'.");
            }

            directory = args[index];
            break;
    }
}

if (directory is null)
{
    return Usage("A content directory is required.");
}

if (!Directory.Exists(directory))
{
    return Usage($"'{directory}' is not a directory.");
}

string[] assets = [.. Directory.EnumerateFiles(directory, "*.xnb", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

var readable = new List<string>();
var nativeBacked = new List<string>();
var needsGameAssembly = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
var missingBuiltIn = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
var compressed = new List<string>();
var malformed = new SortedDictionary<string, string>(StringComparer.Ordinal);
var readerUsage = new SortedDictionary<string, int>(StringComparer.Ordinal);
var rootReaders = new SortedDictionary<string, string>(StringComparer.Ordinal);

foreach (string asset in assets)
{
    string relative = Path.GetRelativePath(directory, asset);
    try
    {
        using FileStream file = File.OpenRead(asset);
        bool wasCompressed = (Peek(asset) & 0xC0) != 0;

        Stream payload = ManagedXnbContentLoader.OpenPayload(file, relative);
        using Stream owned = ReferenceEquals(payload, file) ? Stream.Null : payload;
        using var reader = new BinaryReader(payload, Encoding.UTF8, leaveOpen: true);

        if (wasCompressed)
        {
            compressed.Add(relative);
        }

        int count = Read7BitEncodedInt(reader);
        if (count is < 0 or > 4096)
        {
            malformed[relative] = $"implausible type reader count {count}";
            continue;
        }

        var absentTypes = new List<string>();
        var missing = new List<string>();
        string? rootReader = null;

        for (int index = 0; index < count; index++)
        {
            string name = reader.ReadString();
            _ = reader.ReadInt32();          // reader version

            string canonical = Canonical(name);
            rootReader ??= canonical;
            readerUsage[canonical] = readerUsage.TryGetValue(canonical, out int seen) ? seen + 1 : 1;

            if (ContentTypeReaderManager.CanResolveForSurvey(name))
            {
                continue;
            }

            // Two very different failures. A reader naming a type that is not loaded here would
            // resolve inside the game, whose own assembly supplies it. A built-in this binding has
            // no reader for would not.
            (BuiltinGenericReaders.FailedOnlyOnAnAbsentType(name) ? absentTypes : missing).Add(canonical);
        }

        // An asset whose root goes to CNA's own loader needs no managed reader for anything in its
        // table, so its nested readers are not a finding.
        if (rootReader is not null && ContentManager.IsNativeBackedRootReaderForSurvey(rootReader))
        {
            nativeBacked.Add(relative);
        }
        else if (missing.Count > 0)
        {
            missingBuiltIn[relative] = missing;
        }
        else if (absentTypes.Count > 0)
        {
            needsGameAssembly[relative] = absentTypes;
        }
        else
        {
            readable.Add(relative);
        }

        if (rootReader is not null)
        {
            rootReaders[relative] = rootReader;
        }
    }
    catch (Exception exception) when (
        exception is IOException or EndOfStreamException or FormatException or ContentLoadException)
    {
        malformed[relative] = exception.Message;
    }
}

var missingReaders = new SortedDictionary<string, int>(StringComparer.Ordinal);
foreach (List<string> names in missingBuiltIn.Values)
{
    foreach (string name in names.Distinct(StringComparer.Ordinal))
    {
        missingReaders[name] = missingReaders.TryGetValue(name, out int seen) ? seen + 1 : 1;
    }
}

Console.WriteLine($"CONTENT_SURVEY_DIRECTORY={Path.GetFullPath(directory)}");
Console.WriteLine($"CONTENT_SURVEY_ASSETS={assets.Length}");
Console.WriteLine($"CONTENT_SURVEY_MANAGED_READABLE={readable.Count}");
Console.WriteLine($"CONTENT_SURVEY_NATIVE_BACKED={nativeBacked.Count}");
Console.WriteLine($"CONTENT_SURVEY_NEEDS_GAME_ASSEMBLY={needsGameAssembly.Count}");
Console.WriteLine($"CONTENT_SURVEY_MISSING_BUILTIN={missingBuiltIn.Count}");
Console.WriteLine($"CONTENT_SURVEY_COMPRESSED_AND_ANALYSED={compressed.Count}");
Console.WriteLine($"CONTENT_SURVEY_MALFORMED={malformed.Count}");
Console.WriteLine($"CONTENT_SURVEY_DISTINCT_READERS={readerUsage.Count}");

if (load)
{
    // Everything the resolution pass believes this binding can read, loaded for real. Assets that
    // need a game's own assembly are excluded because their types genuinely are not here, and
    // malformed ones because there is nothing to load.
    List<(string Relative, string RootReader)> loadable =
    [
        .. readable.Concat(nativeBacked)
            .Where(rootReaders.ContainsKey)
            .Select(relative => (relative, rootReaders[relative]))
            .OrderBy(entry => entry.relative, StringComparer.Ordinal),
    ];

    using var survey = new CnaDotnet.ContentSurvey.LoadingSurvey(directory, loadable);
    survey.RunFrameEXT();

    Console.WriteLine($"CONTENT_LOAD_ATTEMPTED={loadable.Count}");
    Console.WriteLine($"CONTENT_LOAD_LOADED={survey.Loaded.Count}");
    Console.WriteLine($"CONTENT_LOAD_NATIVE_NOT_SUPPORTED={survey.NativeNotSupported.Count}");
    Console.WriteLine($"CONTENT_LOAD_RUNTIME_FAILURE={survey.RuntimeFailures.Count}");
    Console.WriteLine($"CONTENT_LOAD_EXTERNAL_GAME_TYPE={survey.ExternalGameTypes.Count}");
    Console.WriteLine($"CONTENT_LOAD_NO_MANAGED_TYPE={survey.NoManagedType.Count}");

    int compressedLoaded = survey.Loaded.Keys.Count(compressed.Contains);
    Console.WriteLine($"CONTENT_LOAD_COMPRESSED_LOADED={compressedLoaded} of {compressed.Count}");

    if (verbose)
    {
        // What each asset actually materialised into, not just that it did. A model's line carries
        // its part/effect/texture counts, which is the difference between "the file parsed" and
        // "the object can draw".
        foreach ((string relative, string detail) in survey.Loaded.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  LOADED {relative}: {detail}");
        }

        foreach ((string relative, string detail) in survey.RuntimeFailures)
        {
            Console.WriteLine($"  RUNTIME_FAILURE {relative}: {detail}");
        }

        foreach ((string relative, string detail) in survey.NativeNotSupported)
        {
            Console.WriteLine($"  NATIVE_NOT_SUPPORTED {relative}: {detail}");
        }
    }
}

if (missingReaders.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Built-in readers this binding does not have, by how many assets need them:");
    foreach ((string name, int assetCount) in missingReaders.OrderByDescending(pair => pair.Value))
    {
        Console.WriteLine($"  {assetCount,5}  {name}");
    }
}
else
{
    Console.WriteLine();
    Console.WriteLine("No asset here needs a built-in reader this binding does not have.");
}

if (malformed.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Assets this survey could not read at all:");
    foreach ((string asset, string reason) in malformed)
    {
        Console.WriteLine($"  {asset}: {reason}");
    }
}

if (needsGameAssembly.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine(
        $"{needsGameAssembly.Count} asset(s) name a type this survey cannot load. Those resolve inside the");
    Console.WriteLine("game, whose own assembly supplies them; they are listed here, not counted as failures.");
    if (verbose)
    {
        foreach ((string asset, List<string> names) in needsGameAssembly)
        {
            Console.WriteLine($"  {asset}: {string.Join(", ", names.Distinct(StringComparer.Ordinal))}");
        }
    }
}

if (verbose)
{
    Console.WriteLine();
    Console.WriteLine("Every reader seen, by usage:");
    foreach ((string name, int usage) in readerUsage.OrderByDescending(pair => pair.Value))
    {
        Console.WriteLine($"  {usage,5}  {name}");
    }
}

if (jsonOutput is not null)
{
    var report = new
    {
        schemaVersion = 1,
        directory = Path.GetFullPath(directory),
        assets = assets.Length,
        managedReadable = readable.Count,
        nativeBacked = nativeBacked.Count,
        needsGameAssembly = needsGameAssembly.Count,
        missingBuiltIn = missingBuiltIn.Count,
        compressed = compressed.Count,
        malformed = malformed.Count,
        missingReaders,
        readerUsage,
        missingBuiltInAssets = missingBuiltIn,
        needsGameAssemblyAssets = needsGameAssembly,
    };

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonOutput))!);
    File.WriteAllText(jsonOutput, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}

// A missing reader is the finding, not an error in this tool: the point is to report it.
return 0;

// The container's flags byte, read before the loader consumes the prologue, so the report can say
// how many assets were compressed as well as analysing them.
static byte Peek(string path)
{
    using FileStream stream = File.OpenRead(path);
    Span<byte> prologue = stackalloc byte[6];
    return stream.ReadAtLeast(prologue, prologue.Length, throwOnEndOfStream: false) == prologue.Length
        ? prologue[5]
        : (byte)0;
}

static string Canonical(string serializedName)
{
    int depth = 0;
    for (int index = 0; index < serializedName.Length; index++)
    {
        switch (serializedName[index])
        {
            case '[':
                depth++;
                break;
            case ']':
                depth--;
                break;
            case ',' when depth == 0:
                return serializedName[..index].Trim();
        }
    }

    return serializedName.Trim();
}

static int Read7BitEncodedInt(BinaryReader reader)
{
    int value = 0;
    int shift = 0;
    while (shift < 35)
    {
        byte part = reader.ReadByte();
        value |= (part & 0x7F) << shift;
        if ((part & 0x80) == 0)
        {
            return value;
        }

        shift += 7;
    }

    throw new FormatException("A 7-bit encoded integer did not terminate.");
}

static string Next(string[] arguments, ref int index, string option)
{
    index++;
    return index < arguments.Length ? arguments[index] : throw new ArgumentException($"{option} requires a value.");
}

static int Usage(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine("Usage: cna-content-survey <content directory> [--json <report.json>] [--verbose]");
    return 2;
}
