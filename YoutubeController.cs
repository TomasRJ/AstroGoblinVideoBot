using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using AstroGoblinVideoBot.Model;

namespace AstroGoblinVideoBot;

public class YoutubeController(Credentials userSecret, Config config, ILogger logger, bool responseSaver)
{
    private readonly HttpClient _youtubeHttpClient = new();

    public async Task<bool> HandlePubSubRequest(IQueryCollection queryCollection, HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var topic = queryCollection.ContainsKey("hub.topic")
            ? queryCollection["hub.topic"].ToString()
            : throw new InvalidOperationException();
        var challenge = queryCollection.ContainsKey("hub.challenge")
            ? queryCollection["hub.challenge"].ToString()
            : throw new InvalidOperationException();
        var mode = queryCollection.ContainsKey("hub.mode")
            ? queryCollection["hub.mode"].ToString()
            : throw new InvalidOperationException();
        var leaseSeconds = queryCollection.ContainsKey("hub.lease_seconds")
            ? queryCollection["hub.lease_seconds"].ToString()
            : throw new InvalidOperationException();

        if (!string.IsNullOrEmpty(challenge) && mode.Equals("subscribe") && topic.Equals(config.GooglePubSubTopic))
        {
            logger.LogInformation("Google PubSubHubbub verification request received");
            httpContext.Response.ContentType = "text/plain";
            await httpContext.Response.WriteAsync(challenge, cancellationToken);
            logger.LogInformation(
                "Google PubSubHubbub verification successful, now successfully subscribed to the Youtube channel");
        }

        if (string.IsNullOrEmpty(leaseSeconds)) return false;

        var leaseSecondsInt = int.Parse(leaseSeconds);
        _ = Task.Run(async () => // using "_ =" makes the Task run in the background
        {
            await Task.Delay(leaseSecondsInt * 1000, cancellationToken);
            logger.LogInformation("Resubscribing to Google PubSubHubbub");
            await SubscribeToChannel();
        }, cancellationToken);
        return true;
    }

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
            logger.LogInformation(
                "Successfully sent subscription request Google PubSubHubbub, now waiting for verification");
            return true;
        }

        var responseContent = await subscribeResponse.Content.ReadAsStringAsync();
        logger.LogError("Failed to subscribe to Youtube channel, got following error: {Content}", responseContent);

        return false;
    }

    public async Task<VideoFeed> GetVideoFeed(HttpContext youtubeSubscriptionRequest)
    {
        logger.LogInformation("Google PubSubHubbub subscription request received");
        var requestBody = new MemoryStream();
        await youtubeSubscriptionRequest.Request.Body.CopyToAsync(requestBody);

        if (!SignatureVerification(youtubeSubscriptionRequest, requestBody))
            throw new InvalidOperationException("Invalid signature");
        youtubeSubscriptionRequest.Response.StatusCode = 200;
        
        if (responseSaver)
            await ResponseSaver.SavePubSubResponseAsync("pubSubResponse.xml", requestBody);

        logger.LogDebug("Deserializing the Youtube video feed");
        requestBody.Position = 0;
        var xmlSerializer = new XmlSerializer(typeof(VideoFeed));
        var videoFeed = (VideoFeed)(xmlSerializer.Deserialize(requestBody) ?? throw new InvalidOperationException());

        logger.LogInformation("Successfully deserialized the Youtube video feed");
        return videoFeed;
    }

    private bool SignatureVerification(HttpContext youtubeRequest, MemoryStream requestBody)
    {
        var headers = youtubeRequest.Request.Headers;
        var signatureHeader = headers["X-Hub-Signature"].FirstOrDefault();

        if (!SignatureExists(signatureHeader, youtubeRequest)) return false;

        if (!SignatureFormatCheck(signatureHeader, youtubeRequest, out var signatureParts)) return false;
        var signature = signatureParts[1];

        requestBody.Position = 0;

        if (VerifySignature(requestBody.ToArray(), userSecret.HmacSecret, signature))
            return true;

        youtubeRequest.Response.StatusCode =
            200; // The Google PubSubHubbub protocol requires a 200 response even if the signature is invalid
        return false;
    }

    private bool VerifySignature(byte[] payload, string secret, string signature)
    {
        var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
        var hashBytes = hmac.ComputeHash(payload);
        var hashString = Convert.ToHexString(hashBytes);

        if (hashString.Equals(signature.ToUpper()))
        {
            logger.LogInformation("The Google PubSubHubbub POST request HMAC signature is valid");
            return true;
        }

        logger.LogError(
            "The Google PubSubHubbub POST request HMAC signature verification failed, expected {Expected} but got {Actual}",
            hashString, signature.ToUpper());
        return false;
    }

    private bool SignatureExists(string? signature, HttpContext httpContext)
    {
        if (signature != null)
        {
            logger.LogInformation("The X-Hub-Signature exists");
            return true;
        }

        logger.LogError("The X-Hub-Signature not found");
        httpContext.Response.StatusCode = 400;
        return false;
    }

    private bool SignatureFormatCheck(string? signature, HttpContext httpContext, out string[] strings)
    {
        strings = signature!.Split('=');
        if (strings is ["sha1", _])
        {
            logger.LogInformation("The X-Hub-Signature format is correct");
            return true;
        }

        logger.LogError("Invalid X-Hub-Signature format, expected 'sha1=hash', got {Signature}", signature);
        httpContext.Response.StatusCode = 400;
        return false;
    }
}