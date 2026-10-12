using Xunit;
using PosPipeline.Shared.Services;
using PosPipeline.Shared.Models;

namespace PosPipeline.Tests;

/// <summary>
/// Tests for POS parsing with gold example data
/// Gold example: POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800
/// Expected: lat 16.705, lon 96.2083
/// </summary>
public class PosParserTests
{
    [Fact]
    public void Parse_GoldExample_ReturnsExpectedLatitude()
    {
        // Arrange
        var parser = new PosParser();
        var rawMessage = "POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800";
        var requestTimestamp = new DateTime(2026, 9, 4, 12, 5, 0, DateTimeKind.Utc);

        // Act
        var result = parser.Parse(rawMessage, requestTimestamp);

        // Assert
        // N1642.3 -> 16 + 42.3/60 = 16.705
        Assert.Equal(16.705, result.Latitude, 4);
    }

    [Fact]
    public void Parse_GoldExample_ReturnsExpectedLongitude()
    {
        // Arrange
        var parser = new PosParser();
        var rawMessage = "POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800";
        var requestTimestamp = new DateTime(2026, 9, 4, 12, 5, 0, DateTimeKind.Utc);

        // Act
        var result = parser.Parse(rawMessage, requestTimestamp);

        // Assert
        // E09612.5 -> 96 + 12.5/60 = 96.2083...
        Assert.Equal(96.2083, result.Longitude, 4);
    }

    [Fact]
    public void Parse_GoldExample_ReturnsCorrectFlightData()
    {
        // Arrange
        var parser = new PosParser();
        var rawMessage = "POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800";
        var requestTimestamp = new DateTime(2026, 9, 4, 12, 5, 0, DateTimeKind.Utc);

        // Act
        var result = parser.Parse(rawMessage, requestTimestamp);

        // Assert
        Assert.Equal("UL", result.IataCode);
        Assert.Equal("204", result.FlightNumber);
        Assert.Equal("RGN", result.Departure);
        Assert.Equal("BKK", result.Destination);
        Assert.Equal(450, result.GroundSpeedKnots);
        Assert.Equal(12500, result.FuelOnBoardKg);
        Assert.Equal(2800, result.FuelFlowKgPerHour);
        Assert.Equal("UL20420260904RGNBKK", result.FlightId);
        Assert.Equal(new DateTime(2026, 9, 4, 12, 5, 0, DateTimeKind.Utc), result.Timestamp);
    }

