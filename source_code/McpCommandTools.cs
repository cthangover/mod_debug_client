using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Cthangover.Mods.DebugClient.Mcp;

/// <summary>Describes the four MCP tools and translates validated inputs into the kernel command envelope.</summary>
internal static class McpCommandTools
{
    private static readonly Tool[] Catalog =
    [
        Create("diagnose", "Return the current boot phase and recent errors with source coordinates. Supply a mod ID to see its status and compilation cache.", true, """
            {"type":"object","additionalProperties":false,"properties":{
              "mod":{"type":"string","description":"Exact mod ID"},
              "since":{"type":"integer","minimum":0,"maximum":9223372036854775807,"description":"Previous diagnostic cursor"},
              "limit":{"type":"integer","minimum":1,"maximum":100}
            }}
            """),
        Create("inspect", "Inspect nodes, caches, live game state, resources, or a loaded mod. Use a selector and page large results.", true, """
            {"type":"object","additionalProperties":false,"required":["area"],"properties":{
              "area":{"type":"string","enum":["node","cache","runtime","resource","mod"]},
              "path":{"type":"string","description":"Exact Godot node path"},
              "type":{"type":"string","description":"Godot or C# node type"},
              "id":{"type":"integer","minimum":1,"description":"Godot instance ID"},
              "group":{"type":"string","description":"Godot node group"},
              "key":{"type":"string","description":"Exact resource key"},
              "kind":{"type":"string","description":"Resource or cache kind"},
              "mod":{"type":"string","description":"Exact mod ID"},
              "query":{"type":"string","description":"Partial name or path"},
              "fields":{"type":"array","maxItems":64,"items":{"type":"string"}},
              "properties":{"type":"array","maxItems":64,"items":{"type":"string"}},
              "offset":{"type":"integer","minimum":0,"maximum":2147483647},
              "limit":{"type":"integer","minimum":1,"maximum":100}
            }}
            """),
        Create("describe", "Find registered development commands and their owning mods. Use their descriptions to choose arguments for run.", true, """
            {"type":"object","additionalProperties":false,"properties":{
              "query":{"type":"string","description":"Text in a command, provider, or description"},
              "mod":{"type":"string","description":"Exact command provider mod ID"},
              "offset":{"type":"integer","minimum":0,"maximum":2147483647},
              "limit":{"type":"integer","minimum":1,"maximum":100}
            }}
            """),
        Create("run", "Execute one registered development command on the game thread and return its result. Use describe to find commands and arguments. Supply an operation ID before changing state so you can recover the outcome with operation.status after disconnecting.", false, """
            {"type":"object","additionalProperties":false,"required":["command"],"properties":{
              "command":{"type":"string","description":"Exact command name"},
              "provider":{"type":"string","description":"Owning mod ID. Omit for kernel commands."},
              "operation_id":{"type":"string","format":"uuid","description":"Caller-generated operation UUID. Use the same ID with operation.status after a disconnect."},
              "arguments":{"type":"object","description":"Command-specific values as described by its provider"}
            }}
            """)
    ];

    public static ValueTask<ListToolsResult> List(RequestContext<ListToolsRequestParams> context, CancellationToken cancellation)
        => ValueTask.FromResult(new ListToolsResult { Tools = Catalog });

    public static JsonObject CreateRequest(CallToolRequestParams? call)
    {
        var tool = FindTool(call?.Name);
        var arguments = call!.Arguments;
        ValidateArguments(tool.InputSchema, arguments);
        if (tool.Name == "run")
            return CreateRunRequest(arguments!);

        var request = new JsonObject { ["command"] = tool.Name };
        if (arguments != null)
            foreach (var (name, value) in arguments)
                request[name] = JsonNode.Parse(value.GetRawText());
        return request;
    }

    private static Tool FindTool(string? name)
    {
        foreach (var tool in Catalog)
            if (tool.Name == name)
                return tool;
        throw new ArgumentException("Unknown tool. Use tools/list to find a tool.");
    }

    private static JsonObject CreateRunRequest(IDictionary<string, JsonElement> arguments)
    {
        var parameters = arguments.TryGetValue("arguments", out var body)
            ? JsonNode.Parse(body.GetRawText())!.AsObject()
            : new JsonObject();
        if (parameters.ContainsKey("command") || parameters.ContainsKey("provider") || parameters.ContainsKey("client")
            || parameters.ContainsKey("operation_id"))
            throw new ArgumentException("Pass command, provider and operation_id as tool arguments. The arguments object is reserved for command values.");
        parameters["command"] = arguments["command"].GetString();
        if (arguments.TryGetValue("provider", out var provider))
            parameters["provider"] = provider.GetString();
        if (arguments.TryGetValue("operation_id", out var operationId))
            parameters["operation_id"] = operationId.GetString();
        return parameters;
    }

    private static Tool Create(string name, string description, bool readOnly, string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return new Tool
        {
            Name = name,
            Description = description,
            InputSchema = document.RootElement.Clone(),
            Annotations = new ToolAnnotations { ReadOnlyHint = readOnly, OpenWorldHint = false }
        };
    }

    private static void ValidateArguments(JsonElement schema, IDictionary<string, JsonElement>? arguments)
    {
        if (schema.TryGetProperty("required", out var required))
            foreach (var property in required.EnumerateArray())
            {
                var name = property.GetString()!;
                if (arguments == null || !arguments.ContainsKey(name))
                    throw new ArgumentException("Missing argument: " + name);
            }
        if (arguments == null)
            return;

        var properties = schema.GetProperty("properties");
        foreach (var (name, value) in arguments)
        {
            if (!properties.TryGetProperty(name, out var property))
                throw new ArgumentException("Unknown argument: " + name);
            if (!Matches(value, property))
                throw new ArgumentException("Invalid argument: " + name + ". See the tool schema.");
        }
    }

    private static bool Matches(JsonElement value, JsonElement schema)
    {
        switch (schema.GetProperty("type").GetString())
        {
            case "string":
                return MatchesString(value, schema);
            case "integer":
                return value.ValueKind == JsonValueKind.Number && (value.TryGetInt64(out _) || value.TryGetUInt64(out _))
                    && (!schema.TryGetProperty("minimum", out var minimum) || value.GetDecimal() >= minimum.GetDecimal())
                    && (!schema.TryGetProperty("maximum", out var maximum) || value.GetDecimal() <= maximum.GetDecimal());
            case "array":
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > schema.GetProperty("maxItems").GetInt32())
                    return false;
                var items = schema.GetProperty("items");
                foreach (var item in value.EnumerateArray())
                    if (!Matches(item, items))
                        return false;
                return true;
            case "object":
                return value.ValueKind == JsonValueKind.Object;
            default:
                throw new InvalidOperationException("Unsupported debug tool schema.");
        }
    }

    private static bool MatchesString(JsonElement value, JsonElement schema)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            return false;
        var text = value.GetString();
        if (schema.TryGetProperty("format", out var format) && format.GetString() == "uuid")
            return Guid.TryParseExact(text, "D", out _);
        if (!schema.TryGetProperty("enum", out var options))
            return true;
        foreach (var option in options.EnumerateArray())
            if (option.GetString() == text)
                return true;
        return false;
    }
}
