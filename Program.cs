using System.Xml.Serialization;
using AstroGoblinVideoBot;
using AstroGoblinVideoBot.Model;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

#region Credentials
var userSecret = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build()
    .Get<Credentials>();
var config = new ConfigurationBuilder()
    .AddJsonFile("config.json")
    .Build()
    .Get<Config>();
#endregion

var oauthToken = await RedditPoster.GetOauthToken();

app.MapGet("/", () => "Hello World!");

app.MapPost("/", async context =>
{
    var memoryStream = new MemoryStream();
    await context.Request.Body.CopyToAsync(memoryStream);
    memoryStream.Position = 0;
    
    var serializer = new XmlSerializer(typeof(VideoFeed));
    var videoFeed = (VideoFeed) (serializer.Deserialize(memoryStream) ?? throw new InvalidOperationException());
    
    await RedditPoster.SubmitVideo(oauthToken, videoFeed);
});

await app.RunAsync();