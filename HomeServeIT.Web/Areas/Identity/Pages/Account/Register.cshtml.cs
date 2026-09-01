// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using HomeServeIT.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using HomeServeIT.Web.Data;

namespace HomeServeIT.Web.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly ApplicationDbContext _context;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _context = context;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string ReturnUrl { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; }

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 8)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; }

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }

            [Required]
            [Display(Name = "Full Name")]
            [StringLength(100)]
            public string FullName { get; set; }

            [Display(Name = "Phone Number")]
            [Required(ErrorMessage = "Phone number is required.")]
            [StringLength(13, MinimumLength = 13, ErrorMessage = "Phone number must be exactly 13 characters.")]
            [RegularExpression(@"^\+63\d{10}$", ErrorMessage = "Phone number must start with +63 followed by exactly 10 digits (e.g. +639171234567).")]
            public string PhoneNumber { get; set; }

            [Display(Name = "Street Address")]
            [Required(ErrorMessage = "Street address is required.")]
            [StringLength(200)]
            public string StreetAddress { get; set; }

            [Display(Name = "Barangay and City")]
            [Required(ErrorMessage = "Barangay and city are required.")]
            [StringLength(100)]
            public string BarangayCity { get; set; }

            [Display(Name = "Landmark")]
            [StringLength(100)]
            public string Landmark { get; set; }

            public bool PrefApptReminders { get; set; } = true;
            public bool PrefQuotations { get; set; } = true;
            public bool PrefInvoices { get; set; } = true;

            // Fake verification code for UI
            public string VerificationCode { get; set; }
        }


        public async Task OnGetAsync(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            if (ModelState.IsValid)
            {
                var user = CreateUser();
                
                user.FullName = Input.FullName;
                user.PhoneNumber = Input.PhoneNumber;
                user.StreetAddress = Input.StreetAddress;
                user.BarangayCity = Input.BarangayCity;
                user.Landmark = Input.Landmark;
                user.PrefApptReminders = Input.PrefApptReminders;
                user.PrefQuotations = Input.PrefQuotations;
                user.PrefInvoices = Input.PrefInvoices;

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    // Automatically assign the Customer role to all users who sign up through the public form
                    await _userManager.AddToRoleAsync(user, HomeServeIT.Web.Constants.Roles.Customer);

                    var userId = await _userManager.GetUserIdAsync(user);

                    // Auto-create CRM profile for this new customer
                    var nameParts = Input.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                    var firstName = nameParts.Length > 0 ? nameParts[0] : "Unknown";
                    var lastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : "Unknown";

                    var addressParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(Input.StreetAddress)) addressParts.Add(Input.StreetAddress);
                    if (!string.IsNullOrWhiteSpace(Input.BarangayCity)) addressParts.Add(Input.BarangayCity);
                    if (!string.IsNullOrWhiteSpace(Input.Landmark)) addressParts.Add($"({Input.Landmark})");
                    var homeAddress = string.Join(", ", addressParts);
                    if (string.IsNullOrWhiteSpace(homeAddress)) homeAddress = "Unknown";

                    var customer = new HomeServeIT.Web.Models.Customer
                    {
                        UserID = userId,
                        FirstName = firstName,
                        LastName = lastName,
                        PhoneNumber = Input.PhoneNumber ?? string.Empty,
                        HomeAddress = homeAddress
                    };
                    _context.Customers.Add(customer);
                    await _context.SaveChangesAsync();

                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId = userId, code = code, returnUrl = returnUrl },
                        protocol: Request.Scheme);

                    await _emailSender.SendEmailAsync(Input.Email, "Confirm your email",
                        $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

                    if (_userManager.Options.SignIn.RequireConfirmedAccount)
                    {
                        return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = returnUrl });
                    }
                    else
                    {
                        await _signInManager.SignInAsync(user, isPersistent: false);
                        
                        if (returnUrl == "~/" || returnUrl == "/")
                        {
                            return LocalRedirect("~/Customer/Dashboard");
                        }
                        
                        return LocalRedirect(returnUrl);
                    }
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }

        private ApplicationUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<ApplicationUser>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(ApplicationUser)}'. " +
                    $"Ensure that '{nameof(ApplicationUser)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                    $"override the register page in /Areas/Identity/Pages/Account/Register.cshtml");
            }
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<ApplicationUser>)_userStore;
        }
    }
}
