using System.Text;
using System.Text.Json;
using AstroGoblinVideoBot.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AstroGoblinVideoBot.Frontend;

public class Index : PageModel
{
    [BindProperty]
    public RedditAuthorizeForm? AuthorizeForm { get; set; }
    [BindProperty]
    public string? Password { get; set; }
    public bool IsAuthorized { get; private set; }
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public void OnGet()
    {
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
        if (!ModelState.IsValid || AuthorizeForm is null || !AuthorizeForm.IsAuthorized)
        {
            return Page();
        }
        var authorizeFormJson = JsonSerializer.Serialize(AuthorizeForm, SerializerOptions);
        await System.IO.File.WriteAllTextAsync("authorizeForm.json", authorizeFormJson, Encoding.UTF8);
        
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