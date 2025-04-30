// Arquivo: Services/PowerShellService.cs
using GtopPdqNet.Interfaces; // Para usar a interface
using Microsoft.AspNetCore.Hosting; // Para IWebHostEnvironment
using Microsoft.Extensions.Configuration; // Para IConfiguration
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; // Para Collection<PSObject>
using System.IO;                   // Para Path, File
using System.Management.Automation; // Para PowerShell
using System.Management.Automation.Runspaces; // Para Runspace, InitialSessionState
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics; // Para Process e ProcessStartInfo

namespace GtopPdqNet.Services // Namespace correto
{
    public class PowerShellService : IPowerShellService
    {
        private readonly ILogger<PowerShellService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _contentRootPath;
        private readonly string? _pdqDeployExePath;
        private readonly string? _getPackagesScriptFullPath;
        private readonly string? _deployScriptFullPath;

        public PowerShellService(ILogger<PowerShellService> logger, IConfiguration configuration, IWebHostEnvironment environment)
        {
            _logger = logger;
            _configuration = configuration;
            _contentRootPath = environment.ContentRootPath;

            _logger.LogInformation("Content Root Path: {ContentRootPath}", _contentRootPath);

            _pdqDeployExePath = GetAndValidatePath("PdqSettings:DeployExePath", false);
            _getPackagesScriptFullPath = GetAndValidatePath("PdqSettings:GetPackagesScriptPath", true);
            _deployScriptFullPath = GetAndValidatePath("PdqSettings:DeployScriptPath", true);
        }

        // Helper para pegar o caminho do appsettings e validar
        private string? GetAndValidatePath(string configKey, bool isRelative)
        {
            string? configuredPath = _configuration[configKey];
            if (string.IsNullOrEmpty(configuredPath)) {
                _logger.LogError("Configuração não encontrada: {ConfigKey}", configKey);
                return null;
            }
            string fullPath = isRelative ? Path.Combine(_contentRootPath, configuredPath) : configuredPath;
            if (!File.Exists(fullPath)) {
                _logger.LogError("Arquivo não encontrado para {ConfigKey}: {FullPath}", configKey, fullPath);
                return null;
            }
             _logger.LogInformation("Caminho validado para {ConfigKey}: {FullPath}", configKey, fullPath);
             return fullPath;
         }