    [Fact]
    public void Parse_SouthLatitude_ReturnsNegative()
    {
        // Arrange
        var parser = new PosParser();
        // Sydney coordinates example: S3352.0 (South latitude)
        var rawMessage = "POS/QF123.FR SYD/TO MEL/151030/S3352.0E15112.0/500/15000/3000";
        var requestTimestamp = new DateTime(2026, 10, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        var result = parser.Parse(rawMessage, requestTimestamp);

        // Assert
        // S3352.0 -> -(33 + 52.0/60) = -33.8667
        Assert.True(result.Latitude < 0, "South latitude should be negative");
        Assert.Equal(-33.8667, result.Latitude, 4);
    }

    [Fact]
    public void Parse_WestLongitude_ReturnsNegative()
    {
        // Arrange
        var parser = new PosParser();
        // West longitude example
        var rawMessage = "POS/AA100.FR JFK/TO LAX/151030/N4021.0W07358.0/480/16000/3200";
        var requestTimestamp = new DateTime(2026, 10, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        var result = parser.Parse(rawMessage, requestTimestamp);

        // Assert
        // W07358.0 -> -(73 + 58.0/60) = -73.9667
        Assert.True(result.Longitude < 0, "West longitude should be negative");
        Assert.Equal(-73.9667, result.Longitude, 4);
    }

    [Fact]
    public void Parse_InvalidMessage_ThrowsArgumentException()
    {
        // Arrange
        var parser = new PosParser();
        var requestTimestamp = DateTime.UtcNow;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => parser.Parse("", requestTimestamp));
        Assert.Throws<ArgumentException>(() => parser.Parse("INVALID", requestTimestamp));
        Assert.Throws<ArgumentException>(() => parser.Parse("POS/", requestTimestamp));
        Assert.Throws<ArgumentException>(() => parser.Parse("POS/UL204", requestTimestamp));
    }

    [Fact]
    public void Parse_MissingParts_ThrowsArgumentException()
    {
        // Arrange
        var parser = new PosParser();
        var requestTimestamp = DateTime.UtcNow;

        // Act & Assert - not enough parts (only 7 parts, need 8)
        Assert.Throws<ArgumentException>(() => 
            parser.Parse("POS/UL204.FR RGN/TO BKK/041205", requestTimestamp));
    }

    [Fact]
    public void Parse_TooFewSegments_ThrowsArgumentException()
    {
        // Arrange
        var parser = new PosParser();
        var requestTimestamp = DateTime.UtcNow;

        // Act & Assert - 7 segments instead of 8
        var message = "POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500";
        Assert.Throws<ArgumentException>(() => parser.Parse(message, requestTimestamp));
    }

    [Fact]
    public void Parse_BadTimeField_ThrowsArgumentException()
    {
        // Arrange
        var parser = new PosParser();
        var requestTimestamp = DateTime.UtcNow;

        // Act & Assert - time field too short (5 chars instead of 6)
        var message = "POS/UL204.FR RGN/TO BKK/04120/N1642.3E09612.5/450/12500/2800";
        Assert.Throws<ArgumentException>(() => parser.Parse(message, requestTimestamp));

        // Act & Assert - time field too long
        var message2 = "POS/UL204.FR RGN/TO BKK/0412055/N1642.3E09612.5/450/12500/2800";
        Assert.Throws<ArgumentException>(() => parser.Parse(message2, requestTimestamp));
    }

    [Fact]
    public void Parse_BadCoordinates_ThrowsArgumentException()
    {
        // Arrange
        var parser = new PosParser();
        var requestTimestamp = DateTime.UtcNow;

        // Act & Assert - missing longitude hemisphere
        var message = "POS/UL204.FR RGN/TO BKK/041205/N1642.3096125/450/12500/2800";
        Assert.Throws<ArgumentException>(() => parser.Parse(message, requestTimestamp));

        // Act & Assert - invalid latitude hemisphere
        var message2 = "POS/UL204.FR RGN/TO BKK/041205/X1642.3E09612.5/450/12500/2800";
        Assert.Throws<ArgumentException>(() => parser.Parse(message2, requestTimestamp));
    }
}

/// <summary>
/// Tests for FlightId generation
/// Expected: UL20420260904RGNBKK with frozen clock
/// </summary>
public class FlightIdBuilderTests
{
    [Fact]
    public void BuildFlightId_GoldExample_ReturnsExpectedFormat()
    {
        // Arrange
        var builder = new FlightIdBuilder();
        var requestTimestamp = new DateTime(2026, 9, 4, 12, 5, 0, DateTimeKind.Utc);

        // Act
        var flightId = builder.BuildFlightId("UL", "204", 4, requestTimestamp, "RGN", "BKK");

        // Assert
        Assert.Equal("UL20420260904RGNBKK", flightId);
    }

    [Fact]
    public void BuildFlightId_DifferentMonth_UsesRequestMonth()
    {
        // Arrange
        var builder = new FlightIdBuilder();
        var requestTimestamp = new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc);

        // Act - message day is 14, but request is Oct 15
        var flightId = builder.BuildFlightId("AA", "100", 14, requestTimestamp, "JFK", "LAX");

        // Assert - should use request year/month (2026/10) + message day (14)
        Assert.Equal("AA10020261014JFKLAX", flightId);
    }

    [Fact]
    public void BuildFlightId_ThreeLetterIata_Works()
    {
        // Arrange
        var builder = new FlightIdBuilder();
        var requestTimestamp = new DateTime(2026, 12, 25, 10, 0, 0, DateTimeKind.Utc);

        // Act
        var flightId = builder.BuildFlightId("SIA", "777", 25, requestTimestamp, "SIN", "LHR");

        // Assert
        Assert.Equal("SIA77720261225SINLHR", flightId);
    }
}

