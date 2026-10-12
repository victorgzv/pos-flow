using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Text.Json;
using PosPipeline.Shared.Models;
using PosPipeline.Shared.Services;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace PosPipeline.Calculator;

public class Function
{
    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonDynamoDB _dynamoClient;
    private readonly string _bucketName;
    private readonly string _parsedRecordsTable;
    private readonly string _calculationResultsTable;
    private readonly HaversineCalculator _haversineCalculator;
    private readonly AirportLookup _airportLookup;

    public Function()
    {
        _s3Client = new AmazonS3Client();
        _dynamoClient = new AmazonDynamoDBClient();
        _bucketName = Environment.GetEnvironmentVariable("BUCKET_NAME") ?? throw new InvalidOperationException("BUCKET_NAME not set");
        _parsedRecordsTable = Environment.GetEnvironmentVariable("PARSED_RECORDS_TABLE") ?? throw new InvalidOperationException("PARSED_RECORDS_TABLE not set");
        _calculationResultsTable = Environment.GetEnvironmentVariable("CALCULATION_RESULTS_TABLE") ?? throw new InvalidOperationException("CALCULATION_RESULTS_TABLE not set");
        _haversineCalculator = new HaversineCalculator();
        _airportLookup = new AirportLookup();
    }

    /// <summary>
    /// Lambda handler for SQS messages from ProcessQueue
    /// Loads parsed data, performs distance/time/fuel calculations, writes results
    /// </summary>
    public async Task<SQSBatchResponse> FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        var batchItemFailures = new List<SQSBatchResponse.BatchItemFailure>();

        foreach (var record in sqsEvent.Records)
        {
            try
            {
                context.Logger.LogInformation($"Processing SQS message: {record.MessageId}");

                // Parse message body
                var message = JsonSerializer.Deserialize<Dictionary<string, string>>(record.Body);
                var flightId = message?["flightId"] ?? throw new InvalidOperationException("Missing flightId in message");
                var attachmentKey = message?["attachmentKey"] ?? throw new InvalidOperationException("Missing attachmentKey in message");
                var timestamp = message?["timestamp"] ?? throw new InvalidOperationException("Missing timestamp in message");

                // Load parsed record from DynamoDB as the authoritative source
                var queryResponse = await _dynamoClient.QueryAsync(new QueryRequest
                {
                    TableName = _parsedRecordsTable,
                    KeyConditionExpression = "flightId = :fid AND #ts = :timestamp",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#ts"] = "timestamp"
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":fid"] = new AttributeValue { S = flightId },
                        [":timestamp"] = new AttributeValue { S = timestamp }
                    },
                    Limit = 1
                });

                if (queryResponse.Items.Count == 0)
                    throw new InvalidOperationException($"No parsed record found for flight {flightId} at {timestamp}");

                // Get parsed POS data from S3 attachment (full payload with all fields)
                var getResponse = await _s3Client.GetObjectAsync(_bucketName, attachmentKey);
                PosReport? posReport;
                using (var reader = new StreamReader(getResponse.ResponseStream))
                {
                    var json = await reader.ReadToEndAsync();
                    posReport = JsonSerializer.Deserialize<PosReport>(json);
                }

                if (posReport == null)
                    throw new InvalidOperationException("Failed to deserialize PosReport");

                context.Logger.LogInformation($"Loaded parsed record from DynamoDB and attachment from S3 for flight {flightId}");

                context.Logger.LogInformation($"Processing calculation for flight {flightId}");

                // Get destination airport coordinates
                var destination = _airportLookup.GetAirport(posReport.Destination);

                // Calculate distance using Haversine
                var distanceNM = _haversineCalculator.CalculateDistance(
                    posReport.Latitude, posReport.Longitude,
                    destination.Latitude, destination.Longitude);

                // Calculate remaining flight time
                var flightTimeHours = _haversineCalculator.CalculateFlightTimeHours(distanceNM, posReport.GroundSpeedKnots);
                var flightTimeMinutes = (int)Math.Round(flightTimeHours * 60);

                // Calculate fuel consumption and fuel at arrival
                var fuelConsumption = _haversineCalculator.CalculateFuelConsumption(posReport.FuelFlowKgPerHour, flightTimeHours);
                var fuelAtArrival = (int)Math.Round(posReport.FuelOnBoardKg - fuelConsumption);
                var lowFuelWarning = fuelAtArrival < 0;

                context.Logger.LogInformation($"Calculation results: Distance={distanceNM:F1} NM, Time={flightTimeMinutes} min, Fuel={fuelAtArrival} kg, Warning={lowFuelWarning}");

                // Create calculation result
                var calcResult = new CalculationResult
                {
                    FlightId = flightId,
                    Timestamp = DateTime.UtcNow,
                    DistanceNauticalMiles = distanceNM,
                    RemainingFlightTimeMinutes = flightTimeMinutes,
                    EstimatedFuelAtArrivalKg = fuelAtArrival,
                    LowFuelWarning = lowFuelWarning
                };

                // Store result in S3 (use milliseconds for uniqueness)
                var resultKey = $"results/{flightId}-{calcResult.Timestamp:yyyy-MM-ddTHH-mm-ss-fff}.json";
                await _s3Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = resultKey,
                    ContentBody = JsonSerializer.Serialize(calcResult),
                    ContentType = "application/json"
                });

                context.Logger.LogInformation($"Stored result at {resultKey}");

                // Write to DynamoDB CalculationResults
                await _dynamoClient.PutItemAsync(new PutItemRequest
                {
                    TableName = _calculationResultsTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["flightId"] = new AttributeValue { S = flightId },
                        ["timestamp"] = new AttributeValue { S = calcResult.Timestamp.ToString("o") },
                        ["attachment"] = new AttributeValue { S = resultKey }
                    }
                });

                context.Logger.LogInformation($"Wrote calculation result to DynamoDB for flight {flightId}");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error processing message {record.MessageId}: {ex}");
                // Report batch item failure for retry
                batchItemFailures.Add(new SQSBatchResponse.BatchItemFailure
                {
                    ItemIdentifier = record.MessageId
                });
            }
        }

        return new SQSBatchResponse
        {
            BatchItemFailures = batchItemFailures
        };
    }
}
