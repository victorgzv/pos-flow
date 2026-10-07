namespace PosPipeline.Shared.Models;

/// <summary>
/// Represents an airport with its IATA code and geographic coordinates
/// </summary>
public class Airport
{
    /// <summary>
    /// IATA airport code (e.g., "RGN", "BKK", "SIN")
    /// </summary>
    public string IataCode { get; set; } = string.Empty;

    /// <summary>
    /// Airport name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Latitude in decimal degrees
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Longitude in decimal degrees
    /// </summary>
    public double Longitude { get; set; }
}
