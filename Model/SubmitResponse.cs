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
    
    [JsonPropertyName("data")]
    public Data Data { get; init; }
}

public readonly struct Data
{
    [JsonPropertyName("url")]
    public string Url { get; init; }
    
    [JsonPropertyName("drafts_count")]
    public int DraftsCount { get; init; }
    
    [JsonPropertyName("id")]
    public string Id { get; init; }
    
    [JsonPropertyName("name")]
    public string Name { get; init; }
}