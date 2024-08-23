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
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid || AuthorizeForm is null)
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