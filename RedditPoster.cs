using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using AstroGoblinVideoBot.Model;

namespace AstroGoblinVideoBot;

public class RedditPoster
{
    private readonly Config _config;
    private readonly Credentials _userSecret;
    private readonly HttpClient _redditHttpClient = new();
    private readonly ILogger _logger;

    public RedditPoster(Credentials credentials, Config config, ILogger logger)
    {
        _userSecret = credentials;
        _config = config;
        _logger = logger;
        _redditHttpClient.DefaultRequestHeaders.Add("User-Agent", _config.RedditUserAgent);
    }
    
    public async Task PostVideoToReddit(HttpContext youtubeRequest, YoutubeSubscriber youtubeSubscriber, OauthToken oauthToken)
    {
        _logger.LogInformation("Youtube subscription request received");
        if (string.IsNullOrEmpty(oauthToken.AccessToken) || !OathTokenFileExists(out oauthToken))
        {
            _logger.LogError("Reddit Oauth token not found / does not exist");
            return;
        }
        
        if (IsTokenExpired())
            oauthToken = await GetNewOathToken(oauthToken.RefreshToken);
        
        var requestBody = new MemoryStream();
        await youtubeRequest.Request.Body.CopyToAsync(requestBody);
        
        if (!SignatureVerification(youtubeRequest, youtubeSubscriber, requestBody))
            return;

        requestBody.Position = 0;
        var serializer = new XmlSerializer(typeof(VideoFeed));
        var videoFeed = (VideoFeed) (serializer.Deserialize(requestBody) ?? throw new InvalidOperationException());
    
        await SubmitVideo(oauthToken, videoFeed);
    }

    private bool SignatureVerification(HttpContext youtubeRequest, YoutubeSubscriber youtubeSubscriber, MemoryStream requestBody)
    {
        var headers = youtubeRequest.Request.Headers;
        var signatureHeader = headers["X-Hub-Signature"].FirstOrDefault();
    
        if (!youtubeSubscriber.SignatureExists(signatureHeader, youtubeRequest)) return false;
    
        if (!youtubeSubscriber.SignatureFormatCheck(signatureHeader, youtubeRequest, out var signatureParts)) return false;
        var signature = signatureParts[1];

        requestBody.Position = 0;

        if (youtubeSubscriber.VerifySignature(requestBody.ToArray(), _userSecret.HmacSecret, signature))
            return true;
        
        youtubeRequest.Response.StatusCode = 200;
        return false;
    }

    public async Task<OauthToken> GetOauthToken(string authorizationCode)
    {
        _logger.LogInformation("Getting Oauth token from Reddit redirect request");
        SetBasicAuthHeader();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", authorizationCode },
            { "redirect_uri", _userSecret.RedditRedirectUrl }
        });
        
        var authResponse = await _redditHttpClient.PostAsync(_config.RedditAccessTokenUrl, content);
        
        if (authResponse.StatusCode == HttpStatusCode.OK)
        {
            var oauthToken = await authResponse.Content.ReadFromJsonAsync<OauthToken>();
            _logger.LogInformation("Successfully got Oauth token from Reddit");
            WriteOauthToken(oauthToken);
            return oauthToken;
        }
        
        var responseContent = await authResponse.Content.ReadAsStringAsync();
        _logger.LogError("Failed to get Oauth token from Reddit, got the following response: {Response}", responseContent);
        return new OauthToken();
    }
    
    private async Task SubmitVideo(OauthToken oauthToken, VideoFeed videoFeed)
    {
        _logger.LogInformation("Submitting video to Reddit");
        _redditHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oauthToken.AccessToken);
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
        var response = await _redditHttpClient.PostAsync(_config.RedditSubmitUrl, content);
        var submitResponse = await response.Content.ReadFromJsonAsync<SubmitResponse>();
        
        if (response.StatusCode != HttpStatusCode.OK || submitResponse.Details.Errors.Count != 0)
            _logger.LogError("Failed to submit video to Reddit, got the following response: {Errors}", submitResponse.Details.Errors);
        
        _logger.LogInformation("Successfully submitted video to Reddit");
    }
    private async Task<OauthToken> GetNewOathToken (string refreshToken)
    {
        _logger.LogInformation("Getting new Oauth token from Reddit");
        SetBasicAuthHeader();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "refresh_token" },
            { "refresh_token", refreshToken }
        });
        
        var authResponse = await _redditHttpClient.PostAsync(_config.RedditAccessTokenUrl, content);
        
        if (authResponse.StatusCode == HttpStatusCode.OK)
        {
            var oauthToken = await authResponse.Content.ReadFromJsonAsync<OauthToken>();
            WriteOauthToken(oauthToken);
            _logger.LogInformation("Successfully got new Oauth token from Reddit");
            return oauthToken;
        }
        
        
        var responseContent = await authResponse.Content.ReadAsStringAsync();
        _logger.LogError("Failed to get refresh token from Reddit, got the following response: {Response}", responseContent);
        return new OauthToken();
    }
    
    private void SetBasicAuthHeader()
    {
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_userSecret.RedditClientId}:{_userSecret.RedditSecret}"));
        _redditHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
    }
    
    private const string RefreshTokenDetailsFilename = "refreshTokenDetails.json";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    
    private void WriteOauthToken(OauthToken oauthToken)
    {
        var oauthTokenJson = JsonSerializer.Serialize(oauthToken, SerializerOptions);
        File.WriteAllText("redditOathToken.json", oauthTokenJson, Encoding.UTF8);
        _logger.LogInformation("Successfully wrote Oauth token to file");
        
        var expireTimestamp = DateTimeOffset.UtcNow.AddSeconds(oauthToken.ExpiresIn).ToUnixTimeSeconds();
        var refreshTokenDetails = new RefreshTimestamp { Timestamp = expireTimestamp };
        var refreshTokenDetailsJson = JsonSerializer.Serialize(refreshTokenDetails, SerializerOptions);
        File.WriteAllText(RefreshTokenDetailsFilename, refreshTokenDetailsJson, Encoding.UTF8);
        _logger.LogInformation("Successfully wrote refresh token details to file");
    }
    
    private bool OathTokenFileExists(out OauthToken oauthToken)
    {
        if (!File.Exists("redditOathToken.json")) 
        {
            _logger.LogError("Reddit OAuth token not found");
            oauthToken = new OauthToken();
            return false;
        }
        
        var oauthTokenText = File.ReadAllText("redditOathToken.json", Encoding.UTF8);
        oauthToken = JsonSerializer.Deserialize<OauthToken>(oauthTokenText);
        _logger.LogInformation("The Reddit OAuth token exists");
        return true;
    }

    private bool IsTokenExpired()
    {
        if (!File.Exists(RefreshTokenDetailsFilename)) 
        {
            _logger.LogError("Refresh token details file was not found");
            return false;
        }
        
        var refreshTokenDetailsText = File.ReadAllText(RefreshTokenDetailsFilename, Encoding.UTF8);
        var refreshTokenDetails = JsonSerializer.Deserialize<RefreshTimestamp>(refreshTokenDetailsText);
        
        if (refreshTokenDetails.Timestamp < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            _logger.LogError("The Reddit OAuth token has expired");
            return true;
        }
        _logger.LogInformation("Reddit OAuth token is still valid");
        return false;
    }
}