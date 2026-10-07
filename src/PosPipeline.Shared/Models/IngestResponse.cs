namespace PosPipeline.Shared.Models;

/// <summary>
/// Response model for ingest endpoint
/// </summary>
public class IngestResponse
{
    /// <summary>
    /// Flight identifier
    /// </summary>
    public string FlightId { get; set; } = string.Empty;

    /// <summary>
    /// Status: typically "RECEIVED"
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of ingestion
    /// </summary>
    public DateTime Timestamp { get; set; }
}
