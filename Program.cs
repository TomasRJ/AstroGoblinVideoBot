using System.Data.SQLite;
using AstroGoblinVideoBot;
using AstroGoblinVideoBot.Model;
using Dapper;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder();
builder.Services.AddRazorPages().WithRazorPagesRoot("/Frontend");
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddJsonFile("config.json", false);

if (args.Contains("--enable-http-logging"))
    builder.Services.AddHttpLogging(_ => { });

const string logFormat = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:w}:{NewLine}{Message}{Exception}{NewLine}";
var serilog = new LoggerConfiguration().WriteTo.Console(outputTemplate: logFormat).CreateLogger();

if (args.Contains("--save-logs"))
{
    Directory.CreateDirectory("./logs");
    serilog = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console(
            outputTemplate: logFormat,
            restrictedToMinimumLevel: LogEventLevel.Information
        )
        .WriteTo.File(
            "logs/.log",
            outputTemplate: logFormat,
            rollingInterval: RollingInterval.Month
        )
        .CreateLogger();
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

var config = app.Configuration.Get<Config>();
var userSecret = app.Configuration.Get<Credentials>();
var responseSaver = args.Contains("--save-responses");

var youtubeController = new YoutubeController(userSecret, config, logger, responseSaver);
var redditController = new RedditController(userSecret, config, logger, responseSaver);

var isSubscribed = await youtubeController.SubscribeToChannel();
if (!isSubscribed)
    return;

var shutdownToken = app.Lifetime.ApplicationStopping;

app.MapGet("/youtube", async pubSubHubbub =>
{
    var query = pubSubHubbub.Request.Query;
    if (await youtubeController.HandlePubSubRequest(query, pubSubHubbub, shutdownToken)) return;

    logger.LogInformation("Got unknown request at the /youtube endpoint: {Query}", query);
    pubSubHubbub.Response.StatusCode = 200;
});

SQLiteConnection sqLiteConnection = new("Data Source=reddit.sqlite;Version=3;");
app.MapGet("/redditRedirect", async redditRedirect =>
{
    logger.LogInformation("Reddit redirect request received");

    var query = redditRedirect.Request.Query;
    var code = query.ContainsKey("code") ? query["code"].ToString() : throw new InvalidOperationException();
    var state = query.ContainsKey("state") ? query["state"].ToString() : throw new InvalidOperationException();

    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || query.ContainsKey("error"))
    {
        // ReSharper disable once RedundantSuppressNullableWarningExpression
        logger.LogError("Got the following error from Reddit: {Error}", query["error"]!);
        return;
    }

    await redditController.SaveOauthTokenToDb(code);

    const string doesStateStringExistQuery = "SELECT EXISTS(SELECT 1 FROM FormAuth WHERE Id = 'StateString')";
    var doesStateStringExist = await sqLiteConnection.QueryFirstAsync<bool>(doesStateStringExistQuery);

    if (doesStateStringExist)
        await redditController.AuthorizeForm(redditRedirect, state);
    else
        logger.LogError("State string does not exist in the database");
});

app.MapPost("/youtube", async youtubeSubscriptionRequest =>
{
    var videoFeed = await youtubeController.GetVideoFeed(youtubeSubscriptionRequest);

    if (await redditController.IsVideoAlreadySubmitted(videoFeed))
        return;

    await redditController.HandleRedditSubmission(videoFeed);
});

await app.RunAsync();