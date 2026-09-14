using System.ComponentModel.DataAnnotations;
using PropertyManagement.Domain.Enums;

namespace PropertyManagement.Web.ViewModels.Account;

public class LoginViewModel
{
    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

/// <summary>
/// Sign-up lets the user say which role they are, as the assessment asks. In a real system this
/// would be an invitation or an administrator action rather than self-selection.
/// </summary>
public class RegisterViewModel
{
    [Required]
    [Display(Name = "First name")]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Last name")]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Phone]
    [Display(Name = "Phone")]
    [StringLength(30)]
    public string? PhoneNumber { get; set; }

    [Required]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required]
    [Display(Name = "I am signing up as")]
    public string Role { get; set; } = UserRole.Applicant;

    public string? ReturnUrl { get; set; }

    public static IReadOnlyList<(string Value, string Label)> RoleChoices =>
    [
        (UserRole.Applicant, "An applicant looking for a unit"),
        (UserRole.PropertyManager, "A property manager")
    ];
}
