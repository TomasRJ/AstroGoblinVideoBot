using System.Xml.Serialization;
using AstroGoblinVideoBot;
using AstroGoblinVideoBot.Model;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var config = new ConfigurationBuilder().AddJsonFile("config.json").Build().Get<Config>();

await YoutubeSubscriber.SubscribeToChannel();

var oauthToken = await RedditPoster.GetOauthToken();

app.MapGet("/youtube", async youtubeVerify =>
{
    var query = youtubeVerify.Request.Query;
    var topic = query["hub.topic"];
    var challenge = query["hub.challenge"];
    var mode = query["hub.mode"];
    
    if (!string.IsNullOrEmpty(challenge) && mode.Equals("subscribe") && topic.Equals(config.GooglePubSubTopic))
    {
        youtubeVerify.Response.ContentType = "text/plain";
        await youtubeVerify.Response.WriteAsync(challenge!);
    }
    else
        youtubeVerify.Response.StatusCode = 400;
});

app.MapPost("/youtube", async youtubeRequest =>
{
    var memoryStream = new MemoryStream();
    await youtubeRequest.Request.Body.CopyToAsync(memoryStream);
    memoryStream.Position = 0;
    
    var serializer = new XmlSerializer(typeof(VideoFeed));
    var videoFeed = (VideoFeed) (serializer.Deserialize(memoryStream) ?? throw new InvalidOperationException());
    
    await RedditPoster.SubmitVideo(oauthToken, videoFeed);
});

await app.RunAsync();