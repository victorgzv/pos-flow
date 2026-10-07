using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Text.Json;
using PosPipeline.Shared.Models;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace PosPipeline.Status;

public class Function
{
    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonDynamoDB _dynamoClient;
    private readonly string _bucketName;
    private readonly string _parsedRecordsTable;
    private readonly string _calculationResultsTable;

    public Function()
    {
        _s3Client = new AmazonS3Client();
        _dynamoClient = new AmazonDynamoDBClient();
        _bucketName = Environment.GetEnvironmentVariable("BUCKET_NAME") ?? throw new InvalidOperationException("BUCKET_NAME not set");
        _parsedRecordsTable = Environment.GetEnvironmentVariable("PARSED_RECORDS_TABLE") ?? throw new InvalidOperationException("PARSED_RECORDS_TABLE not set");
        _calculationResultsTable = Environment.GetEnvironmentVariable("CALCULATION_RESULTS_TABLE") ?? throw new InvalidOperationException("CALCULATION_RESULTS_TABLE not set");
    }

    /// <summary>
    /// Lambda handler for GET /status/{flightId}
    /// Queries DynamoDB for latest parsed/calculated records, returns status
    /// </summary>
    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        try
        {
            // Extract flightId from path parameters
            if (!request.PathParameters.TryGetValue("flightId", out var flightId) || string.IsNullOrEmpty(flightId))
            {
                return CreateResponse(400, new { error = "Missing flightId in path" });
            }

            context.Logger.LogInformation($"Querying status for flight {flightId}");

            // Query CalculationResults table for latest result
            var calcQueryResponse = await _dynamoClient.QueryAsync(new QueryRequest
            {
                TableName = _calculationResultsTable,
                KeyConditionExpression = "flightId = :fid",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":fid"] = new AttributeValue { S = flightId }
                },
                ScanIndexForward = false, // Sort descending by timestamp
                Limit = 1
            });

            // If calculation result exists, return COMPLETE status
            if (calcQueryResponse.Items.Count > 0)
            {
                var calcItem = calcQueryResponse.Items[0];
                var attachmentKey = calcItem["attachment"].S;

                // Get calculation result from S3
                var getResponse = await _s3Client.GetObjectAsync(_bucketName, attachmentKey);
                CalculationResult? calcResult;
                using (var reader = new StreamReader(getResponse.ResponseStream))
                {
                    var json = await reader.ReadToEndAsync();
                    calcResult = JsonSerializer.Deserialize<CalculationResult>(json);
                }

                if (calcResult != null)
                {
                    var response = new StatusResponse
                    {
                        FlightId = flightId,
                        Status = "COMPLETE",
                        Timestamp = calcResult.Timestamp,
                        RemainingFlightTimeMinutes = calcResult.RemainingFlightTimeMinutes,
                        EstimatedFuelAtArrivalKg = calcResult.EstimatedFuelAtArrivalKg,
                        LowFuelWarning = calcResult.LowFuelWarning
                    };

                    return CreateResponse(200, response);
                }
            }

            // Check if parsed record exists (processing in progress)
            var parseQueryResponse = await _dynamoClient.QueryAsync(new QueryRequest
            {
                TableName = _parsedRecordsTable,
                KeyConditionExpression = "flightId = :fid",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":fid"] = new AttributeValue { S = flightId }
                },
                ScanIndexForward = false,
                Limit = 1
            });

            if (parseQueryResponse.Items.Count > 0)
            {
                var parseItem = parseQueryResponse.Items[0];
                var timestamp = DateTime.Parse(parseItem["timestamp"].S);

                var response = new StatusResponse
                {
                    FlightId = flightId,
                    Status = "PROCESSING",
                    Timestamp = timestamp
                };

                return CreateResponse(200, response);
            }

            // No records found
            return CreateResponse(404, new { error = $"No records found for flight {flightId}" });
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error querying status: {ex}");
            return CreateResponse(500, new { error = "Internal server error" });
        }
    }

    private APIGatewayHttpApiV2ProxyResponse CreateResponse(int statusCode, object body)
    {
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = statusCode,
            Body = JsonSerializer.Serialize(body),
            Headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" }
            }
        };
    }
}
