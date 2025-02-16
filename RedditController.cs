using System.Data.SQLite;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AstroGoblinVideoBot.Model;
using Dapper;

namespace AstroGoblinVideoBot;

public class RedditController
{
    private readonly Config _config;
    private readonly ILogger _logger;
    private readonly HttpClient _redditHttpClient = new();
    private readonly Credentials _userSecret;

    private string _redditUrl = "";
    private int _redditModerationRetries;

    public RedditController(Credentials credentials, Config config, ILogger logger)
    {
        _userSecret = credentials;
        _config = config;
        _logger = logger;
        _redditHttpClient.DefaultRequestHeaders.Add("User-Agent", _config.RedditUserAgent);
        CreateRedditDatabase().Wait();
    }

    private void SetBasicAuthHeader()
    {
        var basicAuth =
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_userSecret.RedditClientId}:{_userSecret.RedditSecret}"));
        _redditHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
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

        _logger.LogError(
            "The state string from reddit does not does not match the state string from the authorization form");
        bodyText = "State does not match"u8.ToArray();
        await redditRedirect.Response.BodyWriter.WriteAsync(bodyText);

        redditRedirect.Response.StatusCode = 400;
    }

    public async Task HandleRedditSubmission(VideoFeed videoFeed)
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

    private async Task SubmitVideo(OauthToken oauthToken, VideoFeed videoFeed)
    {
        _logger.LogInformation("Submitting video to Reddit");
        _redditHttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", oauthToken.AccessToken);
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "api_type", "json" },
            { "extension", "json" },
            { "flair_id", _userSecret.SubmitFlairId },
            { "kind", "link" },
            { "resubmit", "true" },
            { "sendreplies", "false" },
            { "sr", _config.Subreddit },
            { "title", videoFeed.Entry.Title },
            { "url", videoFeed.Entry.Link.Href }
        });

        var response = await _redditHttpClient.PostAsync(_config.RedditSubmitUrl, content);
        var submitResponse = await response.Content.ReadFromJsonAsync<SubmitResponse>();

        if (response.StatusCode != HttpStatusCode.OK || submitResponse.Details.Errors.Count != 0)
        {
            var failedResponseContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to submit video to Reddit, got the following response: {Errors}",
                failedResponseContent);
            return;
        }

        _redditUrl = "https://redd.it/" + submitResponse.Details.Data.Id;
        _logger.LogInformation(
            "Successfully submitted video to Reddit at {RedditUrl}, with the following YouTube URL: {YouTubeUrl}",
            _redditUrl, videoFeed.Entry.Link.Href);

        await RedditSubmissionModeration(submitResponse, videoFeed);
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
        _logger.LogError("Failed to get Oauth token from Reddit, got the following response: {Response}",
            responseContent);
        throw new InvalidOperationException("Failed to refresh the Reddit Oauth token");
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

    private async Task<OauthToken> RefreshRedditOathToken(string refreshToken)
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
        _logger.LogError("Failed to refresh the Reddit Oauth token, got the following response: {Response}",
            responseContent);
        throw new InvalidOperationException("Failed to refresh the Reddit Oauth token");
    }

    private async Task AddOauthTokenToDb(OauthToken oauthToken)
    {
        _logger.LogInformation("Adding the Reddit Oauth token to the database");
        var oauthTokenString = JsonSerializer.Serialize(oauthToken);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + oauthToken.ExpiresIn;

        const string insertQuery =
            "INSERT OR REPLACE INTO RedditAuth (Id, OauthToken, Timestamp) VALUES (1, @OauthToken, @Timestamp)";
        await _sqLiteConnection.ExecuteAsync(insertQuery, new { OauthToken = oauthTokenString, Timestamp = timestamp });

        _logger.LogInformation("Successfully added the Reddit Oauth token to the database");
    }

    #endregion

    #region Database

    private const string RedditDb = "reddit.sqlite";
    private readonly SQLiteConnection _sqLiteConnection = new($"Data Source={RedditDb};Version=3;");

    private async Task CreateRedditDatabase()
    {
        const string checkDbQuery =
            "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'Submissions' OR name = 'RedditAuth' OR name = 'FormAuth')";
        var dbExists = await _sqLiteConnection.QueryFirstAsync<bool>(checkDbQuery);
        if (dbExists)
        {
            _logger.LogInformation("The Reddit database already exists");
            return;
        }

        _logger.LogInformation("The reddit database does not exist, creating it now");
        const string createSubmissionTableQuery =
            "CREATE TABLE Submissions (YoutubeVideoId TEXT NOT NULL, RedditSubmissionId TEXT NOT NULL, Timestamp INTEGER NOT NULL, Stickied INTEGER NOT NULL, PRIMARY KEY (YoutubeVideoId))";
        const string createRedditAuthTableQuery =
            "CREATE TABLE RedditAuth (Id INTEGER NOT NULL, OauthToken TEXT NOT NULL, Timestamp INTEGER NOT NULL, PRIMARY KEY (Id))";
        const string createFormAuthTableQuery =
            "CREATE TABLE FormAuth (Id TEXT NOT NULL, Value TEXT NOT NULL, PRIMARY KEY (Id))";
        await _sqLiteConnection.ExecuteAsync(createSubmissionTableQuery);
        await _sqLiteConnection.ExecuteAsync(createRedditAuthTableQuery);
        await _sqLiteConnection.ExecuteAsync(createFormAuthTableQuery);

        var stickiedSubmissions = await GetSubmissions();

        const string insertSubmissionsQuery =
            "INSERT INTO Submissions (YoutubeVideoId, RedditSubmissionId, Timestamp, Stickied) VALUES (@YoutubeVideoId, @RedditSubmissionId, @Timestamp, @Stickied)";
        await _sqLiteConnection.ExecuteAsync(insertSubmissionsQuery, stickiedSubmissions);

        _logger.LogInformation("Successfully created the Reddit database");
    }

    private async Task<List<object>> GetSubmissions()
    {
        _logger.LogInformation("Getting stickied submissions from Reddit");

        var userSubmissions = await FetchUserSubmissions(_config.UserSubmissionsInfo);

        if (userSubmissions.Data.After != null)
        {
            var after = userSubmissions.Data.After;
            var page = 2;
            while (after != null)
            {
                _logger.LogInformation("There are more than 25 submissions, getting page {Page} of Reddit submissions",
                    page++);
                var olderSubmissions = await FetchUserSubmissions($"{_config.UserSubmissionsInfo}?after={after}");
                userSubmissions.Data.Children.AddRange(olderSubmissions.Data.Children);
                after = olderSubmissions.Data.After;
            }
        }

        _logger.LogInformation("Successfully got all Reddit submissions");

        return userSubmissions.Data.Children
            .Select(child => new
            {
                YoutubeVideoId = child.Data.Url.Split("?v=").Last(),
                RedditSubmissionId = child.Data.Name,
                Timestamp = child.Data.TimestampUtc,
                Stickied = child.Data.Stickied ? 1 : 0
            })
            .ToList<object>();
    }

    private async Task<RedditSubmissions> FetchUserSubmissions(string url)
    {
        SetBasicAuthHeader();
        var response = await _redditHttpClient.GetAsync(url);

        if (response.StatusCode == HttpStatusCode.OK)
            return await response.Content.ReadFromJsonAsync<RedditSubmissions>();

        _logger.LogError("Failed to get stickied submission from Reddit, got the following response: {Response}",
            await response.Content.ReadAsStringAsync());
        throw new InvalidOperationException("Failed to get stickied submissions from Reddit");
    }

    public async Task<bool> IsVideoAlreadySubmitted(VideoFeed videoFeed)
    {
        _logger.LogInformation("Checking if the Youtube video exists in the database");
        const string doesVideoExistsQuery =
            "SELECT EXISTS(SELECT 1 FROM Submissions WHERE YoutubeVideoId = @youtubeVideoId)";
        var result = await _sqLiteConnection.QueryFirstAsync<bool>(doesVideoExistsQuery,
            new { youtubeVideoId = videoFeed.Entry.VideoId });

        if (result)
        {
            _logger.LogInformation("The Youtube video already exists in the database, skipping the Reddit submission");
            return true;
        }

        _logger.LogInformation(
            "The Youtube video does not exist in the database, continuing with the Reddit submission");
        return false;
    }

    #endregion

    #region RedditModeration

    private async Task RedditSubmissionModeration(SubmitResponse submitResponse, VideoFeed videoFeed)
    {
        _logger.LogInformation("Starting moderation of the following Reddit submission: {RedditUrl}", _redditUrl);
        await InsertNewVideo(submitResponse, videoFeed);

        // Sticking a submission immediately after submitting it down-ranks it in the Reddit algorithm.
        // The rest of these methods makes the 2nd and 3rd most recent video sticky instead of the 1st and 2nd most recent video.
        var oldRedditSubmissionId = await GetOldestRedditStickySubmissionsId();

        var previousRedditSubmissionId = await UpdatePreviousVideo(oldRedditSubmissionId, videoFeed);

        await UnstickyOldRedditSubmission(oldRedditSubmissionId);

        await StickyRedditSubmission(previousRedditSubmissionId);
        _logger.LogInformation("Successfully finished moderation of Reddit submissions");
    }

    private async Task<string> GetOldestRedditStickySubmissionsId()
    {
        const string oldestRedditStickySubmissionQuery =
            "SELECT RedditSubmissionId FROM Submissions WHERE Stickied = 1 ORDER BY Timestamp LIMIT 1";
        var oldRedditSubmissionId = await _sqLiteConnection.QueryFirstAsync<string>(oldestRedditStickySubmissionQuery);
        _logger.LogInformation("Got the oldest stickied Reddit submission id: {Id}", oldRedditSubmissionId);
        return oldRedditSubmissionId;
    }

    private async Task InsertNewVideo(SubmitResponse submitResponse, VideoFeed videoFeed)
    {
        _logger.LogDebug("Inserting the new Reddit submission to the Reddit database");

        const string insertNewestSubmissionQuery =
            "INSERT INTO Submissions (YoutubeVideoId, RedditSubmissionId, Timestamp, Stickied) VALUES (@YoutubeVideoId, @RedditSubmissionId, @Timestamp, 0)";
        await _sqLiteConnection.ExecuteAsync(insertNewestSubmissionQuery, new
        {
            YoutubeVideoId = videoFeed.Entry.VideoId,
            RedditSubmissionId = submitResponse.Details.Data.Name,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        _logger.LogDebug("Successfully inserted the new Reddit submission");
    }

    private async Task<string> UpdatePreviousVideo(string oldRedditSubmissionId, VideoFeed videoFeed)
    {
        const string getPreviousVideoIdQuery =
            "SELECT YoutubeVideoId FROM Submissions WHERE YoutubeVideoId != @videoId ORDER BY Timestamp DESC";
        var previousVideoId = await _sqLiteConnection.QueryFirstAsync<string>(getPreviousVideoIdQuery,
            new { videoId = videoFeed.Entry.VideoId });
        _logger.LogInformation("Got the previously submitted YouTube video ID: {Id}", previousVideoId);

        const string previousVideoUpdateQuery = "UPDATE Submissions SET Stickied = 1 WHERE YoutubeVideoId = @videoId";
        await _sqLiteConnection.ExecuteAsync(previousVideoUpdateQuery, new { videoId = previousVideoId });

        const string unstickyQuery =
            "UPDATE Submissions SET Stickied = 0 WHERE RedditSubmissionId = @redditSubmissionId";
        await _sqLiteConnection.ExecuteAsync(unstickyQuery, new { redditSubmissionId = oldRedditSubmissionId });

        _logger.LogInformation("Successfully updated the the previous video in the Reddit database");
        return previousVideoId;
    }

    private async Task UnstickyOldRedditSubmission(string oldRedditSubmissionId)
    {
        _logger.LogDebug("Unsticking the old Reddit submission");
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "api_type", "json" },
            { "id", oldRedditSubmissionId },
            { "state", "false" }
        });

        var response = await _redditHttpClient.PostAsync(_config.RedditStickyUrl, content);

        var unstickySubmissionResponse = await response.Content.ReadFromJsonAsync<SubmitResponse>();

        if (response.StatusCode != HttpStatusCode.OK || unstickySubmissionResponse.Details.Errors.Count != 0)
        {
            var failedResponseContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to unsticky the old Reddit submission, got the following response: {Response}",
                failedResponseContent);
            if (response.StatusCode == HttpStatusCode.InternalServerError)
                await RetryModeration(oldRedditSubmissionId, UnstickyOldRedditSubmission);

            return;
        }

        _logger.LogInformation("Successfully unstuck the oldest stickied Reddit submission");
    }

    private async Task StickyRedditSubmission(string redditSubmissionId)
    {
        _logger.LogDebug("Sticking the new Reddit submission");
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "api_type", "json" },
            { "id", redditSubmissionId },
            { "state", "true" }
        });

        var response = await _redditHttpClient.PostAsync(_config.RedditStickyUrl, content);

        var stickySubmissionResponse = await response.Content.ReadFromJsonAsync<SubmitResponse>();

        if (response.StatusCode != HttpStatusCode.OK || stickySubmissionResponse.Details.Errors.Count != 0)
        {
            var failedResponseContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to sticky the new Reddit submission, got the following response: {Response}",
                failedResponseContent);
            if (response.StatusCode == HttpStatusCode.InternalServerError)
                await RetryModeration(redditSubmissionId, UnstickyOldRedditSubmission);
            return;
        }

        _logger.LogInformation("Successfully stuck the following Reddit submission: {RedditUrl}", _redditUrl);
    }
    
    private async Task RetryModeration(string redditSubmissionId, Func<string, Task> superMethod)
    {
        if (_redditModerationRetries > 5)
        {
            _logger.LogWarning("Retried {N} times to unsticky/sticky the post, but Reddit seems to be down at moment." +
                               "The submission now has to be manually stickied", _redditModerationRetries);
            return;
        }
        
        _redditModerationRetries++;
        await Task.Delay(_redditModerationRetries * 1000);
        _logger.LogInformation("Got 500 HTTP error from Reddit, retrying with attempt nr: {Retry}", _redditModerationRetries);
        await superMethod(redditSubmissionId);
    }

    #endregion
}