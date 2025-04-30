using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering; // Para SelectList
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq; // Para .Any()
using System;
using GtopPdqNet.Interfaces; // Garanta que IPowerShellService está aqui
using System.Net.NetworkInformation; // Adicionado para Ping
using System.Text;                   // Adicionado para StringBuilder

namespace GtopPdqNet.Pages // Assumindo que seu namespace é este
{
    [Authorize] // Garante que só usuarios logados acessem
    public class IndexModel : PageModel // <<< GARANTA QUE ESTA É A IndexModel >>>
    {
        private readonly IPowerShellService _psService;
        private readonly ILogger<IndexModel> _logger;

        // --- Variáveis BindProperty para o formulário ---
        [BindProperty]
        [Required(ErrorMessage = "O Hostname é obrigatório.")]
        [Display(Name = "Hostname ou IP")]
        public string Hostname { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Selecione um pacote.")]
        [Display(Name = "Pacote PDQ")]
        public string SelectedPackage { get; set; } = string.Empty;

        // Propriedade para popular o dropdown
        public SelectList? PackageOptions { get; set; }

        // --- Construtor ---
        public IndexModel(IPowerShellService psService, ILogger<IndexModel> logger)
        {
            _psService = psService;
            _logger = logger;
        }

        // --- Carregamento Inicial da Página (GET) ---
        public async Task<IActionResult> OnGetAsync()
        {
            _logger.LogInformation("IndexModel.OnGetAsync: Carregando pacotes PDQ...");
            try
            {
                var packages = await _psService.GetPdqPackagesAsync();
                if (packages != null && packages.Any())
                {
                     PackageOptions = new SelectList(packages);
                     _logger.LogInformation("IndexModel.OnGetAsync: Carregados {Count} pacotes.", packages.Count);
                }
                else
                {
                     _logger.LogWarning("IndexModel.OnGetAsync: Nenhum pacote PDQ retornado.");
                     PackageOptions = new SelectList(new List<string>());
                     // ViewData["WarningMessage"] = "Nao foi possivel carregar a lista de pacotes.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IndexModel.OnGetAsync: Erro ao carregar pacotes PDQ.");
                 PackageOptions = new SelectList(new List<string>());
                 ViewData["ErrorMessage"] = "Erro ao carregar pacotes.";
            }
            return Page();
        }

        // --- Handler para Solicitação de Deploy via Fetch/AJAX ---
        public async Task<JsonResult> OnPostDeployAsync(string hostname, string selectedPackage)
        {
            _logger.LogInformation("IndexModel.OnPostDeployAsync: Host={Hostname}, Pacote={Package}", hostname, selectedPackage);

            if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(selectedPackage)) {
                 _logger.LogWarning("IndexModel.OnPostDeployAsync: Parâmetros inválidos.");
                 return new JsonResult(new { success = false, log = "ERRO: Hostname e Pacote são obrigatórios." });
            }

            var logBuilder = new StringBuilder();

            try {
                 _logger.LogInformation("IndexModel.OnPostDeployAsync: Verificando conectividade com: {Hostname}", hostname);
                 logBuilder.AppendLine($"-> Verificando conectividade com '{hostname}'...");
                using (var pingSender = new Ping()) {
                    PingReply reply;
                    try {
                        reply = await pingSender.SendPingAsync(hostname, 5000); // Timeout de 5 segundos
                    } catch (PingException PEx) when (PEx.InnerException is System.Net.Sockets.SocketException sockEx && sockEx.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound) {
                        _logger.LogWarning(PEx, "IndexModel.OnPostDeployAsync: Ping para {Hostname} - Host Não Encontrado (DNS?)", hostname);
                        logBuilder.AppendLine($"FALHA CONEXÃO: Host '{hostname}' não resolvido (erro DNS).");
                        return new JsonResult(new { success = false, log = logBuilder.ToString() });
                    }

                    if (reply.Status == IPStatus.Success) {
                        _logger.LogInformation("IndexModel.OnPostDeployAsync: Ping OK para {Hostname}. IP: {IPAddress}, Tempo: {RoundtripTime}ms", hostname, reply.Address, reply.RoundtripTime);
                        logBuilder.AppendLine($"   SUCESSO: Host '{hostname}' ({reply.Address}) respondeu em {reply.RoundtripTime}ms.");
                        logBuilder.AppendLine("---------------------------------------");
                        logBuilder.AppendLine("-> Iniciando o comando de deploy PDQ...");
                        logBuilder.AppendLine();
                    } else {
                        _logger.LogWarning("IndexModel.OnPostDeployAsync: Ping FALHOU para {Hostname}. Status: {Status}", hostname, reply.Status);
                        logBuilder.AppendLine($"FALHA CONEXÃO: Host '{hostname}' NÃO respondeu ao ping (Status: {reply.Status}). Deploy cancelado.");
                        return new JsonResult(new { success = false, log = logBuilder.ToString() });
                    }
                }

                var (deploySuccess, deployOutput) = await _psService.ExecutePdqDeployAsync(hostname, selectedPackage);
                _logger.LogInformation("IndexModel.OnPostDeployAsync: Resultado do script deploy: Success={Success}", deploySuccess);
                logBuilder.AppendLine("--- Saída do Script PowerShell ---");
                logBuilder.Append(deployOutput ?? "[Nenhuma saída do script]");

                if (!deploySuccess) {
                     _logger.LogWarning("IndexModel.OnPostDeployAsync: Script de deploy retornou falha.");
                     logBuilder.AppendLine("\nFALHA: O processo de deploy não foi concluído com sucesso. Verifique os logs acima.");
                } else {
                    logBuilder.AppendLine("\nSUCESSO: Comando de deploy enviado/concluído com sucesso pelo script.");
                }
                 return new JsonResult(new { success = deploySuccess, log = logBuilder.ToString() });

            } catch (PingException pingEx) {
                _logger.LogError(pingEx, "IndexModel.OnPostDeployAsync: Erro PING inesperado para {Hostname}", hostname);
                logBuilder.AppendLine($"ERRO PING INESPERADO: {pingEx.Message}");
                return new JsonResult(new { success = false, log = logBuilder.ToString() });
            } catch (Exception ex) {
                 _logger.LogError(ex, "IndexModel.OnPostDeployAsync: Erro INESPERADO para {Hostname}/{Package}", hostname, selectedPackage);
                 logBuilder.AppendLine($"\n******************\nERRO INESPERADO NO SERVIDOR:\n{ex.Message}\n******************");
                return new JsonResult(new { success = false, log = logBuilder.ToString() });
            }
        }
    }
}