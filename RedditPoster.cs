using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AstroGoblinVideoBot.Model;

namespace AstroGoblinVideoBot;

public abstract class RedditPoster
{
    private static readonly Config Config = new ConfigurationBuilder().AddJsonFile("config.json").Build().Get<Config>();
    private static readonly Credentials UserSecret = new ConfigurationBuilder().AddUserSecrets<RedditPoster>().Build().Get<Credentials>();
    private static readonly HttpClient RedditHttpClient = new();

    static RedditPoster() => RedditHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Config.UserAgent);

    public static async Task<OauthToken> GetOauthToken()
    {
        var basicAuth = Encoding.UTF8.GetBytes($"{UserSecret.RedditClientId}:{UserSecret.RedditSecret}");
        RedditHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(basicAuth));
        var content = new FormUrlEncodedContent(new Dictionary<string, string> { { "grant_type", "client_credentials" } });
        var authResponse = await RedditHttpClient.PostAsync(Config.AccessTokenUrl, content);
        
        if (authResponse.StatusCode == HttpStatusCode.OK)
            return await authResponse.Content.ReadFromJsonAsync<OauthToken>();
        
        Console.WriteLine("Failed to authenticate with Reddit");
        var responseContent = await authResponse.Content.ReadAsStringAsync();
        Console.WriteLine(responseContent);
        throw new HttpRequestException("Failed to authenticate with Reddit");
    }
    
    public static async Task<bool> SubmitVideo(OauthToken oauthToken)
    {
        RedditHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oauthToken.AccessToken);
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "api_type", "json" },
            { "kind", "link" },
            { "sr", Config.Subreddit },
            { "title", "Astro Goblin Video Bot" },
            { "url", "https://www.youtube.com/watch?v=ZQ7o6e4vJ2U" }
        });
        
        var response = await RedditHttpClient.PostAsync(Config.SubmitUrl, content);
        
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var okContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine(okContent);
            return true;
        }
        
        Console.WriteLine("Failed to submit video to Reddit");
        var errorContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine(errorContent);
        return false;
    }
}