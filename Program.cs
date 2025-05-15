using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets; 
using System.Text; 
using System.Threading.Tasks; 
using Microsoft.AspNetCore.Builder;
using System.DirectoryServices.Protocols;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
public class LdapSettings
{
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; } // Controla LDAPS ou StartTLS implícito
    public string SearchBase { get; set; } = string.Empty;
    public string SearchFilter { get; set; } = string.Empty;
    public string AllowedGroupCn { get; set; } = string.Empty;
}

// Serviço de Autenticação LDAP
public class AuthService
{
    private readonly ILogger<AuthService> _logger;
    private readonly LdapSettings _ldapSettings;

    public AuthService(ILogger<AuthService> logger, IOptions<LdapSettings> ldapSettings)
    {
        _logger = logger;
        _ldapSettings = ldapSettings.Value ?? throw new ArgumentNullException(nameof(ldapSettings), "Configurações LDAP (LdapSettings) não podem ser nulas.");
    }

    public async Task<(bool IsAuthenticated, string? UserPrincipalName, List<string>? UserGroups, string? ErrorMessage)> AuthenticateUser(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Tentativa de autenticação com usuário ou senha em branco.");
            return (false, null, null, "Usuário ou senha não podem estar em branco.");
        }
        return await Task.Run(() => AuthenticateWithSDSP(username, password));
    }

    private (bool IsAuthenticated, string? UserPrincipalName, List<string>? UserGroups, string? ErrorMessage) AuthenticateWithSDSP(string username, string password)
    {
        _logger.LogInformation($"--- Iniciando Autenticação S.DS.P com Validação de Certificado ---");
        _logger.LogInformation($"Servidor: {_ldapSettings.Server}:{_ldapSettings.Port}, UseSsl: {_ldapSettings.UseSsl}");

        try
        {
            var ldapIdentifier = new LdapDirectoryIdentifier(_ldapSettings.Server, _ldapSettings.Port);
            using var connection = new LdapConnection(ldapIdentifier);

            connection.SessionOptions.ProtocolVersion = 3;
            connection.AuthType = AuthType.Basic;

            if (_ldapSettings.UseSsl)
            {
                connection.SessionOptions.SecureSocketLayer = true;
            }

            var credential = new NetworkCredential(username, password);
            connection.Bind(credential);

            _logger.LogInformation("Autenticação bem-sucedida.");
            return (true, username, new List<string>(), null);
        }
        catch (LdapException ldapEx)
        {
            string detailedErrorMessage = ldapEx.ServerErrorMessage ?? ldapEx.Message;
            return (false, null, null, $"Erro LDAP: {detailedErrorMessage}");
        }
        catch (Exception ex)
        {
            return (false, null, null, $"Erro Geral: {ex.Message}");
        }
        finally
        {
            _logger.LogInformation("--- FINALIZANDO Autenticação S.DS.P ---");
        }
    }
}

// Configuração da Aplicação e Pipeline
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configuração de Serviços
        builder.Services.AddRazorPages();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Login";
                options.LogoutPath = "/Logout";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                options.SlidingExpiration = true;
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });
        builder.Services.AddAuthorization();
        builder.Services.Configure<LdapSettings>(builder.Configuration.GetSection("LdapSettings"));
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<GtopPdqNet.Interfaces.IPowerShellService, GtopPdqNet.Services.PowerShellService>();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();
        builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo("C:\\projetopdq\\keys")) 
        .SetApplicationName("PdqWebApp"); // Ou outro nome único para sua app

        var app = builder.Build();

        // Configuração do Pipeline
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }
        else
        {
            app.UseDeveloperExceptionPage();
        }
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRazorPages();

        app.Run();
    }
}