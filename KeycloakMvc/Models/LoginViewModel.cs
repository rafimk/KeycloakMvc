using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace KeycloakMvc.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "Enter your username.")]
    [StringLength(256)]
    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [StringLength(256)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select a domain.")]
    [Display(Name = "Domain")]
    public string Domain { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }

    [BindNever, ValidateNever]
    public IReadOnlyList<SelectListItem> Domains { get; set; } = [];
}
