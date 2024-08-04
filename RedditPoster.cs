using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
        SetBasicAuthHeader();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", authorizationCode },
            { "redirect_uri", UserSecret.RedditRedirectUrl }
        });
        
        var authResponse = await RedditHttpClient.PostAsync(Config.RedditAccessTokenUrl, content);
        
        if (authResponse.StatusCode == HttpStatusCode.OK)
        {
            var oauthToken = await authResponse.Content.ReadFromJsonAsync<OauthToken>();
            WriteOauthToken(oauthToken);
            return oauthToken;
        }
        
        Console.WriteLine("Failed to authenticate with Reddit");
        var responseContent = await authResponse.Content.ReadAsStringAsync();
        Console.WriteLine(responseContent);
        throw new HttpRequestException("Failed to authenticate with Reddit");
    }
    
    public static async Task<bool> SubmitVideo(OauthToken oauthToken, VideoFeed videoFeed)
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
            return false;
        }
        
        Console.WriteLine("Successfully submitted video to Reddit");
        return true;
    }
    public static async Task<OauthToken> GetNewOathToken (string refreshToken)
    {
        SetBasicAuthHeader();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "refresh_token" },
            { "refresh_token", refreshToken }
        });
        
        var authResponse = await RedditHttpClient.PostAsync(Config.RedditAccessTokenUrl, content);
        
        if (authResponse.StatusCode == HttpStatusCode.OK)
        {
            var oauthToken = await authResponse.Content.ReadFromJsonAsync<OauthToken>();
            WriteOauthToken(oauthToken);
            return oauthToken;
        }
        
        Console.WriteLine("Failed to get refresh token from Reddit");
        var responseContent = await authResponse.Content.ReadAsStringAsync();
        Console.WriteLine(responseContent);
        throw new HttpRequestException("Failed to get refresh token from Reddit");
    }
    
    private static void SetBasicAuthHeader()
    {
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{UserSecret.RedditClientId}:{UserSecret.RedditSecret}"));
        RedditHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
    }
    
    private const string RefreshTokenDetailsFilename = "refreshTokenDetails.json";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    
    private static void WriteOauthToken(OauthToken oauthToken)
    {
        var oauthTokenJson = JsonSerializer.Serialize(oauthToken, SerializerOptions);
        File.WriteAllText("redditOathToken.json", oauthTokenJson, Encoding.UTF8);
        
        var expireTimestamp = DateTimeOffset.UtcNow.AddSeconds(oauthToken.ExpiresIn).ToUnixTimeSeconds();
        var refreshTokenDetails = new RefreshTimestamp { Timestamp = expireTimestamp };
        var refreshTokenDetailsJson = JsonSerializer.Serialize(refreshTokenDetails, SerializerOptions);
        File.WriteAllText(RefreshTokenDetailsFilename, refreshTokenDetailsJson, Encoding.UTF8);
    }
    
    public static bool OathTokenFileExists(out OauthToken oauthToken)
    {
        if (!File.Exists("redditOathToken.json")) 
        {
            oauthToken = new OauthToken();
            return false;
        }
        
        var oauthTokenText = File.ReadAllText("redditOathToken.json", Encoding.UTF8);
        oauthToken = JsonSerializer.Deserialize<OauthToken>(oauthTokenText);
        return true;
    }

    public static bool IsTokenExpired()
    {
        if (!File.Exists(RefreshTokenDetailsFilename)) 
            throw new FileNotFoundException("Refresh token details file not found");
        
        var refreshTokenDetailsText = File.ReadAllText(RefreshTokenDetailsFilename, Encoding.UTF8);
        var refreshTokenDetails = JsonSerializer.Deserialize<RefreshTimestamp>(refreshTokenDetailsText);
        
        return refreshTokenDetails.Timestamp < DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}