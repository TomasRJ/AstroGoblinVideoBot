using System.Data.SQLite;
using AstroGoblinVideoBot.Model;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AstroGoblinVideoBot.Frontend;

public class Authorize : PageModel
{
    [BindProperty]
    public RedditAuthorizeForm? AuthorizeForm { get; set; }
    [BindProperty]
    public string? Password { get; set; }
    public bool IsAuthorized { get; private set; }
    private readonly SQLiteConnection _sqLiteConnection = new($"Data Source=reddit.sqlite;Version=3;");
    
    public void OnGet()
    {
        if (!ModelState.IsValid || AuthorizeForm is null)
            IsAuthorized = false;
    }
    
    public IActionResult OnPostPassword()
    {
        var credentials = new ConfigurationBuilder().AddUserSecrets<Index>(optional:false).Build().Get<Credentials>();
        if (Password == credentials.FormCredentials)
        {
            IsAuthorized = true;
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Invalid password.");
        }
        return Page();
    }
    
    public async Task<IActionResult> OnPostRedditFormAsync()
    {
        if (!ModelState.IsValid || AuthorizeForm is null)
        {
            return Page();
        }

        const string insertQuery = "INSERT OR REPLACE INTO FormAuth (Id, Value) VALUES ('StateString', @StateString)";
        await _sqLiteConnection.ExecuteAsync(insertQuery, new { AuthorizeForm.StateString });
        
        var redirectUrl = $"https://www.reddit.com/api/v1/authorize" +
                          $"?client_id={AuthorizeForm.RedditClientId}" +
                          $"&response_type={AuthorizeForm.RedditResponseType}" +
                          $"&state={AuthorizeForm.StateString}" +
                          $"&redirect_uri={AuthorizeForm.RedirectUrl}" +
                          $"&duration={AuthorizeForm.Duration}" +
                          $"&scope={AuthorizeForm.Scope}";
        
        return Redirect(redirectUrl);
    }
}