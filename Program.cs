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





app.MapGet("/", () => "Hello World!");

await app.RunAsync();