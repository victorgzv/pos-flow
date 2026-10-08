using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;
using PosPipeline.Shared.Models;
using PosPipeline.Shared.Services;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace PosPipeline.Ingest;

public class Function
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly PosParser _posParser;
    private readonly FlightIdBuilder _flightIdBuilder;

    public Function()
    {
        _s3Client = new AmazonS3Client();
        _bucketName = Environment.GetEnvironmentVariable("BUCKET_NAME") ?? throw new InvalidOperationException("BUCKET_NAME not set");
        _posParser = new PosParser();
        _flightIdBuilder = new FlightIdBuilder();
    }

    /// <summary>
    /// Lambda handler for POST /pos-reports
    /// Validates POS report, generates flightId, stores in S3 pos/ prefix, returns 202 RECEIVED
    /// </summary>
    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        try
        {
            context.Logger.LogInformation($"Processing POS report ingestion request");

            // Get raw POS message from request body
            var rawMessage = request.Body ?? string.Empty;

            // Basic validation
            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                return CreateResponse(400, new { error = "Request body cannot be empty" });
            }

            if (!rawMessage.StartsWith("POS/", StringComparison.OrdinalIgnoreCase))
            {
                return CreateResponse(400, new { error = "Message must start with 'POS/'" });
            }

            var parts = rawMessage.Split('/');
            if (parts.Length < 8)
            {
                return CreateResponse(400, new { error = "Invalid POS message format - insufficient parts" });
            }

            var requestTimestamp = DateTime.UtcNow;

            // Parse message to extract flightId and validate structure
            var posReport = _posParser.Parse(rawMessage, requestTimestamp);

            // Store raw message in S3 pos/ prefix
            var s3Key = $"pos/{posReport.FlightId}-{requestTimestamp:yyyy-MM-ddTHH-mm-ss}";
            await _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = s3Key,
                ContentBody = rawMessage,
                ContentType = "text/plain"
            });

            context.Logger.LogInformation($"Stored POS report at s3://{_bucketName}/{s3Key}");

            // Return 202 Accepted with flight ID
            var response = new IngestResponse
            {
                FlightId = posReport.FlightId,
                Status = "RECEIVED",
                Timestamp = requestTimestamp
            };

            return CreateResponse(202, response);
        }
        catch (ArgumentException ex)
        {
            context.Logger.LogError($"Validation error: {ex.Message}");
            return CreateResponse(400, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error processing POS report: {ex}");
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
