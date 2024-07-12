namespace AstroGoblinVideoBot.Model;

public readonly struct Credentials
{
    public string HmacSecret { get; init; }
    public string RedditClientId { get; init; }
    public string RedditSecret { get; init; }
}