using System.Buffers.Binary;
using System.Text;

namespace LlmGlue.Core.Gguf;

public sealed record GgufMetadata(uint Version, ulong TensorCount, IReadOnlyDictionary<string, object?> Values);

public sealed class GgufFormatException(string message) : Exception(message);

public static class GgufMetadataReader
{
    private const int MaxStringBytes = 4 * 1024 * 1024;
    private const ulong MaxMetadataEntries = 100_000;
    private const ulong MaxArrayEntries = 1_000_000;

    public static GgufMetadata Read(string path, IEnumerable<string> allowedDirectories)
    {
        var fullPath = Path.GetFullPath(path);
        var allowed = allowedDirectories.Select(x => Path.GetFullPath(x).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).ToArray();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!allowed.Any(x => fullPath.StartsWith(x, comparison))) throw new UnauthorizedAccessException("GGUF path is outside configured model directories.");
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        if (!reader.ReadBytes(4).SequenceEqual("GGUF"u8.ToArray())) throw new GgufFormatException("Invalid GGUF magic.");
        var version = reader.ReadUInt32();
        if (version is < 2 or > 3) throw new GgufFormatException($"Unsupported GGUF version {version}.");
        var tensorCount = reader.ReadUInt64();
        var metadataCount = reader.ReadUInt64();
        if (metadataCount > MaxMetadataEntries) throw new GgufFormatException("GGUF metadata count exceeds safety limit.");
        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        for (ulong i = 0; i < metadataCount; i++)
        {
            var key = ReadString(reader);
            var type = reader.ReadUInt32();
            if (!values.TryAdd(key, ReadValue(reader, type))) throw new GgufFormatException($"Duplicate GGUF metadata key '{key}'.");
        }
        return new GgufMetadata(version, tensorCount, values);
    }

    private static object? ReadValue(BinaryReader reader, uint type) => type switch
    {
        0 => reader.ReadByte(),
        1 => reader.ReadSByte(),
        2 => reader.ReadUInt16(),
        3 => reader.ReadInt16(),
        4 => reader.ReadUInt32(),
        5 => reader.ReadInt32(),
        6 => reader.ReadSingle(),
        7 => reader.ReadByte() switch { 0 => false, 1 => true, _ => throw new GgufFormatException("Invalid GGUF boolean.") },
        8 => ReadString(reader),
        9 => ReadArray(reader),
        10 => reader.ReadUInt64(),
        11 => reader.ReadInt64(),
        12 => reader.ReadDouble(),
        _ => throw new GgufFormatException($"Unsupported GGUF metadata type {type}.")
    };

    private static object?[] ReadArray(BinaryReader reader)
    {
        var elementType = reader.ReadUInt32();
        var count = reader.ReadUInt64();
        if (count > MaxArrayEntries) throw new GgufFormatException("GGUF array exceeds safety limit.");
        var result = new object?[(int)count];
        for (var i = 0; i < result.Length; i++) result[i] = ReadValue(reader, elementType);
        return result;
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt64();
        if (length > MaxStringBytes) throw new GgufFormatException("GGUF string exceeds safety limit.");
        var bytes = reader.ReadBytes((int)length);
        if (bytes.Length != (int)length) throw new EndOfStreamException("Unexpected end of GGUF metadata.");
        return new UTF8Encoding(false, true).GetString(bytes);
    }
}
