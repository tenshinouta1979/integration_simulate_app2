using System.Collections.Concurrent;
using System.Text.Json; // Added for JsonElement

namespace app2.Models
{
    public static class App2State
    {
        // Stores One-Time Tokens (OTTs) received by app2's backend from app1's backend.
        // Maps ReferenceId to OTT data.
        public static ConcurrentDictionary<string, OneTimeTokenData> ReceivedOneTimeTokens { get; } = new();

        // Stores app2's internal sessions, mapping ReferenceId to app2 session data.
        public static ConcurrentDictionary<string, App2SessionData> App2Sessions { get; } = new();
    }

    // Represents a One-Time Token (OTT) received by app2.
    public class OneTimeTokenData
    {
        public string Ott { get; set; } = string.Empty;       // The actual OTT string.
        public string ReferenceId { get; set; } = string.Empty; // Links this OTT to a specific app1 session.
        public DateTimeOffset Expiry { get; set; }              // When the OTT itself expires.
        public bool IsUsed { get; set; } = false;               // To ensure OTTs are single-use.
        public DateTimeOffset ReceivedTime { get; set; } = DateTimeOffset.UtcNow; // When app2 received it.
    }

    // Represents app2's internal session for a user, linked to app1's session via ReferenceId.
    public class App2SessionData
    {
        public string ReferenceId { get; set; } = string.Empty; // Reference to app1's session.
        public DateTimeOffset App2SessionExpiry { get; set; }   // When app2's own session expires.
        public string UserId { get; set; } = string.Empty;       // User ID obtained from app1 (for display/logging).
    }

    // Model for the backend-to-backend OTT transfer request from app1.
    public class ReceiveOttRequest
    {
        public string Ott { get; set; } = string.Empty;
        public string ReferenceId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty; // Optional: app1 can send user info
    }

    // Model for app2 client's request to establish session.
    public class EstablishSessionRequest
    {
        public string ReferenceId { get; set; } = string.Empty;
    }
    // Minor comment added to force re-compilation.
}