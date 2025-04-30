// Arquivo: Interfaces/IPowerShellService.cs
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GtopPdqNet.Interfaces // Define o namespace
{
    public interface IPowerShellService
    {
        // Método para buscar pacotes PDQ (exemplo)
        Task<List<string>> GetPdqPackagesAsync();

        // Método para executar o deploy (retorna sucesso e output)
        Task<(bool success, string output)> ExecutePdqDeployAsync(string hostname, string packageName);
    }
}