using System.Text.Json.Serialization;

namespace AstroGoblinVideoBot.Model;

public readonly struct SubmitResponse
{
    [JsonPropertyName("json")]
    public Details Details { get; init; }
}

public readonly struct Details
{
    [JsonPropertyName("errors")]
    public List<List<object>> Errors { get; init; }
}