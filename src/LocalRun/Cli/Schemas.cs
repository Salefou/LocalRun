namespace LocalRun.Cli;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// The JSON Schemas of the files LocalRun reads and writes, written by hand (<c>Resources/Schemas</c>) and printed by
/// <c>lrun schema</c>. The configs are also checked against theirs on read, for fields this <c>lrun</c> would ignore.
/// Tests keep each schema's properties equal to what the C# types serialize.
/// </summary>
internal static class Schemas
{
    public const string Config = "config";
    public const string Repack = "repack";
    public const string Result = "result";
    public const string Settings = "settings";

    private static readonly Dictionary<string, string> Resources = new(StringComparer.Ordinal)
    {
        [Config] = "LocalRun.Schemas.localrun.config.schema.json",
        [Repack] = "LocalRun.Schemas.repack.config.schema.json",
        [Result] = "LocalRun.Schemas.result.schema.json",
        [Settings] = "LocalRun.Schemas.settings.schema.json",
    };

    public static IReadOnlyList<string> Names { get; } = [Config, Repack, Result, Settings];

    /// <summary>The schema as written in the resource.</summary>
    public static string Text(string name)
    {
        using var stream = typeof(Schemas).Assembly.GetManifestResourceStream(Resources[name])
            ?? throw new InvalidOperationException($"Schema '{name}' is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static JsonElement Of(string name)
    {
        using var document = JsonDocument.Parse(Text(name));
        return document.RootElement.Clone();
    }

    /// <summary>
    /// The paths (<c>$.Projects[3].SkipRepack</c>) of the properties of <paramref name="value"/> that its schema doesn't
    /// declare where it forbids others: what a reader of that shape ignores. Only looks for those, not for types or
    /// required fields (the deserializer reports them). Under <c>anyOf</c>, the branch with the fewest unknown fields counts.
    /// </summary>
    public static List<string> UnknownFields(JsonElement schema, JsonElement value)
    {
        List<string> unknown = [];
        Walk(schema, schema, value, "$", unknown);
        return unknown;
    }

    private static void Walk(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> unknown)
    {
        schema = Resolve(root, schema);
        if (schema.TryGetProperty("anyOf", out var branches))
        {
            List<string>? closest = null;
            foreach (var branch in branches.EnumerateArray().Where(branch => Admits(root, branch, value)))
            {
                List<string> found = [];
                Walk(root, branch, value, path, found);
                if (closest == null || found.Count < closest.Count)
                    closest = found;
                if (found.Count == 0)
                    break;
            }
            unknown.AddRange(closest ?? []);
            return;
        }

        if (value.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var properties))
        {
            bool closed = schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False;
            foreach (var property in value.EnumerateObject())
            {
                string propertyPath = $"{path}.{property.Name}";
                if (properties.TryGetProperty(property.Name, out var propertySchema))
                    Walk(root, propertySchema, property.Value, propertyPath, unknown);
                else if (closed)
                    unknown.Add(propertyPath);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            int index = 0;
            foreach (var item in value.EnumerateArray())
                Walk(root, items, item, $"{path}[{index++}]", unknown);
        }
    }

    /// <summary>Whether the <c>type</c> of <paramref name="schema"/>, if it has one, allows the kind of <paramref name="value"/>.</summary>
    private static bool Admits(JsonElement root, JsonElement schema, JsonElement value)
    {
        schema = Resolve(root, schema);
        if (!schema.TryGetProperty("type", out var type))
            return true;
        string kind = value.ValueKind switch
        {
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            _ => "null",
        };
        var types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString()) : [type.GetString()];
        return types.Any(t => t == kind || (t == "integer" && kind == "number"));
    }

    /// <summary>Follows <c>$ref</c>s to this schema's own <c>$defs</c> (<c>#/$defs/Name</c>), the only kind the schemas use.</summary>
    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        while (schema.TryGetProperty("$ref", out var reference))
        {
            const string prefix = "#/$defs/";
            string target = reference.GetString()!;
            if (!target.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unsupported $ref '{target}'.");
            schema = root.GetProperty("$defs").GetProperty(target[prefix.Length..]);
        }
        return schema;
    }
}
