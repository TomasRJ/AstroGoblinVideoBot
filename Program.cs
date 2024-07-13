using System.Xml.Serialization;
using AstroGoblinVideoBot;
using AstroGoblinVideoBot.Model;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var config = new ConfigurationBuilder().AddJsonFile("config.json").Build().Get<Config>();
var userSecret = new ConfigurationBuilder().AddUserSecrets<RedditPoster>().Build().Get<Credentials>();

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
    var headers = youtubeRequest.Request.Headers;
    var signatureHeader = headers["X-Hub-Signature"].FirstOrDefault();

    if (YoutubeSubscriber.SignatureExists(signatureHeader, youtubeRequest)) return;
    
    if (YoutubeSubscriber.SignatureFormatCheck(signatureHeader, youtubeRequest, out var signatureParts)) return;
    var signature = signatureParts[1];
    
    var requestBody = new MemoryStream();
    await youtubeRequest.Request.Body.CopyToAsync(requestBody);
    requestBody.Position = 0;
    
    if (!YoutubeSubscriber.VerifySignature(requestBody.ToArray(), userSecret.HmacSecret, signature))
    {
        Console.WriteLine("Invalid signature");
        youtubeRequest.Response.StatusCode = 400;
        return;
    }
    
    requestBody.Position = 0;
    var serializer = new XmlSerializer(typeof(VideoFeed));
    var videoFeed = (VideoFeed) (serializer.Deserialize(requestBody) ?? throw new InvalidOperationException());
    
    await RedditPoster.SubmitVideo(oauthToken, videoFeed);
});

await app.RunAsync();