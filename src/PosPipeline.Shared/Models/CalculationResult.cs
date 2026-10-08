namespace PosPipeline.Shared.Models;

/// <summary>
/// Represents the result of flight calculations (distance, time, fuel)
/// </summary>
public class CalculationResult
{
    /// <summary>
    /// Unique flight identifier
    /// </summary>
    public string FlightId { get; set; } = string.Empty;

    /// <summary>
    /// Calculation timestamp (when the calculation was performed)
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Distance remaining to destination in nautical miles
    /// </summary>
    public double DistanceNauticalMiles { get; set; }

    /// <summary>
    /// Remaining flight time in minutes (rounded to nearest integer)
    /// </summary>
    public int RemainingFlightTimeMinutes { get; set; }

    /// <summary>
    /// Estimated fuel at arrival in kilograms (rounded to nearest integer)
    /// </summary>
    public int EstimatedFuelAtArrivalKg { get; set; }

    /// <summary>
    /// True if fuel at arrival is negative (low fuel warning)
    /// </summary>
    public bool LowFuelWarning { get; set; }
}
