using System.Text.Json.Serialization;

namespace AstroGoblinVideoBot.Model;

public struct Config
{
    [JsonPropertyName("AccessTokenUrl")]
    public string AccessTokenUrl { get; init; }
    
    [JsonPropertyName("SubmitUrl")]
    public string SubmitUrl { get; init; }
    
    [JsonPropertyName("UserAgent")]
    public string UserAgent { get; init; }
    
    [JsonPropertyName("Subreddit")]
    public string Subreddit { get; init; }
}