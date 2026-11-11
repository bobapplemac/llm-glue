using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LlmGlue.Core.Client;

internal static class OpenCodeJsoncWriter
{
    private const string ManagedComment = "// Managed by LLM Glue. Do not edit this section; ownership is tracked in the adjacent sidecar.";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static byte[] Write(string original, JsonObject desired, IReadOnlySet<string> managedProviders, IReadOnlySet<string> managedDisabledProviders)
    {
        var root = ParseObject(original);
        var members = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in root.Members)
        {
            if (!desired.TryGetPropertyValue(member.Name, out var value)) continue;
            seen.Add(member.Name);
            members.Add(member.Name switch
            {
                "providers" => member.WithValue(WriteProviders(member.Value, (JsonObject)value!, managedProviders)),
                "disabled_providers" => member.WithValue(WriteDisabledProviders(member.Value, (JsonArray)value!, managedDisabledProviders)),
                _ => member.Raw
            });
        }
        foreach (var property in desired.Where(x => !seen.Contains(x.Key)))
        {
            var value = property.Key switch
            {
                "providers" => WriteProviders("{}", (JsonObject)property.Value!, managedProviders),
                "disabled_providers" => WriteDisabledProviders("[]", (JsonArray)property.Value!, managedDisabledProviders),
                _ => Serialize(property.Value)
            };
            members.Add($"{JsonSerializer.Serialize(property.Key)}: {value}");
        }
        var body = Join(members, 2);
        var text = root.Prefix + "{" + (body.Length == 0 ? string.Empty : "\n" + body + "\n") + "}" + root.Suffix;
        return new UTF8Encoding(false).GetBytes(text);
    }

    private static string WriteProviders(string original, JsonObject desired, IReadOnlySet<string> managed)
    {
        var parsed = ParseObject(original);
        var members = parsed.Members
            .Where(x => desired.ContainsKey(x.Name) && !managed.Contains(x.Name))
            .Select(x => x.Raw)
            .ToList();
        var generated = managed.Where(desired.ContainsKey).Order(StringComparer.Ordinal).ToArray();
        for (var index = 0; index < generated.Length; index++)
        {
            var name = generated[index];
            var prefix = index == 0 ? ManagedComment + "\n" : string.Empty;
            members.Add($"{prefix}{JsonSerializer.Serialize(name)}: {Serialize(desired[name])}");
        }
        return "{" + (members.Count == 0 ? string.Empty : "\n" + Join(members, 4) + "\n  ") + "}";
    }

    private static string WriteDisabledProviders(string original, JsonArray desired, IReadOnlySet<string> managed)
    {
        var desiredNames = desired.Select(StringValue).ToHashSet(StringComparer.Ordinal);
        var members = ParseArray(original).Members
            .Select(x => new
            {
                x.Raw,
                Name = StringValue(JsonNode.Parse(x.Value, documentOptions: new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
            })
            .Where(x => desiredNames.Contains(x.Name) && !managed.Contains(x.Name))
            .Select(x => x.Raw)
            .ToList();
        var generated = managed.Where(desiredNames.Contains).Order(StringComparer.Ordinal).ToArray();
        for (var index = 0; index < generated.Length; index++)
            members.Add((index == 0 ? ManagedComment + "\n" : string.Empty) + JsonSerializer.Serialize(generated[index]));
        return "[" + (members.Count == 0 ? string.Empty : "\n" + Join(members, 4) + "\n  ") + "]";
    }

    private static string StringValue(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text)
        ? text
        : throw new InvalidDataException("OpenCode disabled provider names must be strings.");

    private static string Serialize(JsonNode? node) => node?.ToJsonString(Indented) ?? "null";

    private static string Join(IReadOnlyList<string> members, int indentation)
    {
        var indent = new string(' ', indentation);
        var builder = new StringBuilder();
        for (var index = 0; index < members.Count; index++)
        {
            if (index != 0) builder.Append('\n');
            var normalized = NormalizeIndentation(members[index]);
            builder.Append(indent).Append(normalized.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\n" + indent, StringComparison.Ordinal));
            if (index != members.Count - 1)
            {
                if (EndsInLineComment(normalized)) builder.Append('\n').Append(indent);
                builder.Append(',');
            }
        }
        return builder.ToString();
    }

    private static string NormalizeIndentation(string text)
    {
        var lines = text.Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var removable = lines.Skip(1).Where(x => x.Length != 0).Select(LeadingWhitespace).DefaultIfEmpty(0).Min();
        if (removable > 0)
            for (var index = 1; index < lines.Length; index++)
                lines[index] = lines[index].Length >= removable ? lines[index][removable..] : string.Empty;
        return string.Join('\n', lines);
    }

    private static int LeadingWhitespace(string text)
    {
        var count = 0;
        while (count < text.Length && char.IsWhiteSpace(text[count])) count++;
        return count;
    }

    private static bool EndsInLineComment(string text)
    {
        var line = text[(Math.Max(text.LastIndexOf('\n'), text.LastIndexOf('\r')) + 1)..];
        var quoted = false;
        var escaped = false;
        for (var index = 0; index < line.Length - 1; index++)
        {
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (line[index] == '\\') escaped = true;
                else if (line[index] == '"') quoted = false;
            }
            else if (line[index] == '"') quoted = true;
            else if (line[index] == '/' && line[index + 1] == '/') return true;
        }
        return false;
    }

    private static ParsedContainer ParseObject(string text) => ParseContainer(text, '{', '}', true);
    private static ParsedContainer ParseArray(string text) => ParseContainer(text, '[', ']', false);

    private static ParsedContainer ParseContainer(string text, char open, char close, bool isObject)
    {
        var position = SkipTrivia(text, 0);
        if (position >= text.Length || text[position] != open) throw new InvalidDataException($"Expected JSONC '{open}'.");
        var opening = position++;
        var members = new List<SyntaxMember>();
        while (true)
        {
            var rawStart = position;
            position = SkipTrivia(text, position);
            if (position >= text.Length) throw new InvalidDataException("Unterminated JSONC container.");
            if (text[position] == close)
                return new(text[..opening], text[(position + 1)..], members);

            string name;
            int valueStart;
            if (isObject)
            {
                if (text[position] != '"') throw new InvalidDataException("JSONC object property names must be quoted.");
                var keyEnd = ScanString(text, position);
                name = JsonSerializer.Deserialize<string>(text[position..keyEnd])!;
                position = SkipTrivia(text, keyEnd);
                if (position >= text.Length || text[position++] != ':') throw new InvalidDataException("JSONC object property is missing ':'.");
                valueStart = SkipTrivia(text, position);
            }
            else
            {
                name = members.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                valueStart = position;
            }
            var valueEnd = ScanValue(text, valueStart);
            position = SkipTrivia(text, valueEnd);
            members.Add(new(name, text[rawStart..position], text[valueStart..valueEnd], rawStart, valueStart, valueEnd, position));
            if (position >= text.Length) throw new InvalidDataException("Unterminated JSONC container.");
            if (text[position] == ',') { position++; continue; }
            if (text[position] != close) throw new InvalidDataException($"Expected JSONC ',' or '{close}'.");
        }
    }

    private static int SkipTrivia(string text, int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position])) { position++; continue; }
            if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '/')
            {
                position += 2;
                while (position < text.Length && text[position] is not ('\r' or '\n')) position++;
                continue;
            }
            if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '*')
            {
                var end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
                if (end < 0) throw new InvalidDataException("Unterminated JSONC block comment.");
                position = end + 2;
                continue;
            }
            return position;
        }
        return position;
    }

    private static int ScanString(string text, int position)
    {
        var escaped = false;
        for (var index = position + 1; index < text.Length; index++)
        {
            if (escaped) { escaped = false; continue; }
            if (text[index] == '\\') { escaped = true; continue; }
            if (text[index] == '"') return index + 1;
        }
        throw new InvalidDataException("Unterminated JSONC string.");
    }

    private static int ScanValue(string text, int position)
    {
        if (position >= text.Length) throw new InvalidDataException("Missing JSONC value.");
        if (text[position] == '"') return ScanString(text, position);
        if (text[position] is '{' or '[')
        {
            var stack = new Stack<char>();
            for (var index = position; index < text.Length; index++)
            {
                if (text[index] == '"') { index = ScanString(text, index) - 1; continue; }
                if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '/')
                {
                    index += 2;
                    while (index < text.Length && text[index] is not ('\r' or '\n')) index++;
                    continue;
                }
                if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '*')
                {
                    var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    if (end < 0) throw new InvalidDataException("Unterminated JSONC block comment.");
                    index = end + 1;
                    continue;
                }
                if (text[index] is '{' or '[') stack.Push(text[index]);
                else if (text[index] is '}' or ']')
                {
                    var expected = text[index] == '}' ? '{' : '[';
                    if (stack.Count == 0 || stack.Pop() != expected) throw new InvalidDataException("Mismatched JSONC container.");
                    if (stack.Count == 0) return index + 1;
                }
            }
            throw new InvalidDataException("Unterminated JSONC value.");
        }
        var endPosition = position;
        while (endPosition < text.Length && !char.IsWhiteSpace(text[endPosition]) && text[endPosition] is not (',' or '}' or ']'))
        {
            if (text[endPosition] == '/' && endPosition + 1 < text.Length && text[endPosition + 1] is '/' or '*') break;
            endPosition++;
        }
        if (endPosition == position) throw new InvalidDataException("Missing JSONC value.");
        return endPosition;
    }

    private sealed record ParsedContainer(string Prefix, string Suffix, IReadOnlyList<SyntaxMember> Members);
    private sealed record SyntaxMember(string Name, string Raw, string Value, int RawStart, int ValueStart, int ValueEnd, int RawEnd)
    {
        public string WithValue(string value) => Raw[..(ValueStart - RawStart)] + value + Raw[(ValueEnd - RawStart)..];
    }
}
