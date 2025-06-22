using app2.Models;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json; // For PostAsJsonAsync
using System.Text.Json; // Added to resolve JsonElement issue

namespace app2.Controllers
{
    [ApiController]
    [Route("api/app1-integration")]
    public class App1IntegrationController : ControllerBase
    {
        private readonly ILogger<App1IntegrationController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;


        public App1IntegrationController(ILogger<App1IntegrationController> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        // This endpoint is called by App1's backend to send the OTT to App2's backend.
        [HttpPost("receive-ott")]
        public IActionResult ReceiveOtt([FromBody] ReceiveOttRequest request)
        {
            try // Added try-catch for debugging 500 errors
            {
                _logger.LogInformation("App2 Backend: Received OTT from App1 backend for ReferenceId: '{ReferenceId}'", request?.ReferenceId);

                if (request == null || string.IsNullOrEmpty(request.Ott) || string.IsNullOrEmpty(request.ReferenceId))
                {
                    _logger.LogWarning("App2 Backend: Invalid OTT or ReferenceId received from App1 (request was null or properties missing).");
                    return BadRequest(new { success = false, message = "OTT and ReferenceId are required." });
                }

                // Store the received OTT in app2's in-memory state.
                // In a real scenario, this would be saved to a database, perhaps with a short TTL.
                // We use the ReferenceId as the key, as app1 sends one OTT per ReferenceId.
                if (App2State.ReceivedOneTimeTokens.TryGetValue(request.ReferenceId, out var existingOtt))
                {
                    if (!existingOtt.IsUsed && existingOtt.Expiry > DateTimeOffset.UtcNow)
                    {
                        _logger.LogWarning("App2 Backend: Overwriting existing unused OTT for ReferenceId '{ReferenceId}'.", request.ReferenceId);
                    }
                }

                // OTT received by app2's backend has its own expiry in app2's context
                App2State.ReceivedOneTimeTokens[request.ReferenceId] = new OneTimeTokenData
                {
                    Ott = request.Ott,
                    ReferenceId = request.ReferenceId,
                    Expiry = DateTimeOffset.UtcNow.AddMinutes(5), // App2 gives it 5 mins from its receipt time
                    IsUsed = false,
                    ReceivedTime = DateTimeOffset.UtcNow
                };

                _logger.LogInformation("App2 Backend: Successfully stored OTT for ReferenceId '{ReferenceId}'. Expiry: {Expiry}",
                                       request.ReferenceId, App2State.ReceivedOneTimeTokens[request.ReferenceId].Expiry);

                return Ok(new { success = true, message = "OTT received and stored by App2 backend." });
            }
            catch (Exception ex)
            {
                // Log the full exception details
                _logger.LogError(ex, "App2 Backend: An unexpected error occurred in ReceiveOtt endpoint. Request ReferenceId: '{ReferenceId}'. Error: {Message}",
                                 request?.ReferenceId ?? "N/A", ex.Message);
                return StatusCode(500, new { success = false, message = $"An internal server error occurred while processing OTT: {ex.Message}" });
            }
        }


        // This endpoint is called by App2's client-side JavaScript to establish its session.
        // App2's backend will now handle the OTT lookup and validation with App1.
        [HttpPost("establish-session")]
        public async Task<IActionResult> EstablishSession([FromBody] EstablishSessionRequest request)
        {
            _logger.LogInformation("App2 Backend: Client requested session establishment for ReferenceId: '{ReferenceId}'", request.ReferenceId);

            if (string.IsNullOrEmpty(request.ReferenceId))
            {
                return BadRequest(new { success = false, message = "ReferenceId is required." });
            }

            // 1. Retrieve the OTT from App2's own state using the ReferenceId
            if (!App2State.ReceivedOneTimeTokens.TryGetValue(request.ReferenceId, out var storedOttData))
            {
                _logger.LogWarning("App2 Backend: EstablishSession - No OTT found for ReferenceId '{ReferenceId}'.", request.ReferenceId);
                return NotFound(new { success = false, message = "No valid authentication token found for this session. Please refresh App1." });
            }

            if (storedOttData.IsUsed)
            {
                _logger.LogWarning("App2 Backend: EstablishSession - OTT for ReferenceId '{ReferenceId}' has already been used.", request.ReferenceId);
                return Conflict(new { success = false, message = "Authentication token has already been used for this session." });
            }

            if (storedOttData.Expiry < DateTimeOffset.UtcNow)
            {
                _logger.LogWarning("App2 Backend: EstablishSession - OTT for ReferenceId '{ReferenceId}' has expired. Marking as used.", request.ReferenceId);
                storedOttData.IsUsed = true; // Mark as used even if expired
                return Unauthorized(new { success = false, message = "Authentication token has expired. Please refresh App1." });
            }

            // Mark the OTT as used BEFORE calling App1, to prevent replay.
            storedOttData.IsUsed = true;

            // 2. Call App1's backend to validate the OTT
            var app1BaseUrl = _configuration["App1Origin"] ?? "http://localhost:7001";
            var app1ApiUrl = $"{app1BaseUrl}/api/app2/validate-ott";

            _logger.LogInformation("App2 Backend: Calling App1 backend for OTT validation at: {App1ApiUrl}", app1ApiUrl);

            try
            {
                using var client = _httpClientFactory.CreateClient();
                var app1Response = await client.PostAsJsonAsync(app1ApiUrl, new { ott = storedOttData.Ott, referenceId = request.ReferenceId });
                app1Response.EnsureSuccessStatusCode(); // Throws if not 2xx

                var app1Result = await app1Response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

                if (app1Result != null &&
                    app1Result.TryGetValue("success", out var successObj) &&
                    successObj is JsonElement successElement &&
                    successElement.ValueKind == JsonValueKind.True)
                {
                    // 3. OTT validated by App1. Now establish App2's own session.
                    string userId = "Unknown";
                    if (app1Result.TryGetValue("userId", out var userIdObj) && userIdObj is JsonElement userIdElement && userIdElement.ValueKind == JsonValueKind.String)
                    {
                        userId = userIdElement.GetString()!;
                    }

                    App2State.App2Sessions[request.ReferenceId] = new App2SessionData
                    {
                        ReferenceId = request.ReferenceId,
                        App2SessionExpiry = DateTimeOffset.UtcNow.AddMinutes(20), // App2's session lasts 20 mins
                        UserId = userId
                    };

                    _logger.LogInformation("App2 Backend: Successfully validated OTT with App1 and established App2 session for ReferenceId '{ReferenceId}'.", request.ReferenceId);
                    return Ok(new { success = true, message = "Session established in App2 backend.", userId = userId });
                }
                else
                {
                    string app1ErrorMessage = "Unknown validation error from App1.";
                    if (app1Result != null && app1Result.TryGetValue("message", out var msgObj) && msgObj is JsonElement msgElement && msgElement.ValueKind == JsonValueKind.String)
                    {
                        app1ErrorMessage = msgElement.GetString()!;
                    }
                    _logger.LogWarning("App2 Backend: App1 OTT validation failed: {Message}", app1ErrorMessage);
                    return Unauthorized(new { success = false, message = $"App1 validation failed: {app1ErrorMessage}" });
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "App2 Backend: HTTP request error during App1 OTT validation for ReferenceId '{ReferenceId}': {Message}", request.ReferenceId, ex.Message);
                return StatusCode(500, new { success = false, message = $"Network error communicating with App1: {ex.Message}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "App2 Backend: An unexpected error occurred during OTT validation for ReferenceId '{ReferenceId}': {Message}", request.ReferenceId, ex.Message);
                return StatusCode(500, new { success = false, message = $"An internal error occurred: {ex.Message}" });
            }
        }
    }
}