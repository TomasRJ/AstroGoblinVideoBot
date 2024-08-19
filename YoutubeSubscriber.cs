using System.Security.Cryptography;
using System.Text;
using AstroGoblinVideoBot.Model;

namespace AstroGoblinVideoBot;

public class YoutubeSubscriber(Credentials userSecret, Config config, ILogger logger)
{
    private readonly HttpClient _youtubeHttpClient = new();
    
    public async Task<bool> SubscribeToChannel()
    {
        logger.LogInformation("Subscribing to Youtube channel: {Channel}", config.GooglePubSubTopic);
        var subscribeForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "hub.callback", userSecret.YoutubeCallbackUrl },
            { "hub.mode", "subscribe" },
            { "hub.topic", config.GooglePubSubTopic },
            { "hub.secret", userSecret.HmacSecret }
        });
        
        var subscribeResponse = await _youtubeHttpClient.PostAsync(config.GooglePubSubUrl, subscribeForm);
        if (subscribeResponse.IsSuccessStatusCode)
        {
            logger.LogInformation("Successfully sent subscription request Google PubSubHubbub, now waiting for verification");
            return true;
        }
        
        var responseContent = await subscribeResponse.Content.ReadAsStringAsync();
        logger.LogError("Failed to subscribe to Youtube channel, got following error: {Content}",  responseContent);

        return false;
    }

    public bool VerifySignature(byte[] payload, string secret, string signature)
    {
        var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
        var hashBytes = hmac.ComputeHash(payload);
        var hashString = Convert.ToHexString(hashBytes);
        if (hashString.Equals(signature.ToUpper()))
        {
           logger.LogInformation("The Google PubSubHubbub post request HMAC signature verified");
           return true;
        }
        logger.LogError("The Google PubSubHubbub post request HMAC signature verification failed, expected {Expected} but got {Actual}", hashString, signature.ToUpper());
        return false;
    }
    
    public bool SignatureExists(string? signature, HttpContext httpContext)
    {
        if (signature != null)
        {
            logger.LogInformation("The X-Hub-Signature exists");
            return true;
        }
        logger.LogError("Signature not found");
        httpContext.Response.StatusCode = 400;
        return false;
    }

    public bool SignatureFormatCheck(string? signature, HttpContext httpContext, out string[] strings)
    {
        strings = signature!.Split('=');
        if (strings is ["sha1", _])
        {
            logger.LogInformation("X-Hub-Signature format is correct");
            return true;
        }
        logger.LogError("Invalid X-Hub-Signature format, expected 'sha1=hash', got {Signature}", signature);
        httpContext.Response.StatusCode = 400;
        return false;
    }
}