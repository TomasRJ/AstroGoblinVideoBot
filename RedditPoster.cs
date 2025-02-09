using System.Data.SQLite;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AstroGoblinVideoBot.Model;
using Dapper;

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
        CreateRedditDatabase().Wait();
    }
    private void SetBasicAuthHeader()
    {
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_userSecret.RedditClientId}:{_userSecret.RedditSecret}"));
        _redditHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
    }
    public async Task PostVideoToReddit(VideoFeed videoFeed)
    {
        if (!RedditOathTokenExist(out var oauthToken))
        {
            _logger.LogError("Reddit Oauth token not found / does not exist");
            return;
        }
        
        if (IsTokenExpired())
            oauthToken = await RefreshRedditOathToken(oauthToken.RefreshToken);
        
        await SubmitVideo(oauthToken, videoFeed);
    }

    #region OauthToken
    public async Task SaveOauthTokenToDb(string authorizationCode)
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
            await AddOauthTokenToDb(oauthToken);
            return;
        }
        
        var responseContent = await authResponse.Content.ReadAsStringAsync();
        _logger.LogError("Failed to get Oauth token from Reddit, got the following response: {Response}", responseContent);
    }
    
    private async Task<OauthToken> RefreshRedditOathToken (string refreshToken)
    {
        _logger.LogInformation("Refreshing the Reddit Oauth token");
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
            await AddOauthTokenToDb(oauthToken);
            _logger.LogInformation("Successfully got refreshed Reddit Oauth token");
            return oauthToken;
        }
        
        var responseContent = await authResponse.Content.ReadAsStringAsync();
        _logger.LogError("Failed to refresh the Reddit Oauth token, got the following response: {Response}", responseContent);
        return new OauthToken();
    }
        
    private bool RedditOathTokenExist(out OauthToken oauthToken)
    {
        const string oauthTokenQuery = "SELECT OauthToken FROM RedditAuth WHERE Id = 1";
        string result;
        
        try
        {
            result = _sqLiteConnection.QuerySingle<string>(oauthTokenQuery);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get the Reddit Oauth token from the database");
            oauthToken = new OauthToken();
            return false;
        }
        
        _logger.LogInformation("Reddit Oauth token exists");
        oauthToken = JsonSerializer.Deserialize<OauthToken>(result);
        return true;
    }

    private bool IsTokenExpired()
    {
        const string timestampQuery = "SELECT Timestamp FROM RedditAuth WHERE Id = 1";
        var timestamp = _sqLiteConnection.QueryFirst<long>(timestampQuery);
        
        if (timestamp < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            _logger.LogInformation("The Reddit OAuth token has expired");
            return true;
        }
        _logger.LogInformation("The Reddit OAuth token is still valid");
        return false;
    }
    
    private async Task AddOauthTokenToDb(OauthToken oauthToken)
    {
        _logger.LogInformation("Adding the Reddit Oauth token to the database");
        var oauthTokenString = JsonSerializer.Serialize(oauthToken);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + oauthToken.ExpiresIn;
        
        const string insertQuery = "INSERT OR REPLACE INTO RedditAuth (Id, OauthToken, Timestamp) VALUES (1, @OauthToken, @Timestamp)";
        await _sqLiteConnection.ExecuteAsync(insertQuery, new { OauthToken = oauthTokenString, Timestamp = timestamp });
        
        _logger.LogInformation("Successfully added the Reddit Oauth token to the database");
    }
    #endregion 
    
    private async Task SubmitVideo(OauthToken oauthToken, VideoFeed videoFeed)
    {
        _logger.LogInformation("Submitting video to Reddit");
        _redditHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oauthToken.AccessToken);
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "api_type", "json" },
            { "extension", "json" },
            { "flair_id", _userSecret.SubmitFlairId },
            { "kind", "link" },
            { "resubmit", "true" },
            { "sendreplies", "false" },
            { "sr",  _config.Subreddit },
            { "title", videoFeed.Entry.Title },
            { "url", videoFeed.Entry.Link.Href }
        });
        
        var response = await _redditHttpClient.PostAsync(_config.RedditSubmitUrl, content);
        var submitResponse = await response.Content.ReadFromJsonAsync<SubmitResponse>();
        
        if (response.StatusCode != HttpStatusCode.OK || submitResponse.Details.Errors.Count != 0)
        {
            _logger.LogError("Failed to submit video to Reddit, got the following response: {Errors}",
               string.Join(",", submitResponse.Details.Errors));
            return;
        }
        
        _logger.LogInformation("Successfully submitted video to Reddit");
        await RedditPostModeration(submitResponse, videoFeed);
    }
    
    public async Task AuthorizeForm(HttpContext redditRedirect, string stateString)
    {
        const string authorizeFormQuery = "SELECT Value FROM FormAuth WHERE Id = 'StateString'";
        var authorizeFormStateString = await _sqLiteConnection.QueryFirstAsync<string>(authorizeFormQuery);
        
        byte[] bodyText;
        if (authorizeFormStateString.Equals(stateString))
        {
            bodyText = "Authorization successful and state matches. "u8.ToArray();
            await redditRedirect.Response.BodyWriter.WriteAsync(bodyText);
            
            _logger.LogInformation("Authorization successful and state matches");
            return;
        }
        
        _logger.LogError("The state string from reddit does not does not match the state string from the authorization form");
        bodyText = "State does not match"u8.ToArray();
        await redditRedirect.Response.BodyWriter.WriteAsync(bodyText);
        
        redditRedirect.Response.StatusCode = 400;
    }

    #region Database
    private const string RedditDb = "reddit.sqlite";
    private readonly SQLiteConnection _sqLiteConnection = new($"Data Source={RedditDb};Version=3;");
    private async Task CreateRedditDatabase()
    {
        const string checkDbQuery = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'Posts' OR name = 'RedditAuth' OR name = 'FormAuth')";
        var dbExists = await _sqLiteConnection.QueryFirstAsync<bool>(checkDbQuery);
        if(dbExists)
        {
            _logger.LogInformation("The Reddit database already exists");
            return;
        }
        
        _logger.LogInformation("The reddit database does not exist, creating it now");
        const string createPostsTableQuery = "CREATE TABLE Posts (YoutubeVideoId TEXT NOT NULL, RedditPostId TEXT NOT NULL, Timestamp INTEGER NOT NULL, Stickied INTEGER NOT NULL, PRIMARY KEY (YoutubeVideoId))";
        const string createRedditAuthTableQuery = "CREATE TABLE RedditAuth (Id INTEGER NOT NULL, OauthToken TEXT NOT NULL, Timestamp INTEGER NOT NULL, PRIMARY KEY (Id))";
        const string createFormAuthTableQuery = "CREATE TABLE FormAuth (Id TEXT NOT NULL, Value TEXT NOT NULL, PRIMARY KEY (Id))";
        await _sqLiteConnection.ExecuteAsync(createPostsTableQuery);
        await _sqLiteConnection.ExecuteAsync(createRedditAuthTableQuery);
        await _sqLiteConnection.ExecuteAsync(createFormAuthTableQuery);
        
        var stickiedPosts = await GetPosts();
        
        const string postsInsertQuery = "INSERT INTO Posts (YoutubeVideoId, RedditPostId, Timestamp, Stickied) VALUES (@YoutubeVideoId, @RedditPostId, @Timestamp, @Stickied)";
        await _sqLiteConnection.ExecuteAsync(postsInsertQuery, stickiedPosts);

        _logger.LogInformation("Successfully created the Reddit database");
    }
    
    private async Task<List<object>> GetPosts()
    {
        _logger.LogInformation("Getting stickied posts from Reddit");
        
        var subredditPosts = await FetchUserPosts(_config.UserPostsInfo);
        
        if (subredditPosts.Data.After != null)
        {
            var after = subredditPosts.Data.After;
            var page = 2;
            while (after != null)
            {
                _logger.LogInformation("There are more than 25 posts, getting page {Page} of Reddit posts", page++);
                var nextPosts = await FetchUserPosts($"{_config.UserPostsInfo}?after={after}");
                subredditPosts.Data.Children.AddRange(nextPosts.Data.Children);
                after = nextPosts.Data.After;
            }
        }

        _logger.LogInformation("Successfully got stickied posts from Reddit");

        return subredditPosts.Data.Children
            .Select(child => new
            {
                YoutubeVideoId = child.Data.Url.Split("?v=").Last(),
                RedditPostId = child.Data.Name,
                Timestamp = child.Data.TimestampUtc,
                Stickied = child.Data.Stickied ? 1 : 0
            })
            .ToList<object>();
    }
    
    private async Task<RedditSubmissions> FetchUserPosts(string url)
    {
        SetBasicAuthHeader();
        var response = await _redditHttpClient.GetAsync(url);

        if (response.StatusCode == HttpStatusCode.OK)
            return await response.Content.ReadFromJsonAsync<RedditSubmissions>();
        
        _logger.LogError("Failed to get stickied posts from Reddit, got the following response: {Response}", await response.Content.ReadAsStringAsync());
        throw new Exception("Failed to get stickied posts from Reddit");
    }
    
    public async Task<bool> IsVideoAlreadyPosted(VideoFeed videoFeed)
    {
        _logger.LogInformation("Checking if the Youtube video exists in the database");
        const string doesVideoExistsQuery = "SELECT EXISTS(SELECT 1 FROM Posts WHERE YoutubeVideoId = @youtubeVideoId)";
        var result = await _sqLiteConnection.QueryFirstAsync<bool>(doesVideoExistsQuery, new { youtubeVideoId = videoFeed.Entry.VideoId });
        
        if (result)
        {
            _logger.LogInformation("The Youtube video already exists in the database, skipping the Reddit post submission");
            return true;
        }
        
        _logger.LogInformation("The Youtube video does not exist in the database, continuing with the Reddit post submission");
        return false;
    }
    #endregion
    
    #region RedditPostModeration
    private async Task RedditPostModeration(SubmitResponse submitResponse, VideoFeed videoFeed)
    {
        _logger.LogInformation("Starting Reddit post moderation");
        var oldRedditPostId = await GetOldestRedditStickyPostId();
        _logger.LogInformation("Successfully got the oldest Reddit sticky post: {RedditPostId}", oldRedditPostId);
        
        await InsertNewVideo(submitResponse, videoFeed);
        
        // Sticking a post immediately after submitting it down-ranks it in the Reddit algorithm.
        // This check makes the 2nd and 3rd most recent video sticky instead of the 1st and 2nd most recent video. 
        if (await IsPreviousVideoStickied(videoFeed))
            return;
        
        await UpdatePreviousVideo(oldRedditPostId, videoFeed);
        
        await UnstickyOldRedditPost(oldRedditPostId);
        
        await StickyNewRedditPost(submitResponse);
        _logger.LogInformation("Successfully finished Reddit post moderation");
    }
    
    private async Task<string> GetOldestRedditStickyPostId()
    {
        _logger.LogInformation("Getting the oldest Reddit sticky post");
        const string oldestRedditStickyPostQuery = "SELECT RedditPostId FROM Posts WHERE Stickied = 1 ORDER BY Timestamp LIMIT 1";
        return await _sqLiteConnection.QueryFirstAsync<string>(oldestRedditStickyPostQuery);
    }
    
    private async Task<bool> IsPreviousVideoStickied(VideoFeed latestVideo)
    {
        const string latestVideoQuery = "SELECT Stickied FROM Posts WHERE YoutubeVideoId != @videoId ORDER BY Timestamp DESC";
        return await _sqLiteConnection.QueryFirstAsync<bool>(latestVideoQuery, new { videoId = latestVideo.Entry.VideoId });
    }
    
    private async Task InsertNewVideo(SubmitResponse submitResponse, VideoFeed videoFeed)
    {
        _logger.LogInformation("Updating the Reddit posts database");

        const string insertNewestPostQuery = "INSERT INTO Posts (YoutubeVideoId, RedditPostId, Timestamp, Stickied) VALUES (@YoutubeVideoId, @RedditPostId, @Timestamp, 0)";
        await _sqLiteConnection.ExecuteAsync(insertNewestPostQuery, new 
            {
                YoutubeVideoId = videoFeed.Entry.VideoId,
                RedditPostId = submitResponse.Details.Data.Name,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() 
            });

        _logger.LogInformation("Successfully updated the Reddit posts database");
    }

    private async Task UpdatePreviousVideo(string oldRedditPostId, VideoFeed videoFeed)
    {
        _logger.LogInformation("Updating the previous video in the Reddit database");
        const string getPreviousVideoIdQuery = "SELECT YoutubeVideoId FROM Posts WHERE YoutubeVideoId != @videoId ORDER BY Timestamp DESC";
        var previousVideoId = await _sqLiteConnection.QueryFirstAsync<string>(getPreviousVideoIdQuery,
            new { videoId = videoFeed.Entry.VideoId });
        const string previousVideoUpdateQuery = "UPDATE Posts SET Stickied = 1 WHERE YoutubeVideoId = @videoId";
        await _sqLiteConnection.ExecuteAsync(previousVideoUpdateQuery, new { videoId = previousVideoId });
        _logger.LogInformation("Successfully updated the the previous video in the Reddit database");
        
        _logger.LogInformation("Updating the 3rd latest video in the Reddit database");
        const string unstickyQuery = "UPDATE Posts SET Stickied = 0 WHERE RedditPostId = @redditPostId";
        await _sqLiteConnection.ExecuteAsync(unstickyQuery, new { redditPostId = oldRedditPostId });
        _logger.LogInformation("Successfully the 3rd latest video in the Reddit database");
    }

    private async Task UnstickyOldRedditPost(string oldRedditPostId)
    {
        _logger.LogInformation("Unsticking the old Reddit post");
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "api_type", "json" },
            { "id", oldRedditPostId },
            { "state", "false" }
        });
        
        var response = await _redditHttpClient.PostAsync(_config.RedditStickyUrl, content);
        
        var unstickyPostResponse = await response.Content.ReadFromJsonAsync<SubmitResponse>();
        
        if (response.StatusCode != HttpStatusCode.OK || unstickyPostResponse.Details.Errors.Count != 0)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to unsticky the old Reddit post, got the following response: {Response}", responseContent);
        }
        
        _logger.LogInformation("Successfully unstuck the old Reddit post");
    }

    private async Task StickyNewRedditPost(SubmitResponse submitResponse)
    {
        _logger.LogInformation("Sticking the new Reddit post");
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "api_type", "json" },
            { "id", submitResponse.Details.Data.Name },
            { "state", "true" }
        });
        
        var response = await _redditHttpClient.PostAsync(_config.RedditStickyUrl, content);
        
        var stickyPostResponse = await response.Content.ReadFromJsonAsync<SubmitResponse>();
        
        if (response.StatusCode != HttpStatusCode.OK || stickyPostResponse.Details.Errors.Count != 0)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to sticky the new Reddit post, got the following response: {Response}", responseContent);
        }
        
        _logger.LogInformation("Successfully stuck the new Reddit post");
    }
    #endregion    
}