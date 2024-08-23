using System.ComponentModel.DataAnnotations;

namespace AstroGoblinVideoBot.Model;

public class RedditAuthorizeForm
{
    [Required]
    public string? RedditClientId { get; init; }
    public string? RedditResponseType { get; init; }
    [Required]
    public string? StateString { get; init; }
    [MinLength(3)]
    [Required]
    public string? RedirectUrl { get; init; }
    public string? Duration { get; init; }
    public string? Scope { get; init; }
}