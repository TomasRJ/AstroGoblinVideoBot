using System.Text.Json.Serialization;

namespace AstroGoblinVideoBot.Model;

public readonly struct SubredditPostsInfo
{
    [JsonPropertyName("data")]
    public SubredditData Data { get; init; }
}

public readonly struct SubredditData
{
    [JsonPropertyName("children")]
    public List<Child> Children { get; init; }
    
    [JsonPropertyName("after")]
    public string? After { get; init; }
}

public readonly struct Child
{
    [JsonPropertyName("data")]
    public ChildData Data { get; init; }
}

public readonly struct ChildData
{
    [JsonPropertyName("name")]
    public string Name { get; init; }
    
    [JsonPropertyName("url")]
    public string Url { get; init; }
    
    [JsonPropertyName("created_utc")]
    public double TimestampUtc { get; init; }
    
    [JsonPropertyName("stickied")]
    public bool Stickied { get; init; }
}