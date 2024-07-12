namespace AstroGoblinVideoBot.Model;

public readonly struct Credentials
{
    public string HmacSecret { get; init; }
    public string RedditUsername { get; init; }
    public string RedditPassword { get; init; }
}