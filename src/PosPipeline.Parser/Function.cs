using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using System.Text.Json;
using PosPipeline.Shared.Models;
using PosPipeline.Shared.Services;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace PosPipeline.Parser;

public class Function
{
    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonDynamoDB _dynamoClient;
    private readonly IAmazonSQS _sqsClient;
    private readonly string _bucketName;
    private readonly string _parsedRecordsTable;
    private readonly string _processQueueUrl;
    private readonly PosParser _posParser;

    public Function()
    {
        _s3Client = new AmazonS3Client();
        _dynamoClient = new AmazonDynamoDBClient();
        _sqsClient = new AmazonSQSClient();
        _bucketName = Environment.GetEnvironmentVariable("BUCKET_NAME") ?? throw new InvalidOperationException("BUCKET_NAME not set");
        _parsedRecordsTable = Environment.GetEnvironmentVariable("PARSED_RECORDS_TABLE") ?? throw new InvalidOperationException("PARSED_RECORDS_TABLE not set");
        _processQueueUrl = Environment.GetEnvironmentVariable("PROCESS_QUEUE_URL") ?? throw new InvalidOperationException("PROCESS_QUEUE_URL not set");
        _posParser = new PosParser();
    }

    /// <summary>
    /// Lambda handler for S3 ObjectCreated events on pos/ prefix
    /// Parses POS report, stores attachment JSON, writes to DynamoDB, sends SQS message
    /// </summary>
    public async Task FunctionHandler(S3Event s3Event, ILambdaContext context)
    {
        foreach (var record in s3Event.Records)
        {
            try
            {
                // URL-decode the S3 key since colons may arrive encoded
                var s3Key = System.Net.WebUtility.UrlDecode(record.S3.Object.Key);
                context.Logger.LogInformation($"Processing S3 object: {s3Key}");

                // Get raw POS message from S3 with metadata
                var getResponse = await _s3Client.GetObjectAsync(_bucketName, s3Key);
                string rawMessage;
                using (var reader = new StreamReader(getResponse.ResponseStream))
                {
                    rawMessage = await reader.ReadToEndAsync();
                }

                // Extract flightId and ingest timestamp from S3 object metadata
                // This ensures we use the ingest time (when POST was received) rather than S3 event time
                var flightId = getResponse.Metadata["x-amz-meta-flightid"];
                var ingestTimestampStr = getResponse.Metadata["x-amz-meta-ingest-timestamp"];
                var ingestTimestamp = DateTime.Parse(ingestTimestampStr).ToUniversalTime();

                // Parse POS message using the ingest timestamp to ensure consistent flightId
                var posReport = _posParser.Parse(rawMessage, ingestTimestamp);

                context.Logger.LogInformation($"Parsed POS report for flight {posReport.FlightId}");

                // Store parsed JSON as attachment with ISO-8601 timestamp (POS timestamp, no milliseconds)
                var attachmentKey = $"attachment/{posReport.FlightId}-{posReport.Timestamp:yyyy-MM-ddTHH:mm:ssZ}.json";
                await _s3Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = attachmentKey,
                    ContentBody = JsonSerializer.Serialize(posReport),
                    ContentType = "application/json"
                });

                context.Logger.LogInformation($"Stored attachment at {attachmentKey}");

                // Write to DynamoDB ParsedRecords
                await _dynamoClient.PutItemAsync(new PutItemRequest
                {
                    TableName = _parsedRecordsTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["flightId"] = new AttributeValue { S = posReport.FlightId },
                        ["timestamp"] = new AttributeValue { S = posReport.Timestamp.ToString("o") },
                        ["attachment"] = new AttributeValue { S = attachmentKey },
                        ["departure"] = new AttributeValue { S = posReport.Departure },
                        ["destination"] = new AttributeValue { S = posReport.Destination }
                    }
                });

                context.Logger.LogInformation($"Wrote to DynamoDB table {_parsedRecordsTable}");

                // Send message to SQS for calculation
                var sqsMessage = JsonSerializer.Serialize(new
                {
                    flightId = posReport.FlightId,
                    attachmentKey = attachmentKey,
                    timestamp = posReport.Timestamp.ToString("o")
                });

                await _sqsClient.SendMessageAsync(new SendMessageRequest
                {
                    QueueUrl = _processQueueUrl,
                    MessageBody = sqsMessage
                });

                context.Logger.LogInformation($"Sent message to SQS queue for flight {posReport.FlightId}");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error processing S3 event for key {record.S3.Object.Key}: {ex}");
                throw; // Allow S3 to retry
            }
        }
    }
}
