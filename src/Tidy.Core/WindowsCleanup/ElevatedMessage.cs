using System.Text.Json.Serialization;

namespace Tidy.Core.WindowsCleanup;

public enum ElevatedMessageType
{
    /// <summary>Started on a handler: <see cref="ElevatedMessage.Key"/>.</summary>
    Working,
    /// <summary>A handler's size, from a check: <see cref="ElevatedMessage.Bytes"/>.</summary>
    Size,
    /// <summary>A handler's progress while cleaning: bytes freed so far.</summary>
    Progress,
    /// <summary>A handler finished cleaning: bytes freed, or <see cref="ElevatedMessage.Error"/>.</summary>
    Done,
}

/// <summary>One line the elevated helper sends back to the app.</summary>
public sealed record ElevatedMessage(
    [property: JsonPropertyName("type")] ElevatedMessageType Type,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("bytes")] long Bytes = 0,
    [property: JsonPropertyName("error")] string? Error = null);

/// <summary>One message per line on the pipe, so never indented.</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(ElevatedMessage))]
internal sealed partial class ElevatedJsonContext : JsonSerializerContext;
