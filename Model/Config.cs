using System.Text.Json.Serialization;

namespace AstroGoblinVideoBot.Model;

public readonly struct Config
{
    [JsonPropertyName("RedditAuthorizeUrl")]
    public string RedditAuthorizeUrl { get; init; }
    
    [JsonPropertyName("RedditAccessTokenUrl")]
    public string RedditAccessTokenUrl { get; init; }
    
    [JsonPropertyName("RedditSubmitUrl")]
    public string RedditSubmitUrl { get; init; }
    
    [JsonPropertyName("RedditUserAgent")]
    public string RedditUserAgent { get; init; }
    
    [JsonPropertyName("Subreddit")]
    public string Subreddit { get; init; }
    
    [JsonPropertyName("GooglePubSubUrl")]
    public string GooglePubSubUrl { get; init; }
    
    [JsonPropertyName("GooglePubSubTopic")]
    public string GooglePubSubTopic { get; init; }
}