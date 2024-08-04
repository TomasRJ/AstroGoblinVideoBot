using System.Security.Cryptography;
using System.Text;
using AstroGoblinVideoBot.Model;

namespace AstroGoblinVideoBot;

public abstract class YoutubeSubscriber
{
    private static readonly Config Config = new ConfigurationBuilder().AddJsonFile("config.json", optional:false).Build().Get<Config>();
    private static readonly Credentials UserSecret = new ConfigurationBuilder().AddUserSecrets<YoutubeSubscriber>(optional:false).Build().Get<Credentials>();
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
        var hashString = Convert.ToHexString(hashBytes);
        return hashString.Equals(signature.ToUpper());
    }
    
    public static bool SignatureExists(string? signature, HttpContext httpContext)
    {
        if (signature != null) return true;
        Console.WriteLine("Missing signature");
        httpContext.Response.StatusCode = 400;
        return false;
    }

    public static bool SignatureFormatCheck(string? signature, HttpContext httpContext, out string[] strings)
    {
        strings = signature!.Split('=');
        if (strings is ["sha1", _]) return false;
        Console.WriteLine("Invalid signature format");
        httpContext.Response.StatusCode = 400;
        return true;
    }
}