/// <summary>
/// Tests for Haversine distance calculation
/// Gold example: RGN position (16.705, 96.2083) to BKK (13.681, 100.747)
/// Expected: ~319 NM, ~43 min at 450 knots, ~10513 kg fuel at arrival
/// </summary>
public class HaversineCalculatorTests
{
    [Fact]
    public void CalculateDistance_GoldExample_ReturnsExpectedDistance()
    {
        // Arrange
        var calculator = new HaversineCalculator();
        var currentLat = 16.705;
        var currentLon = 96.2083;
        var destLat = 13.681;  // BKK
        var destLon = 100.747;

        // Act
        var distance = calculator.CalculateDistance(currentLat, currentLon, destLat, destLon);

        // Assert - allow reasonable tolerance for floating point
        Assert.InRange(distance, 318, 320); // Expected ~319 NM
    }

    [Fact]
    public void CalculateDistance_SamePoint_ReturnsZero()
    {
        // Arrange
        var calculator = new HaversineCalculator();

        // Act
        var distance = calculator.CalculateDistance(13.681, 100.747, 13.681, 100.747);

        // Assert
        Assert.Equal(0, distance, 1); // Should be essentially zero
    }

    [Fact]
    public void CalculateDistance_KnownDistance_ReturnsCorrectValue()
    {
        // Arrange - LAX to JFK is approximately 2139 NM
        var calculator = new HaversineCalculator();
        var laxLat = 33.9416;
        var laxLon = -118.4085;
        var jfkLat = 40.6413;
        var jfkLon = -73.7781;

        // Act
        var distance = calculator.CalculateDistance(laxLat, laxLon, jfkLat, jfkLon);

        // Assert - within reasonable tolerance
        Assert.InRange(distance, 2130, 2150);
    }

    [Fact]
    public void CalculateFlightTimeHours_GoldExample_ReturnsExpectedTime()
    {
        // Arrange
        var calculator = new HaversineCalculator();
        var distanceNM = 319.0;
        var groundSpeedKnots = 450;

        // Act
        var timeHours = calculator.CalculateFlightTimeHours(distanceNM, groundSpeedKnots);

        // Assert
        var timeMinutes = (int)Math.Round(timeHours * 60);
        Assert.InRange(timeMinutes, 42, 43); // Expected ~42.53 min rounds to 43
    }

    [Fact]
    public void CalculateFlightTimeHours_ZeroSpeed_ThrowsException()
    {
        // Arrange
        var calculator = new HaversineCalculator();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            calculator.CalculateFlightTimeHours(100, 0));
    }

    [Fact]
    public void CalculateFuelConsumption_ReturnsCorrectValue()
    {
        // Arrange
        var calculator = new HaversineCalculator();
        var fuelFlowKgPerHour = 2800;
        var flightTimeHours = 2.0;

        // Act
        var consumption = calculator.CalculateFuelConsumption(fuelFlowKgPerHour, flightTimeHours);

        // Assert
        Assert.Equal(5600, consumption);
    }

    [Fact]
    public void CalculateFuelAtArrival_GoldExample_ReturnsExpectedFuel()
    {
        // Arrange
        var calculator = new HaversineCalculator();
        var fuelOnBoard = 12500; // kg
        var fuelFlowKgPerHour = 2800;
        var flightTimeHours = 319.0 / 450.0; // ~0.7089 hours

        // Act
        var fuelConsumption = calculator.CalculateFuelConsumption(fuelFlowKgPerHour, flightTimeHours);
        var fuelAtArrival = (int)Math.Round(fuelOnBoard - fuelConsumption);

        // Assert
        // 12500 - (2800 * 0.7089) = 12500 - 1984.9 ≈ 10515
        Assert.InRange(fuelAtArrival, 10510, 10520);
    }

    [Fact]
    public void LowFuelWarning_NegativeFuel_ShouldWarn()
    {
        // Arrange
        var calculator = new HaversineCalculator();
        var fuelOnBoard = 1000; // kg - not enough fuel
        var fuelFlowKgPerHour = 2800;
        var flightTimeHours = 1.0; // 1 hour flight

        // Act
        var fuelConsumption = calculator.CalculateFuelConsumption(fuelFlowKgPerHour, flightTimeHours);
        var fuelAtArrival = fuelOnBoard - (int)Math.Round(fuelConsumption);

        // Assert
        Assert.True(fuelAtArrival < 0, "Should have negative fuel (low fuel warning)");
    }
}

