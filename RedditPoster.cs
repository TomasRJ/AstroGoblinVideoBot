using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AstroGoblinVideoBot.Model;

namespace AstroGoblinVideoBot;

public abstract class RedditPoster
{
    private static readonly Config Config = new ConfigurationBuilder().AddJsonFile("config.json", optional:false).Build().Get<Config>();
    private static readonly Credentials UserSecret = new ConfigurationBuilder().AddUserSecrets<RedditPoster>(optional:false).Build().Get<Credentials>();
    private static readonly HttpClient RedditHttpClient = new();

    static RedditPoster() => RedditHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Config.RedditUserAgent);

    public static async Task<OauthToken> GetOauthToken(string authorizationCode)
    {
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{UserSecret.RedditClientId}:{UserSecret.RedditSecret}"));
        RedditHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
        
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", authorizationCode },
            { "redirect_uri", "https://localhost:7018/redditRedirect" }
        });
        
        var authResponse = await RedditHttpClient.PostAsync(Config.RedditAccessTokenUrl, content);
        
        if (authResponse.StatusCode == HttpStatusCode.OK)
            return await authResponse.Content.ReadFromJsonAsync<OauthToken>();
        
        Console.WriteLine("Failed to authenticate with Reddit");
        var responseContent = await authResponse.Content.ReadAsStringAsync();
        Console.WriteLine(responseContent);
        throw new HttpRequestException("Failed to authenticate with Reddit");
    }

    public static async Task SubmitVideo(OauthToken oauthToken, VideoFeed videoFeed)
    {
        RedditHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oauthToken.AccessToken);
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "api_type", "json" },
            { "sendreplies", "false" },
            { "resubmit", "true" },
            { "title", videoFeed.Entry.Title },
            { "kind", "link" },
            { "sr", "test" },
            { "url", videoFeed.Entry.Link.Href }
        });
        var response = await RedditHttpClient.PostAsync(Config.RedditSubmitUrl, content);
        var submitResponse = await response.Content.ReadFromJsonAsync<SubmitResponse>();
        
        if (response.StatusCode != HttpStatusCode.OK || submitResponse.Details.Errors.Count != 0)
        {
            foreach (var error in submitResponse.Details.Errors)
            {
                Console.WriteLine(error);
            }
            
            var contentString = await response.Content.ReadAsStringAsync();
            Console.WriteLine(contentString);
        }
        
        Console.WriteLine("Successfully submitted video to Reddit");
    }
}