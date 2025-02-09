namespace AstroGoblinVideoBot.Model;

public readonly struct Config
{
    public string RedditAuthorizeUrl { get; init; }
    public string RedditAccessTokenUrl { get; init; }
    public string RedditSubmitUrl { get; init; }
    public string RedditStickyUrl { get; init; }
    public string RedditUserAgent { get; init; }
    public string UserSubmissionsInfo { get; init; }
    public string Subreddit { get; init; }
    public string GooglePubSubUrl { get; init; }
    public string GooglePubSubTopic { get; init; }
}