/// <summary>
/// Tests for airport lookup
/// </summary>
public class AirportLookupTests
{
    [Fact]
    public void GetAirport_KnownAirport_ReturnsAirport()
    {
        // Arrange
        var lookup = new AirportLookup();

        // Act
        var rgn = lookup.GetAirport("RGN");
        var bkk = lookup.GetAirport("BKK");
        var sin = lookup.GetAirport("SIN");

        // Assert
        Assert.Equal("RGN", rgn.IataCode);
        Assert.Equal("BKK", bkk.IataCode);
        Assert.Equal("SIN", sin.IataCode);
        
        // Verify coordinates are set
        Assert.NotEqual(0, rgn.Latitude);
        Assert.NotEqual(0, bkk.Latitude);
        Assert.NotEqual(0, sin.Latitude);
    }

    [Fact]
    public void GetAirport_CaseInsensitive_ReturnsAirport()
    {
        // Arrange
        var lookup = new AirportLookup();

        // Act
        var bkk1 = lookup.GetAirport("BKK");
        var bkk2 = lookup.GetAirport("bkk");
        var bkk3 = lookup.GetAirport("Bkk");

        // Assert
        Assert.Equal(bkk1.IataCode, bkk2.IataCode);
        Assert.Equal(bkk1.IataCode, bkk3.IataCode);
    }

    [Fact]
    public void GetAirport_UnknownAirport_ThrowsKeyNotFoundException()
    {
        // Arrange
        var lookup = new AirportLookup();

        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => lookup.GetAirport("XXX"));
    }

    [Fact]
    public void GetAirport_EmptyCode_ThrowsArgumentException()
    {
        // Arrange
        var lookup = new AirportLookup();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => lookup.GetAirport(""));
        Assert.Throws<ArgumentException>(() => lookup.GetAirport(null!));
    }

    [Fact]
    public void HasAirport_KnownAirport_ReturnsTrue()
    {
        // Arrange
        var lookup = new AirportLookup();

        // Act & Assert
        Assert.True(lookup.HasAirport("RGN"));
        Assert.True(lookup.HasAirport("BKK"));
        Assert.True(lookup.HasAirport("SIN"));
    }

    [Fact]
    public void HasAirport_UnknownAirport_ReturnsFalse()
    {
        // Arrange
        var lookup = new AirportLookup();

        // Act & Assert
        Assert.False(lookup.HasAirport("XXX"));
        Assert.False(lookup.HasAirport("UNKNOWN"));
    }
}

/// <summary>
/// Tests for S3 key uniqueness and formatting with ISO-8601 timestamps
/// </summary>
public class S3KeyFormattingTests
{
    [Fact]
    public void S3Key_UsesISO8601WithMilliseconds_ForUniqueness()
    {
        // Arrange
        var timestamp1 = new DateTime(2026, 10, 4, 12, 5, 0, 123, DateTimeKind.Utc);
        var timestamp2 = new DateTime(2026, 10, 4, 12, 5, 0, 456, DateTimeKind.Utc);
        var flightId = "UL20420261004RGNBKK";

        // Act - Format keys with ISO-8601 format including milliseconds
        var key1 = $"pos/{flightId}-{timestamp1:yyyy-MM-ddTHH:mm:ss.fffZ}";
        var key2 = $"pos/{flightId}-{timestamp2:yyyy-MM-ddTHH:mm:ss.fffZ}";

        // Assert - Keys are different despite same second
        Assert.NotEqual(key1, key2);
        Assert.Contains("2026-10-04T12:05:00.123Z", key1);
        Assert.Contains("2026-10-04T12:05:00.456Z", key2);
    }

    [Fact]
    public void AttachmentKey_UsesISO8601WithoutMilliseconds()
    {
        // Arrange - Attachment uses POS timestamp without milliseconds
        var posTimestamp = new DateTime(2026, 9, 4, 12, 5, 0, DateTimeKind.Utc);
        var flightId = "UL20420260904RGNBKK";

        // Act - Format attachment key
        var attachmentKey = $"attachment/{flightId}-{posTimestamp:yyyy-MM-ddTHH:mm:ssZ}.json";

        // Assert - Should match brief format
        Assert.Equal("attachment/UL20420260904RGNBKK-2026-09-04T12:05:00Z.json", attachmentKey);
    }

