using System.Text.Json.Serialization;

namespace AstroGoblinVideoBot.Model;

public readonly struct RefreshTimestamp
{
    [JsonPropertyName("RedditRefreshTimestamp")]
    public long Timestamp { get; init; }
}