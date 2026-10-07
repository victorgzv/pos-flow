using PosPipeline.Shared.Models;

namespace PosPipeline.Shared.Services;

/// <summary>
/// Provides airport information lookup by IATA code
/// Static dictionary for assessment (RGN, BKK, SIN + test airports)
/// </summary>
public class AirportLookup
{
    private static readonly Dictionary<string, Airport> _airports = new(StringComparer.OrdinalIgnoreCase)
    {
        // Real airports from requirements
        ["RGN"] = new Airport
        {
            IataCode = "RGN",
            Name = "Yangon International Airport",
            Latitude = 16.907,
            Longitude = 96.133
        },
        ["BKK"] = new Airport
        {
            IataCode = "BKK",
            Name = "Suvarnabhumi Airport (Bangkok)",
            Latitude = 13.681,
            Longitude = 100.747
        },
        ["SIN"] = new Airport
        {
            IataCode = "SIN",
            Name = "Singapore Changi Airport",
            Latitude = 1.350,
            Longitude = 103.994
        },
        
        // Test airports (if needed for additional test cases)
        ["JFK"] = new Airport
        {
            IataCode = "JFK",
            Name = "John F. Kennedy International Airport",
            Latitude = 40.6413,
            Longitude = -73.7781
        },
        ["LAX"] = new Airport
        {
            IataCode = "LAX",
            Name = "Los Angeles International Airport",
            Latitude = 33.9416,
            Longitude = -118.4085
        }
    };

    /// <summary>
    /// Gets airport information by IATA code
    /// </summary>
    /// <param name="iataCode">3-letter IATA airport code</param>
    /// <returns>Airport information</returns>
    /// <exception cref="KeyNotFoundException">Thrown if airport code is not found</exception>
    public Airport GetAirport(string iataCode)
    {
        if (string.IsNullOrWhiteSpace(iataCode))
            throw new ArgumentException("IATA code cannot be empty", nameof(iataCode));

        if (_airports.TryGetValue(iataCode, out var airport))
            return airport;

        throw new KeyNotFoundException($"Airport '{iataCode}' not found in lookup table. Known airports: {string.Join(", ", _airports.Keys)}");
    }

    /// <summary>
    /// Checks if an airport code exists in the lookup table
    /// </summary>
    public bool HasAirport(string iataCode)
    {
        return !string.IsNullOrWhiteSpace(iataCode) && _airports.ContainsKey(iataCode);
    }

    /// <summary>
    /// Gets all known airport codes
    /// </summary>
    public IEnumerable<string> GetKnownAirportCodes()
    {
        return _airports.Keys;
    }
}
