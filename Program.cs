using System.Data.SQLite;
using AstroGoblinVideoBot;
using AstroGoblinVideoBot.Model;
using Dapper;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;

var builder = WebApplication.CreateBuilder();
builder.Services.AddRazorPages().WithRazorPagesRoot("/Frontend");
if (args.Contains("--enable-http-logging"))
    builder.Services.AddHttpLogging(_ => { });
const string logFormat = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:w}:{NewLine}{Message}{Exception}{NewLine}";
var serilog = new LoggerConfiguration().WriteTo.Console(outputTemplate: logFormat).CreateLogger();
if (args.Contains("--save-logs"))
{
    Directory.CreateDirectory("./logs");
    serilog = new LoggerConfiguration()
        .WriteTo.Console(outputTemplate: logFormat)
        .WriteTo.File(
            "logs/.log",
            outputTemplate: logFormat,
            rollingInterval: RollingInterval.Month
        ).CreateLogger();
}
builder.Services.AddSerilog(serilog);

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
    var topic = query.ContainsKey("hub.topic") ? query["hub.topic"].ToString() : throw new InvalidOperationException();
    var challenge = query.ContainsKey("hub.challenge") ? query["hub.challenge"].ToString() : throw new InvalidOperationException();
    var mode = query.ContainsKey("hub.mode") ? query["hub.mode"].ToString() : throw new InvalidOperationException();
    var leaseSeconds = query.ContainsKey("hub.lease_seconds") ? query["hub.lease_seconds"].ToString() : throw new InvalidOperationException();
    
    if (!string.IsNullOrEmpty(challenge) && mode.Equals("subscribe") && topic.Equals(config.GooglePubSubTopic))
    {
        logger.LogInformation("Google PubSubHubbub verification request received");
        pubSubHubbub.Response.ContentType = "text/plain";
        await pubSubHubbub.Response.WriteAsync(challenge);
        logger.LogInformation("Google PubSubHubbub verification successful, now successfully subscribed to the Youtube channel");
    }
    
    if (!string.IsNullOrEmpty(leaseSeconds))
    {
        var leaseSecondsInt = int.Parse(leaseSeconds);
        _ = Task.Run(async () =>
        {
            await Task.Delay(leaseSecondsInt * 1000, shutdownToken);
            logger.LogInformation("Resubscribing to Google PubSubHubbub");
            await youtubeSubscriber.SubscribeToChannel();
        }, shutdownToken);
        return;
    }
    
    logger.LogInformation("Got unknown Google PubSubHubbub request: {Query}", query);
    pubSubHubbub.Response.StatusCode = 200;
});

SQLiteConnection sqLiteConnection = new("Data Source=reddit.sqlite;Version=3;");
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
    else
        logger.LogError("State string does not exist in the database");
});

app.MapPost("/youtube", async youtubeSubscriptionRequest =>
{
    var videoFeed = await youtubeSubscriber.GetVideoFeed(youtubeSubscriptionRequest);
    
    if (await redditPoster.IsVideoAlreadyPosted(videoFeed))
        return;
    
    await redditPoster.PostVideoToReddit(videoFeed);
});

await app.RunAsync();