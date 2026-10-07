namespace PosPipeline.Shared.Models;

/// <summary>
/// Represents a parsed Position (POS) report from an aircraft
/// </summary>
public class PosReport
{
    /// <summary>
    /// IATA airline code (e.g., "UL")
    /// </summary>
    public string IataCode { get; set; } = string.Empty;

    /// <summary>
    /// Flight number (e.g., "204")
    /// </summary>
    public string FlightNumber { get; set; } = string.Empty;

    /// <summary>
    /// Departure airport code (e.g., "RGN")
    /// </summary>
    public string Departure { get; set; } = string.Empty;

    /// <summary>
    /// Destination airport code (e.g., "BKK")
    /// </summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Position report timestamp (UTC)
    /// Derived from ddHHmm in message + request year/month
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Latitude in decimal degrees (positive for North, negative for South)
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Longitude in decimal degrees (positive for East, negative for West)
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Ground speed in knots
    /// </summary>
    public int GroundSpeedKnots { get; set; }

    /// <summary>
    /// Fuel on board in kilograms
    /// </summary>
    public int FuelOnBoardKg { get; set; }

    /// <summary>
    /// Fuel flow rate in kg/hour
    /// </summary>
    public int FuelFlowKgPerHour { get; set; }

    /// <summary>
    /// Unique flight identifier: {IATA}{number}{yyyyMMdd}{dep}{dest}
    /// Example: UL20420260904RGNBKK
    /// </summary>
    public string FlightId { get; set; } = string.Empty;

    /// <summary>
    /// Raw POS message text
    /// </summary>
    public string RawMessage { get; set; } = string.Empty;
}
