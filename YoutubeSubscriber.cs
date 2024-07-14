using System.Security.Cryptography;
using System.Text;
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
            { "hub.callback", UserSecret.CallbackUrl },
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
        var responseContent = await subscribeResponse.Content.ReadAsStringAsync();
        Console.WriteLine(responseContent);
        return false;
    }

    public static bool VerifySignature(byte[] payload, string secret, string signature)
    {
        var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
        var hashBytes = hmac.ComputeHash(payload);
        var hashString = Convert.ToBase64String(hashBytes);
        return hashString.Equals(signature);
    }
    
    public static bool SignatureExists(string? signature, HttpContext httpContext)
    {
        if (signature != null) return false;
        Console.WriteLine("Missing signature");
        httpContext.Response.StatusCode = 400;
        return true;
    }

    public static bool SignatureFormatCheck(string? signature, HttpContext httpContext, out string[] strings)
    {
        strings = signature!.Split('=');
        if (strings.Length == 2 && strings[0] == "sha1") return false;
        Console.WriteLine("Invalid signature format");
        httpContext.Response.StatusCode = 400;
        return true;
    }
}