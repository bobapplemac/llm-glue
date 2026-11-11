using System.Text;
using LlmGlue.Core.Gguf;

namespace LlmGlue.Tests;

public sealed class GgufMetadataReaderTests
{
    [Fact]
    public void ReadsBoundedVersionThreeMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"llm-glue-gguf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "test.gguf");
        try
        {
            using (var writer = new BinaryWriter(File.Create(path), Encoding.UTF8, false))
            {
                writer.Write("GGUF"u8);
                writer.Write((uint)3);
                writer.Write((ulong)0);
                writer.Write((ulong)3);
                WriteString(writer, "general.architecture"); writer.Write((uint)8); WriteString(writer, "qwen3");
                WriteString(writer, "qwen3.context_length"); writer.Write((uint)10); writer.Write((ulong)262144);
                WriteString(writer, "general.file_type"); writer.Write((uint)4); writer.Write((uint)15);
            }
            var metadata = GgufMetadataReader.Read(path, [directory]);
            Assert.Equal((uint)3, metadata.Version);
            Assert.Equal("qwen3", metadata.Values["general.architecture"]);
            Assert.Equal((ulong)262144, metadata.Values["qwen3.context_length"]);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RejectsPathOutsideAllowlist()
    {
        Assert.Throws<UnauthorizedAccessException>(() => GgufMetadataReader.Read(Path.GetTempFileName(), [Path.Combine(Path.GetTempPath(), "not-this-directory")]));
    }

    [Fact]
    public void RejectsSymlinkThatEscapesAllowlistWhenPlatformPermitsLinks()
    {
        var root = Path.Combine(Path.GetTempPath(), $"llm-glue-gguf-link-{Guid.NewGuid():N}");
        var allowed = Path.Combine(root, "allowed");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(allowed);
        Directory.CreateDirectory(outside);
        var target = Path.Combine(outside, "model.gguf");
        var link = Path.Combine(allowed, "model.gguf");
        try
        {
            File.WriteAllBytes(target, "GGUF"u8.ToArray());
            try { File.CreateSymbolicLink(link, target); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException) { return; }
            Assert.Throws<UnauthorizedAccessException>(() => GgufMetadataReader.Read(link, [allowed]));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ulong)bytes.Length);
        writer.Write(bytes);
    }
}
