namespace PosPipeline.Shared.Services;

/// <summary>
/// Calculates great circle distances between geographic coordinates using the Haversine formula
/// </summary>
public class HaversineCalculator
{
    /// <summary>
    /// Earth radius in nautical miles (as specified in requirements)
    /// </summary>
    public const double EarthRadiusNauticalMiles = 3440.065;

    /// <summary>
    /// Calculates the distance between two points on Earth using the Haversine formula
    /// </summary>
    /// <param name="lat1">Latitude of first point in decimal degrees</param>
    /// <param name="lon1">Longitude of first point in decimal degrees</param>
    /// <param name="lat2">Latitude of second point in decimal degrees</param>
    /// <param name="lon2">Longitude of second point in decimal degrees</param>
    /// <returns>Distance in nautical miles</returns>
    /// <remarks>
    /// Formula: a = sin²(Δφ/2) + cos φ1 ⋅ cos φ2 ⋅ sin²(Δλ/2)
    ///          c = 2 ⋅ atan2(√a, √(1−a))
    ///          d = R ⋅ c
    /// where φ is latitude, λ is longitude, R is earth's radius
    /// </remarks>
    public double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        // Convert degrees to radians
        double lat1Rad = DegreesToRadians(lat1);
        double lon1Rad = DegreesToRadians(lon1);
        double lat2Rad = DegreesToRadians(lat2);
        double lon2Rad = DegreesToRadians(lon2);

        // Calculate differences
        double deltaLat = lat2Rad - lat1Rad;
        double deltaLon = lon2Rad - lon1Rad;

        // Apply Haversine formula
        double a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                   Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                   Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        // Distance in nautical miles
        double distance = EarthRadiusNauticalMiles * c;

        return distance;
    }

    /// <summary>
    /// Converts degrees to radians
    /// </summary>
    private double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    /// <summary>
    /// Calculates remaining flight time based on distance and ground speed
    /// </summary>
    /// <param name="distanceNauticalMiles">Distance in nautical miles</param>
    /// <param name="groundSpeedKnots">Ground speed in knots</param>
    /// <returns>Flight time in hours</returns>
    public double CalculateFlightTimeHours(double distanceNauticalMiles, int groundSpeedKnots)
    {
        if (groundSpeedKnots <= 0)
            throw new ArgumentException("Ground speed must be positive", nameof(groundSpeedKnots));

        return distanceNauticalMiles / groundSpeedKnots;
    }

    /// <summary>
    /// Calculates fuel consumption based on fuel flow rate and flight time
    /// </summary>
    /// <param name="fuelFlowKgPerHour">Fuel flow in kg/hour</param>
    /// <param name="flightTimeHours">Flight time in hours</param>
    /// <returns>Fuel consumption in kg</returns>
    public double CalculateFuelConsumption(int fuelFlowKgPerHour, double flightTimeHours)
    {
        return fuelFlowKgPerHour * flightTimeHours;
    }
}