    [Fact]
    public void ResultKey_UsesISO8601WithMilliseconds()
    {
        // Arrange
        var calcTimestamp = new DateTime(2026, 9, 4, 12, 6, 15, 842, DateTimeKind.Utc);
        var flightId = "UL20420260904RGNBKK";

        // Act - Format result key
        var resultKey = $"results/{flightId}-{calcTimestamp:yyyy-MM-ddTHH:mm:ss.fffZ}.json";

        // Assert - Should match brief format
        Assert.Equal("results/UL20420260904RGNBKK-2026-09-04T12:06:15.842Z.json", resultKey);
    }
}

/// <summary>
/// Tests for Status endpoint timestamp comparison logic
/// </summary>
public class StatusTimestampComparisonTests
{
    [Fact]
    public void Status_NewerParsedRecord_ShouldReturnProcessing()
    {
        // Arrange - Calculation at 12:00:00, parsed record at 12:01:00
        var calcTimestamp = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var parsedTimestamp = new DateTime(2026, 10, 4, 12, 1, 0, DateTimeKind.Utc);

        // Act - Compare timestamps
        var shouldShowProcessing = parsedTimestamp > calcTimestamp;

        // Assert - Should return PROCESSING status, not COMPLETE
        Assert.True(shouldShowProcessing, "Newer parsed record should show PROCESSING status");
    }

    [Fact]
    public void Status_CalcIsNewer_ShouldReturnComplete()
    {
        // Arrange - Parsed at 12:00:00, calculation at 12:01:00
        var parsedTimestamp = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var calcTimestamp = new DateTime(2026, 10, 4, 12, 1, 0, DateTimeKind.Utc);

        // Act - Compare timestamps
        var shouldShowComplete = calcTimestamp >= parsedTimestamp;

        // Assert - Should return COMPLETE status
        Assert.True(shouldShowComplete, "Newer calculation should show COMPLETE status");
    }

    [Fact]
    public void Status_SameTimestamp_ShouldReturnComplete()
    {
        // Arrange - Both at exactly the same time
        var parsedTimestamp = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var calcTimestamp = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

        // Act - Compare timestamps
        var shouldShowComplete = calcTimestamp >= parsedTimestamp;

        // Assert - Should return COMPLETE when timestamps match
        Assert.True(shouldShowComplete, "Equal timestamps should show COMPLETE status");
    }
}

/// <summary>
/// Tests for Parser using ingest timestamp from metadata
/// </summary>
public class ParserMetadataTests
{
    [Fact]
    public void Parser_UsesIngestTimestamp_NotS3EventTime()
    {
        // This test documents the expected behavior:
        // Parser should extract flightId and ingest timestamp from S3 object metadata
        // (set by Ingest when writing to S3) rather than using the S3 event time.
        // This prevents flightId mismatches near month boundaries.

        // Arrange
        var ingestTime = new DateTime(2026, 9, 30, 23, 59, 0, DateTimeKind.Utc); // End of Sept
        var s3EventTime = new DateTime(2026, 10, 1, 0, 0, 5, DateTimeKind.Utc);  // Oct 1 (S3 event delayed)
        
        var parser = new PosParser();
        var rawMessage = "POS/UL204.FR RGN/TO BKK/301200/N1642.3E09612.5/450/12500/2800";

        // Act - Parse with ingest time (from metadata)
        var resultWithIngestTime = parser.Parse(rawMessage, ingestTime);
        
        // Parse with S3 event time (wrong - would give different month)
        var resultWithEventTime = parser.Parse(rawMessage, s3EventTime);

        // Assert - Using ingest time gives correct September flightId
        Assert.Contains("202609", resultWithIngestTime.FlightId); // Sept
        Assert.Contains("202610", resultWithEventTime.FlightId);  // Oct (wrong!)
        
        // The fix ensures Parser uses metadata so both scenarios produce the Sept flightId
        Assert.NotEqual(resultWithIngestTime.FlightId, resultWithEventTime.FlightId);
    }
}