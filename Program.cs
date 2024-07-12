using System.Net;
using System.Net.Http.Headers;
using System.Text;
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

using (HttpClient client = new())
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(config.UserAgent);
    var oathToken = await GetOauthToken(client);
    await SubmitVideo(client, oathToken);
}

async Task<OauthToken> GetOauthToken(HttpClient httpClient)
{
    var basicAuth = Encoding.UTF8.GetBytes($"{userSecret.RedditClientId}:{userSecret.RedditSecret}");
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(basicAuth));
    var content = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        {"grant_type", "client_credentials"}
    });
    
    var authResponse = await httpClient.PostAsync(config.AccessTokenUrl, content);

    if (authResponse.StatusCode == HttpStatusCode.OK)
        return await authResponse.Content.ReadFromJsonAsync<OauthToken>();
    
    Console.WriteLine("Failed to authenticate with Reddit");
    var responseContent = await authResponse.Content.ReadAsStringAsync();
    Console.WriteLine(responseContent);
    throw new HttpRequestException("Failed to authenticate with Reddit");
}

async Task SubmitVideo(HttpClient httpClient, OauthToken oauthToken)
{
    throw new NotImplementedException();
}


app.MapGet("/", () => "Hello World!");

await app.RunAsync();