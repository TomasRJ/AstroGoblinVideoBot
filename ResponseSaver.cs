namespace AstroGoblinVideoBot;

public static class ResponseSaver
{
    public static async Task SaveRedditResponseAsync(string path, HttpResponseMessage response)
    {
        await File.AppendAllTextAsync(path, await response.Content.ReadAsStringAsync());
    }

    public static async Task SavePubSubResponseAsync(string path, MemoryStream response)
    {
        response.Position = 0;
        await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.CopyToAsync(fileStream);
    }
}