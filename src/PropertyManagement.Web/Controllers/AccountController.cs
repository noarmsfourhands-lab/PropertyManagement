using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Infrastructure.Identity;
using PropertyManagement.Web.ViewModels.Account;

namespace PropertyManagement.Web.Controllers;

/// <summary>
/// Sign-up, sign-in and sign-out. Hand-written rather than scaffolded from Identity UI because
/// sign-up has to let the user choose a role, and a Razor Pages area would sit oddly in an
/// otherwise MVC application.
/// </summary>
public class AccountController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<AccountController> logger) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(
            model.Email,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account is locked. Try again later.");
            return View(model);
        }

        if (!result.Succeeded)
        {
            // Deliberately vague: saying which half was wrong tells an attacker which emails exist.
            ModelState.AddModelError(string.Empty, "That email and password do not match.");
            return View(model);
        }

        logger.LogInformation("{Email} signed in.", model.Email);
        return RedirectToLocal(model.ReturnUrl);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new RegisterViewModel
        {
            ReturnUrl = returnUrl,
            PasswordRule = PasswordRule()
        });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        // The role arrives from a form field, so it is checked against the known roles rather
        // than trusted. Without this, a crafted post could claim any role string.
        if (!UserRole.All.Contains(model.Role, StringComparer.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Role), "Choose one of the available roles.");
        }

        model.PasswordRule = PasswordRule();

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true,
            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim(),
            PhoneNumber = model.PhoneNumber
        };

        var created = await userManager.CreateAsync(user, model.Password);

        if (!created.Succeeded)
        {
            ReportSignUpFailure(created);
            return View(model);
        }

        var assigned = await userManager.AddToRoleAsync(user, model.Role);

        if (!assigned.Succeeded)
        {
            // The account exists but has no role, so it would be refused on every page it opened.
            // Undo it rather than leave someone signed in to nothing.
            logger.LogError(
                "Created {Email} but could not put them in {Role}: {Errors}",
                model.Email,
                model.Role,
                string.Join("; ", assigned.Errors.Select(error => error.Description)));

            await userManager.DeleteAsync(user);

            ModelState.AddModelError(string.Empty, "That account could not be set up. Try again.");
            return View(model);
        }

        await signInManager.SignInAsync(user, isPersistent: false);

        logger.LogInformation("{Email} signed up as {Role}.", model.Email, model.Role);
        return RedirectToLocal(model.ReturnUrl);
    }

    /// <summary>
    /// Asks before signing out, for anyone who reaches this address directly.
    ///
    /// Signing out has to be a POST: a GET that ends the session can be fired by a link in an
    /// email, an image tag on another site, or a browser prefetching what it thinks is a page.
    /// That leaves the address itself with nothing to answer, which is a "method not allowed" for
    /// a URL people can perfectly reasonably type or bookmark. So the GET is a real page that asks,
    /// and the button on it posts.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Logout()
    {
        // Already signed out: there is nothing to confirm, so say so rather than offering a button
        // that would do nothing.
        return User.Identity?.IsAuthenticated == true
            ? View()
            : RedirectToAction(nameof(Login));
    }

    [HttpPost]
    [ActionName(nameof(Logout))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogoutConfirmed()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    /// <summary>
    /// The password rule as a sentence, read from the configuration that enforces it so the two
    /// cannot drift apart.
    /// </summary>
    private string PasswordRule()
    {
        var rules = userManager.Options.Password;
        var needs = new List<string>();

        if (rules.RequireUppercase)
        {
            needs.Add("an uppercase letter");
        }

        if (rules.RequireLowercase)
        {
            needs.Add("a lowercase letter");
        }

        if (rules.RequireDigit)
        {
            needs.Add("a number");
        }

        if (rules.RequireNonAlphanumeric)
        {
            needs.Add("a symbol");
        }

        var length = $"Use at least {rules.RequiredLength} characters";

        return needs.Count == 0
            ? $"{length}."
            : $"{length}, including {string.Join(", ", needs[..^1])}"
              + (needs.Count > 1 ? " and " : " ")
              + $"{needs[^1]}.";
    }

    /// <summary>
    /// Puts an Identity failure on the form in the words of this application.
    ///
    /// Identity reports what its own model thinks, which is not what the person filled in. The
    /// account's username is their email address here, so a duplicate address comes back twice, as
    /// a taken username and a taken email: one fact, stated twice, half of it about a field this
    /// form does not have. Password rules come back as one error per rule for the same box.
    ///
    /// So each error is placed on the field it belongs to and the duplicates collapse.
    /// </summary>
    private void ReportSignUpFailure(IdentityResult result)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var error in result.Errors)
        {
            var (field, message) = error.Code switch
            {
                // Both of these mean the same thing, because the username is the email address.
                "DuplicateUserName" or "DuplicateEmail" => (
                    nameof(RegisterViewModel.Email),
                    "An account already exists for that email address. Sign in instead."),

                "InvalidEmail" or "InvalidUserName" => (
                    nameof(RegisterViewModel.Email),
                    "That does not look like an email address."),

                // Identity raises one of these per unmet rule, and the field renders only the
                // first, so reporting them separately corrects the person once per attempt. The
                // whole rule is one message.
                var code when code.StartsWith("Password", StringComparison.Ordinal) => (
                    nameof(RegisterViewModel.Password),
                    PasswordRule()),

                _ => (string.Empty, error.Description)
            };

            // Identity can report the same conclusion under more than one code, and a field should
            // never be told the same thing twice.
            if (reported.Add($"{field}|{message}"))
            {
                ModelState.AddModelError(field, message);
            }
        }
    }

    /// <summary>
    /// Only ever redirects inside this application. An open redirect would let a crafted link
    /// bounce a freshly signed-in user to somewhere else entirely.
    /// </summary>
    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Home");
}
