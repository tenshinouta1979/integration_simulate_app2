using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration; // Explicitly added for clarity and to ensure recognition
using System; // Explicitly added for DateTimeOffset

namespace app2.Pages
{
    public class IndexModel : PageModel
    {
        private readonly IConfiguration _configuration;
        public string App1Origin { get; set; } = string.Empty;

        public IndexModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void OnGet()
        {
            // Get App1Origin from configuration to pass to client-side JS
            App1Origin = _configuration["App1Origin"] ?? "http://localhost:7001";
        }
    }
}