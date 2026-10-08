using PosPipeline.Shared.Models;

namespace PosPipeline.Shared.Services;

/// <summary>
/// Parses POS report messages into structured data
/// Format: POS/{IATACode}{flightNumber}.FR {departure}/TO {destination}/{ddHHmm}/{lat}{N|S}{lon}{E|W}/{groundSpeedKnots}/{fuelOnBoardKg}/{fuelFlowKgPerHour}
/// Example: POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800
/// </summary>
public class PosParser
{
    /// <summary>
    /// Parses a POS report message string into a structured PosReport object
    /// </summary>
    /// <param name="rawMessage">Raw POS message text</param>
    /// <param name="requestTimestamp">Timestamp when the request was received (UTC)</param>
    /// <returns>Parsed PosReport</returns>
    /// <exception cref="ArgumentException">Thrown if message format is invalid</exception>
    public PosReport Parse(string rawMessage, DateTime requestTimestamp)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            throw new ArgumentException("POS message cannot be empty", nameof(rawMessage));

        if (!rawMessage.StartsWith("POS/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("POS message must start with 'POS/'", nameof(rawMessage));

        var parts = rawMessage.Split('/');
        if (parts.Length < 8)
            throw new ArgumentException($"POS message does not have enough parts (expected at least 8, got {parts.Length})", nameof(rawMessage));

        try
        {
            // Part 0: "POS"
            // Part 1: "{IATA}{flightNumber}.FR {departure}"
            var flightAndDeparture = parts[1].Split(new[] { ".FR " }, StringSplitOptions.None);
            if (flightAndDeparture.Length != 2)
                throw new ArgumentException("Invalid flight/departure format");

            var flightCode = flightAndDeparture[0]; // e.g., "UL204"
            var departure = flightAndDeparture[1];   // e.g., "RGN"

            // Extract IATA code and flight number
            // IATA is typically 2-3 letters, flight number is the rest
            int digitIndex = 0;
            for (int i = 0; i < flightCode.Length; i++)
            {
                if (char.IsDigit(flightCode[i]))
                {
                    digitIndex = i;
                    break;
                }
            }
            
            if (digitIndex == 0)
                throw new ArgumentException("Invalid flight code format");

            var iataCode = flightCode.Substring(0, digitIndex);
            var flightNumber = flightCode.Substring(digitIndex);

            // Part 2: "TO {destination}"
            if (!parts[2].StartsWith("TO ", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Expected 'TO' before destination");
            var destination = parts[2].Substring(3); // Skip "TO "

            // Part 3: "{ddHHmm}" - day, hour, minute
            var timeString = parts[3];
            if (timeString.Length != 6)
                throw new ArgumentException($"Invalid time format - expected 6 characters (ddHHmm), got {timeString.Length}");

            int day = int.Parse(timeString.Substring(0, 2));
            int hour = int.Parse(timeString.Substring(2, 2));
            int minute = int.Parse(timeString.Substring(4, 2));

            // Construct timestamp: use message day + request year/month
            var timestamp = new DateTime(
                requestTimestamp.Year,
                requestTimestamp.Month,
                day,
                hour,
                minute,
                0,
                DateTimeKind.Utc
            );

            // Part 4: "{lat}{N|S}{lon}{E|W}" - e.g., "N1642.3E09612.5"
            var coordString = parts[4];
            int lonStartIndex = -1;
            
            // Find where longitude starts (look for E or W after the latitude)
            for (int i = 1; i < coordString.Length; i++)
            {
                if (coordString[i] == 'E' || coordString[i] == 'e' || 
                    coordString[i] == 'W' || coordString[i] == 'w')
                {
                    lonStartIndex = i;
                    break;
                }
            }

            if (lonStartIndex == -1)
                throw new ArgumentException("Invalid coordinate format - longitude not found");

            var latString = coordString.Substring(0, lonStartIndex);
            var lonString = coordString.Substring(lonStartIndex);

            double latitude = ParseLatitude(latString);
            double longitude = ParseLongitude(lonString);

            // Part 5: Ground speed in knots
            int groundSpeedKnots = int.Parse(parts[5]);

            // Part 6: Fuel on board in kg
            int fuelOnBoardKg = int.Parse(parts[6]);

            // Part 7: Fuel flow in kg/hour
            int fuelFlowKgPerHour = int.Parse(parts[7]);

            // Build flight ID
            var flightIdBuilder = new FlightIdBuilder();
            var flightId = flightIdBuilder.BuildFlightId(
                iataCode, flightNumber, day, requestTimestamp, departure, destination
            );

            return new PosReport
            {
                RawMessage = rawMessage,
                IataCode = iataCode,
                FlightNumber = flightNumber,
                Departure = departure,
                Destination = destination,
                Timestamp = timestamp,
                Latitude = latitude,
                Longitude = longitude,
                GroundSpeedKnots = groundSpeedKnots,
                FuelOnBoardKg = fuelOnBoardKg,
                FuelFlowKgPerHour = fuelFlowKgPerHour,
                FlightId = flightId
            };
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException($"Failed to parse POS message: {ex.Message}", nameof(rawMessage), ex);
        }
    }

    /// <summary>
    /// Converts latitude string (e.g., "N1642.3") to decimal degrees
    /// Format: N/S + 2-digit degrees + decimal minutes
    /// </summary>
    private double ParseLatitude(string latString)
    {
        if (string.IsNullOrEmpty(latString) || latString.Length < 3)
            throw new ArgumentException("Invalid latitude format");

        char hemisphere = char.ToUpper(latString[0]);
        if (hemisphere != 'N' && hemisphere != 'S')
            throw new ArgumentException($"Invalid latitude hemisphere: {hemisphere}");

        // Extract degrees (2 digits) and minutes (rest)
        var coordPart = latString.Substring(1);
        if (coordPart.Length < 2)
            throw new ArgumentException("Latitude must have at least 2-digit degrees");

        int degrees = int.Parse(coordPart.Substring(0, 2));
        double minutes = coordPart.Length > 2 ? double.Parse(coordPart.Substring(2)) : 0.0;

        // Convert to decimal degrees
        double decimalDegrees = degrees + (minutes / 60.0);

        // South is negative
        if (hemisphere == 'S')
            decimalDegrees = -decimalDegrees;

        return decimalDegrees;
    }

    /// <summary>
    /// Converts longitude string (e.g., "E09612.5") to decimal degrees
    /// Format: E/W + 3-digit degrees + decimal minutes
    /// </summary>
    private double ParseLongitude(string lonString)
    {
        if (string.IsNullOrEmpty(lonString) || lonString.Length < 4)
            throw new ArgumentException("Invalid longitude format");

        char hemisphere = char.ToUpper(lonString[0]);
        if (hemisphere != 'E' && hemisphere != 'W')
            throw new ArgumentException($"Invalid longitude hemisphere: {hemisphere}");

        // Extract degrees (3 digits) and minutes (rest)
        var coordPart = lonString.Substring(1);
        if (coordPart.Length < 3)
            throw new ArgumentException("Longitude must have at least 3-digit degrees");

        int degrees = int.Parse(coordPart.Substring(0, 3));
        double minutes = coordPart.Length > 3 ? double.Parse(coordPart.Substring(3)) : 0.0;

        // Convert to decimal degrees
        double decimalDegrees = degrees + (minutes / 60.0);

        // West is negative
        if (hemisphere == 'W')
            decimalDegrees = -decimalDegrees;

        return decimalDegrees;
    }
}
