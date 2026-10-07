namespace PosPipeline.Shared.Services;

/// <summary>
/// Builds unique flight identifiers from POS report data
/// Format: {IATA}{number}{yyyyMMdd}{dep}{dest}
/// Example: UL20420260904RGNBKK
/// Date = message day (dd) + year/month of request receipt (UTC)
/// </summary>
public class FlightIdBuilder
{
    /// <summary>
    /// Builds a flight ID from components
    /// </summary>
    /// <param name="iataCode">IATA airline code (e.g., "UL")</param>
    /// <param name="flightNumber">Flight number (e.g., "204")</param>
    /// <param name="messageDay">Day from POS message (1-31)</param>
    /// <param name="requestTimestamp">Request timestamp (UTC) - provides year and month</param>
    /// <param name="departure">Departure airport code</param>
    /// <param name="destination">Destination airport code</param>
    /// <returns>Flight ID string</returns>
    public string BuildFlightId(
        string iataCode,
        string flightNumber,
        int messageDay,
        DateTime requestTimestamp,
        string departure,
        string destination)
    {
        // Format: {IATA}{number}{yyyyMMdd}{dep}{dest}
        // Date = message day + year/month from request timestamp
        var year = requestTimestamp.Year;
        var month = requestTimestamp.Month;
        
        // Build the date string
        var dateString = $"{year:D4}{month:D2}{messageDay:D2}";
        
        return $"{iataCode}{flightNumber}{dateString}{departure}{destination}";
    }
}
