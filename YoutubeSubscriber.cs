using AstroGoblinVideoBot.Model;

namespace AstroGoblinVideoBot;

public abstract class YoutubeSubscriber
{
    private static readonly Config Config = new ConfigurationBuilder().AddJsonFile("config.json").Build().Get<Config>();
    private static readonly Credentials UserSecret = new ConfigurationBuilder().AddUserSecrets<RedditPoster>().Build().Get<Credentials>();
    private static readonly HttpClient YoutubeHttpClient = new();
    
    public static async Task<bool> SubscribeToChannel()
    {
        var subscribeForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "hub.callback", Config.CallbackUrl },
            { "hub.mode", "subscribe" },
            { "hub.topic", Config.GooglePubSubTopic },
            { "hub.verify", "async" },
            { "hub.secret", UserSecret.HmacSecret }
        });
        var subscribeResponse = await YoutubeHttpClient.PostAsync(Config.GooglePubSubUrl, subscribeForm);
        
        if (subscribeResponse.IsSuccessStatusCode)
        {
            Console.WriteLine("Successfully subscribed to Youtube channel");
            return true;
        }
        
        Console.WriteLine("Failed to subscribe to Youtube channel");
        return false;
    }
}