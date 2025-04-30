// Arquivo: Pages/Login.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using GtopPdqNet.Services; // Ou o namespace correto do seu AuthService

namespace GtopPdqNet.Pages
{
    // >>> ESTE ARQUIVO DEVE CONTER APENAS LoginModel <<<
    public class LoginModel : PageModel
    {
        private readonly AuthService _authService;
        private readonly ILogger<LoginModel> _logger;

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();

        public string? ReturnUrl { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "O nome de usuário é obrigatório.")]
            [Display(Name = "Usuário")]
            public string Username { get; set; } = string.Empty;

            [Required(ErrorMessage = "A senha é obrigatória.")]
            [DataType(DataType.Password)]
            [Display(Name = "Senha")]
            public string Password { get; set; } = string.Empty;
        }

        public LoginModel(AuthService authService, ILogger<LoginModel> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        public void OnGet(string? returnUrl = null)
        {
            ReturnUrl = returnUrl ?? Url.Content("~/");
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            ReturnUrl = returnUrl ?? Url.Content("~/");

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Login POST: ModelState inválido.");
                return Page();
            }

            _logger.LogInformation("Login POST: Tentando autenticar usuário: {Username}", Input.Username);

            // Chama o AuthService (assumindo que ele usa S.DS.P e verifica grupo)
            var (isAuthenticated, userPrincipal, userGroups, authErrorMessage) = await _authService.AuthenticateUser(Input.Username, Input.Password);

            if (isAuthenticated && userPrincipal != null)
            {
                _logger.LogInformation("Login POST: Autenticado: {Username}, Principal: {UserPrincipal}", Input.Username, userPrincipal);

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, userPrincipal),
                    new Claim(ClaimTypes.Name, userPrincipal),
                };
                if (userGroups != null)
                {
                    _logger.LogInformation("Login POST: Adicionando {GroupCount} claims de role...", userGroups.Count);
                    foreach (var group in userGroups) {
                        claims.Add(new Claim(ClaimTypes.Role, group));
                        _logger.LogDebug(" - Role Claim Adicionada: {RoleName}", group);
                    }
                }

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

                var authProperties = new AuthenticationProperties { IsPersistent = true };

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal, authProperties);

                _logger.LogInformation("Login POST: Cookie criado para {Username}. Redirecionando para {ReturnUrl}", Input.Username, ReturnUrl);
                return LocalRedirect(ReturnUrl);
            }
            else
            {
                _logger.LogWarning("Login POST: Falha na autenticação/autorização para {Username}. Motivo: {Reason}", Input.Username, authErrorMessage ?? "Não especificado");
                ModelState.AddModelError(string.Empty, authErrorMessage ?? "Tentativa de login inválida ou sem permissão.");
                ErrorMessage = authErrorMessage ?? "Tentativa de login inválida ou sem permissão.";
                return Page();
            }
        }
    } // Fim LoginModel
} // Fim namespace