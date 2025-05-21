using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace GtopPdqNet.Pages
{
    [Authorize]
    public class NetworkReferenceModel : PageModel
    {
        private readonly ILogger<NetworkReferenceModel> _logger;

        public NetworkReferenceModel(ILogger<NetworkReferenceModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogInformation("NetworkReferenceModel.OnGet: Página de referência de rede acessada");
        }
    }
}
