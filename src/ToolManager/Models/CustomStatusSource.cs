namespace ToolManager.Models;

using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<CustomStatusSource>))]
public enum CustomStatusSource
{
    None,

    [JsonStringEnumMemberName("Last output line")]
    LastOutputLine,

    [JsonStringEnumMemberName("Explicit status line")]
    ExplicitStatusLine,
}
