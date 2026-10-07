namespace PosPipeline.Shared.Models;

/// <summary>
/// Response model for status endpoint
/// </summary>
public class StatusResponse
{
    /// <summary>
    /// Flight identifier
    /// </summary>
    public string FlightId { get; set; } = string.Empty;

    /// <summary>
    /// Status: RECEIVED, PROCESSING, or COMPLETE
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Latest timestamp from calculation or parse
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Remaining flight time in minutes (only present when status is COMPLETE)
    /// </summary>
    public int? RemainingFlightTimeMinutes { get; set; }

    /// <summary>
    /// Estimated fuel at arrival in kg (only present when status is COMPLETE)
    /// </summary>
    public int? EstimatedFuelAtArrivalKg { get; set; }

    /// <summary>
    /// Low fuel warning flag (only present when status is COMPLETE)
    /// </summary>
    public bool? LowFuelWarning { get; set; }
}
