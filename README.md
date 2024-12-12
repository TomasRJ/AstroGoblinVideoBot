# Astrogoblin YouTube Bot
This a C# .NET 8 project I made to have a "YouTube bot" reddit account post new videos from the [Astrogoblin YouTube channel](https://www.youtube.com/@astrogoblinplays) to [the Astrogoblin subreddit](https://reddit.com/r/astrogoblin/).

[YouTube supports push notifications via PubSubHubbub](https://developers.google.com/youtube/v3/guides/push_notifications) (also called WebSub), a server-to-server publish/subscribe protocol, where this project specifically uses the ["Google PubSubHubbub Hub"](https://pubsubhubbub.appspot.com/) to get HTTP POST requests for when new videos are uploaded to the channel and them post them to Reddit via the [Reddit API](https://www.reddit.com/dev/api/).

### Features:
- Moderation of the new posts by unsticking a previous video post and stickying the new post.
- Prevention of posting the same video multiple times by storing the post and video details in a SQLite database.
- The ability get previous reddit posts details by using Reddit's JSON URL endpoint feature. This URL is set in the "UserPostsInfo" in the **config.json** file.
- A front-end, where the /authorize endpoint has a HTML form to make a ["Reddit Authorize url"](https://github.com/reddit-archive/reddit/wiki/OAuth2#authorization) where the submit button takes you to the Reddit OAuth2 page to authorize the bot account to submit and moderate posts.
- HMAC signature verification for the PubSubHubbub Hub to verify the authenticity of the POST requests.
- Usage of post flairs to flair the new posts with a specific flair. Use the [Reddit API](https://old.reddit.com/dev/api/oauth#GET_api_link_flair_v2) to get the flair ids for your subreddit.
- Automatic refreshing of the Reddit OAuth2 access token when it expires.
- Automatic refreshing of the PubSubHubbub Hub subscription when it expires.

### Project endpoints:
- **/** - The Frontend/Index.cshtml page.
- **/authorize** - The Frontend/Authorize.cshtml page, used to make the reddit authorize url with an HTML form. Has a "state string" that used in tandem with the /redditRedirect endpoint to verify the authenticity of the Reddit OAuth2 authorization.
- **/redditRedirect** - A GET endpoint that takes the Reddit OAuth2 authorization redirect back to the project and with the code gets the Reddit OAuth2 access token and with the state string verifies the authenticity of the Reddit OAuth2 redirect.
- **/youtube** - A GET endpoint that is used by the PubSubHubbub Hub to verify the subscription.
- **/youtube** - A POST endpoint that is used by the PubSubHubbub Hub to send the new video details to.

### Using the project:
If you want to use this project, you will need:
- Either the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or [Docker](https://www.docker.com/products/docker-desktop/) to run the project.
- To create a Reddit account and create a Reddit app to get the RedditClientId and RedditSecret. I would recommend using the [Postman Reddit API collection](https://www.postman.com/lovingmydemons/workspace/reddit-api/collection/30347094-3ab37a1f-dd25-4f23-92a4-9142dfd77ffa?action=share&creator=32597187) and read the Reddit API documentation [on GitHub](https://github.com/reddit-archive/reddit/wiki/OAuth2) and on [Reddit](https://www.reddit.com/dev/api/oauth) how the Reddit API authorization and authentication works.
- The YouTube channel ID for the GooglePubSubTopic value in the **config.json** file. You can get the channel ID by going to this website: https://www.streamweasels.com/tools/youtube-channel-id-and-user-id-convertor/
- To edit the **config.json** file with your own UserAgent, UserPostsInfo url and Subreddit values.
- To create a [.NET User Secret](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-9.0&tabs=windows#enable-secret-storage) using the following JSON format and replace the values with your own:

    ```json
    {
      "RedditClientId": "RedditClientId",
      "RedditRedirectUrl": "RedditRedirectUrl/redditRedirect",
      "RedditSecret": "RedditSecret",
      "SubmitFlairId": "SubmitFlairId",
      "YoutubeCallbackUrl": "YoutubeCallbackUrl/youtube",
      "HmacSecret": "HmacSecret"
    }
    ```

#### Note for running the project locally:
If you want to run this project locally, like for example using Docker, you will need to use services like [localhost.run (uses SSH)](https://localhost.run/) or [localtunnel (uses npm)](https://theboroer.github.io/localtunnel-www/) to expose the local server to the internet so the PubSubHubbub Hub subscription can get verified and send POST requests to your local server.

### Example of building and running the project with Docker:
In the project directory run the following commands:
```bash
docker build -t astrogoblin-youtube-bot .
```

```bash
docker run -d -p 8080:8080 astrogoblin-youtube-bot
```
Go to http://localhost:8080/ to see the Frontend/Index.cshtml page.

### Uses the following NuGet packages:
- [Dapper](https://www.nuget.org/packages/Dapper/)
- [System.Data.SQLite.Core](https://www.nuget.org/packages/System.Data.SQLite.Core/)
- [Microsoft.Extensions.Configuration.UserSecrets](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.UserSecrets/)
- [Microsoft.Extensions.Configuration.Binder](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Binder/)

### Example data

Here is an example the XML data PubSubHubbub sends to server when a new video is uploaded:
```xml
<?xml version='1.0' encoding='UTF-8'?>
<feed xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns="http://www.w3.org/2005/Atom"><link rel="hub" href="https://pubsubhubbub.appspot.com"/><link rel="self" href="https://www.youtube.com/xml/feeds/videos.xml?channel_id=UCRb4V8WHojbGqEvzL_9g03Q"/><title>YouTube video feed</title><updated>2024-08-26T12:00:12.863874898+00:00</updated><entry>
  <id>yt:video:7NyIKlfkznM</id>
  <yt:videoId>7NyIKlfkznM</yt:videoId>
  <yt:channelId>UCRb4V8WHojbGqEvzL_9g03Q</yt:channelId>
  <title>It's... a Ubisoft Game - Star Wars Outlaws PREVIEW #ubisoftpartner #ad</title>
  <link rel="alternate" href="https://www.youtube.com/watch?v=7NyIKlfkznM"/>
  <author>
    <name>astrogoblin</name>
    <uri>https://www.youtube.com/channel/UCRb4V8WHojbGqEvzL_9g03Q</uri>
  </author>
  <published>2024-08-26T12:00:06+00:00</published>
  <updated>2024-08-26T12:00:12.863874898+00:00</updated>
</entry></feed>
```

Here is an example of the format of JSON response after making a successful reddit post:
```json
{"json": {"errors": [], "data": {"url": "https://www.reddit.com/r/astrogoblin/comments/1f50b8d/this_is_a_dumb_game_for_babies/", "drafts_count": 0, "id": "1f50b8d", "name": "t3_1f50b8d"}}}
```