        // --- Chamar o script get_packages.ps1 ---
        public async Task<List<string>> GetPdqPackagesAsync()
        {
            _logger.LogInformation("Tentando buscar pacotes PDQ via script PowerShell...");
            var packages = new List<string>();

            if (string.IsNullOrEmpty(_getPackagesScriptFullPath) || string.IsNullOrEmpty(_pdqDeployExePath)) {
                 _logger.LogError("Não é possível buscar pacotes: Caminho inválido/não configurado para Script ou PDQDeploy.exe.");
                return packages;
            }

             try {
                 using (PowerShell ps = PowerShell.Create(InitialSessionState.CreateDefault()))
                 {
                     _logger.LogInformation("Executando script: {ScriptPath}", _getPackagesScriptFullPath);
                     ps.AddCommand(_getPackagesScriptFullPath)
                         .AddParameter("PDQExePath", _pdqDeployExePath);

                     ps.Streams.Verbose.DataAdded += (sender, e) => _logger.LogDebug("PS Verbose: {Message}", ps.Streams.Verbose[e.Index].ToString());
                     ps.Streams.Warning.DataAdded += (sender, e) => _logger.LogWarning("PS Warning: {Message}", ps.Streams.Warning[e.Index].ToString());
                     ps.Streams.Error.DataAdded += (sender, e) => _logger.LogError("PS Error: {ErrorRecord}", ps.Streams.Error[e.Index].ToString());

                     _logger.LogInformation("Invocando script PowerShell de forma assíncrona...");
                     Collection<PSObject> results = await Task.Run(() => ps.Invoke());
                     _logger.LogInformation("Execução do script PowerShell (GetPackages) concluída.");

                     if (ps.HadErrors && ps.Streams.Error.Count > 0) {
                         _logger.LogError("O script PowerShell GetPackages relatou erros graves (ver logs PS Error acima).");
                         // Não processa resultados se houve erro grave no script
                     }
                     else {
                        // --- NOVA LÓGICA DE PROCESSAMENTO DA SAÍDA ---
                        _logger.LogInformation("Processando a saída COMPLETA recebida do script...");
                        StringBuilder rawOutput = new StringBuilder();
                        foreach (PSObject result in results) {
                            rawOutput.AppendLine(result?.ToString() ?? string.Empty);
                        }
                        string fullOutputString = rawOutput.ToString();
                        _logger.LogDebug("--- Saída Bruta Combinada do Script (GetPackages) ---:\n{RawOutput}", fullOutputString.Trim());

                        // Divide a string completa por novas linhas (incluindo \n e \r\n), remove vazias
                        string[] lines = fullOutputString.Split(new[] { Environment.NewLine, "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);

                         _logger.LogInformation("Saída bruta dividida em {LineCount} linhas (após split e remoção de vazias). Adicionando à lista...", lines.Length);

                         foreach (string line in lines) {
                             string packageName = line.Trim();
                             if (!string.IsNullOrEmpty(packageName)) {
                                 packages.Add(packageName);
                                 _logger.LogDebug("  - Pacote da linha processada adicionado: {PackageName}", packageName);
                             }
                         }
                         _logger.LogInformation("Total final de {Count} pacotes adicionados após processamento no C#.", packages.Count);
                         // --- FIM DA NOVA LÓGICA ---
                     }
                 }
             }
             catch (Exception ex) {
                 _logger.LogError(ex, "Exceção do C# ao invocar o script PowerShell GetPackages.");
             }
             return packages;
         }

        // --- Chamar o script deploy.ps1 ---
        public async Task<(bool success, string output)> ExecutePdqDeployAsync(string hostname, string packageName)
         {
             _logger.LogInformation("Tentando executar deploy via script PS para Host: {Hostname}, Pacote: {PackageName}", hostname, packageName);

             if (string.IsNullOrEmpty(_deployScriptFullPath) || string.IsNullOrEmpty(_pdqDeployExePath)) {
                 _logger.LogError("Não é possível executar deploy: Caminho inválido/não configurado para Script ou PDQDeploy.exe.");
                 return (false, "Erro interno: Configuração inválida.");
             }

             try {
                   using (PowerShell ps = PowerShell.Create(InitialSessionState.CreateDefault())) {
                       _logger.LogInformation("Executando script: {ScriptPath}", _deployScriptFullPath);
                       ps.AddCommand(_deployScriptFullPath)
                           .AddParameter("PDQExePath", _pdqDeployExePath)
                           .AddParameter("hostname", hostname)
                           .AddParameter("package", packageName);

                        ps.Streams.Verbose.DataAdded += (sender, e) => _logger.LogDebug("Deploy PS Verbose: {Message}", ps.Streams.Verbose[e.Index].ToString());
                        ps.Streams.Warning.DataAdded += (sender, e) => _logger.LogWarning("Deploy PS Warning: {Message}", ps.Streams.Warning[e.Index].ToString());
                        ps.Streams.Error.DataAdded += (sender, e) => _logger.LogError("Deploy PS Error: {ErrorRecord}", ps.Streams.Error[e.Index].ToString());

                       _logger.LogInformation("Invocando script de Deploy PowerShell...");
                       Collection<PSObject> results = await Task.Run(() => ps.Invoke());
                       _logger.LogInformation("Execução do script de Deploy concluída.");

                       StringBuilder combinedOutput = new StringBuilder();
                       foreach(var result in results) {
                           combinedOutput.AppendLine(result?.ToString() ?? string.Empty);
                       }
                       string finalOutput = combinedOutput.ToString().Trim(); // Trim no final

                        if (ps.HadErrors && ps.Streams.Error.Count > 0) {
                             _logger.LogError("O script PowerShell Deploy relatou erros graves.");
                            return (false, $"Falha crítica no script PowerShell.\n\nLog Completo:\n{finalOutput}");
                        } else {
                             _logger.LogInformation("Script PowerShell de deploy executado. Saída:\n{Output}", finalOutput);
                             // Confia no script PS para indicar sucesso (exit code 0) via ausência de erro.
                             return (true, finalOutput);
                        }
                   }
              }
              catch (Exception ex) {
                   _logger.LogError(ex, "Exceção do C# ao invocar o script PowerShell Deploy para {Hostname}/{PackageName}", hostname, packageName);
                   return (false, $"Erro interno do C#: {ex.Message}");
              }
          }

    } // Fim da classe PowerShellService
} // Fim do namespace GtopPdqNet.Services