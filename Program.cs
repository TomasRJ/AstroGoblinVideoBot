using System.Data.SQLite;
using AstroGoblinVideoBot;
using AstroGoblinVideoBot.Model;
using Dapper;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder();
builder.Services.AddRazorPages().WithRazorPagesRoot("/Frontend");
if (args.Contains("--enable-http-logging"))
    builder.Services.AddHttpLogging(_ => { });

var app = builder.Build();
var logger = app.Logger;
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
app.MapRazorPages();
app.UseCertificateForwarding();
app.UseAntiforgery();
if (args.Contains("--enable-http-logging"))
    app.UseHttpLogging();
app.UseStatusCodePages();

var config = new ConfigurationBuilder().AddJsonFile("config.json", optional:false).Build().Get<Config>();
var userSecret = new ConfigurationBuilder().AddUserSecrets<Program>(optional:false).Build().Get<Credentials>();

var youtubeSubscriber = new YoutubeSubscriber(userSecret, config, logger);
var redditPoster = new RedditPoster(userSecret, config, logger);

var isSubscribed = await youtubeSubscriber.SubscribeToChannel();
if (!isSubscribed)
    return;

var shutdownToken = app.Lifetime.ApplicationStopping;

app.MapGet("/youtube", async pubSubHubbub =>
{
    var query = pubSubHubbub.Request.Query;
    var topic = query["hub.topic"];
    var challenge = query["hub.challenge"];
    var mode = query["hub.mode"];
    var leaseSeconds = query["hub.lease_seconds"];
    
    if (!string.IsNullOrEmpty(challenge) && mode.Equals("subscribe") && topic.Equals(config.GooglePubSubTopic))
    {
        logger.LogInformation("Google PubSubHubbub verification request received");
        pubSubHubbub.Response.ContentType = "text/plain";
        await pubSubHubbub.Response.WriteAsync(challenge!);
        logger.LogInformation("Google PubSubHubbub verification successful, now successfully subscribed to the Youtube channel");
    }

    if (!string.IsNullOrEmpty(leaseSeconds))
    {
        var leaseSecondsInt = int.Parse(leaseSeconds.ToString());
        await Task.Run(async () =>
        {
            await Task.Delay(leaseSecondsInt * 1000, shutdownToken);
            logger.LogInformation("Resubscribing to Google PubSubHubbub");
            await youtubeSubscriber.SubscribeToChannel();
        });
        return;
    }
    
    logger.LogInformation("Got unknown Google PubSubHubbub request");
    pubSubHubbub.Response.StatusCode = 200;
});

SQLiteConnection sqLiteConnection = new($"Data Source=reddit.sqlite;Version=3;");
app.MapGet("/redditRedirect",async redditRedirect =>
{
    logger.LogInformation("Reddit redirect request received");
    
    var query = redditRedirect.Request.Query;
    var code = query.ContainsKey("code") ? query["code"].ToString() : throw new InvalidOperationException();
    var state = query.ContainsKey("state") ? query["state"].ToString() : throw new InvalidOperationException();
    
    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || query.ContainsKey("error"))
    {
        logger.LogError("Got the following error from Reddit: {Error}", query["error"]!);
        return;
    }
    
    await redditPoster.SaveOauthTokenToDb(code);
    
    const string doesStateStringExistQuery = "SELECT EXISTS(SELECT 1 FROM FormAuth WHERE Id = 'StateString')";
    var doesStateStringExist = await sqLiteConnection.QueryFirstAsync<bool>(doesStateStringExistQuery);
    if(doesStateStringExist) 
        await redditPoster.AuthorizeForm(redditRedirect, state);
    
    logger.LogError("State string does not exist in the database");
});

app.MapPost("/youtube", async youtubeSubscriptionRequest =>
{
    var videoFeed = await youtubeSubscriber.GetVideoFeed(youtubeSubscriptionRequest);
    await redditPoster.PostVideoToReddit(videoFeed);
});

await app.RunAsync();