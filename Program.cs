using AstroGoblinVideoBot;
using AstroGoblinVideoBot.Model;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages().WithRazorPagesRoot("/Frontend");

var app = builder.Build();
var logger = app.Logger;

var config = new ConfigurationBuilder().AddJsonFile("config.json", optional:false).Build().Get<Config>();
var userSecret = new ConfigurationBuilder().AddUserSecrets<Program>(optional:false).Build().Get<Credentials>();

var youtubeSubscriber = new YoutubeSubscriber(userSecret, config, logger);
var redditPoster = new RedditPoster(userSecret, config, logger);

var isSubscribed = await youtubeSubscriber.SubscribeToChannel();
if (!isSubscribed)
    return;

var oauthToken = new OauthToken();

app.MapGet("/youtube", async youtubeVerify =>
{
    logger.LogInformation("Google PubSubHubbub verification request received");
    
    var query = youtubeVerify.Request.Query;
    var topic = query["hub.topic"];
    var challenge = query["hub.challenge"];
    var mode = query["hub.mode"];
    
    if (!string.IsNullOrEmpty(challenge) && mode.Equals("subscribe") && topic.Equals(config.GooglePubSubTopic))
    {
        youtubeVerify.Response.ContentType = "text/plain";
        await youtubeVerify.Response.WriteAsync(challenge!);
        logger.LogInformation("Google PubSubHubbub verification successful, now successfully subscribed to the Youtube channel");
        return;
    }
    youtubeVerify.Response.StatusCode = 400;
    logger.LogInformation("Youtube verification failed");
});

app.MapGet("/redditRedirect",async redditRedirect =>
{
    logger.LogInformation("Reddit redirect request received");
    
    var query = redditRedirect.Request.Query;
    var code = query.ContainsKey("code") ? query["code"].ToString() : throw new InvalidOperationException();
    var state = query.ContainsKey("state") ? query["state"].ToString() : throw new InvalidOperationException();
    
    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || query.ContainsKey("error"))
    {
        logger.LogError("Got the following error from Reddit: {Error}", query["error"]);
        return;
    }
    
    oauthToken = await redditPoster.GetOauthToken(code);
    
    if(File.Exists("authorizeForm.json"))
    {
        await redditPoster.AuthorizeForm(redditRedirect, state);
        return;
    }
    
    var bodyText = "Authorization successful, but no authorizeForm.json found, but the bot should still work."u8.ToArray();
    await redditRedirect.Response.BodyWriter.WriteAsync(bodyText);
    redditRedirect.Response.StatusCode = 200;
});

app.MapPost("/youtube", async youtubeSubscriptionRequest =>
{
   await redditPoster.PostVideoToReddit(youtubeSubscriptionRequest, youtubeSubscriber, oauthToken);
});

app.MapRazorPages();

await app.RunAsync();
