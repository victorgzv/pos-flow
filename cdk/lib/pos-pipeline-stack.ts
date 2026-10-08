import * as cdk from 'aws-cdk-lib';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as sqs from 'aws-cdk-lib/aws-sqs';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as logs from 'aws-cdk-lib/aws-logs';
import * as apigatewayv2 from 'aws-cdk-lib/aws-apigatewayv2';
import * as apigatewayv2Integrations from 'aws-cdk-lib/aws-apigatewayv2-integrations';
import * as s3n from 'aws-cdk-lib/aws-s3-notifications';
import * as lambdaEventSources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as iam from 'aws-cdk-lib/aws-iam';
import { Construct } from 'constructs';
import * as path from 'path';

export class PosPipelineStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // Local emulator mode (Floci/LocalStack): cdklocal deploy -c local=true
    // Skips CDK custom resources that can't reach S3 inside the emulator.
    const isLocal = this.node.tryGetContext('local') === 'true' || this.node.tryGetContext('local') === true;

    // ==================== S3 Bucket ====================
    const reportsBucket = new s3.Bucket(this, 'ReportsBucket', {
      bucketName: undefined, // Let CDK generate unique name
      removalPolicy: cdk.RemovalPolicy.DESTROY, // For dev/test; use RETAIN in prod
      autoDeleteObjects: !isLocal, // For dev/test (disabled on local emulator)
      encryption: s3.BucketEncryption.S3_MANAGED,
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      versioned: false,
    });

    // ==================== DynamoDB Tables ====================
    const parsedRecordsTable = new dynamodb.Table(this, 'ParsedRecords', {
      tableName: 'ParsedRecords',
      partitionKey: { name: 'flightId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'timestamp', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY, // For dev/test
      pointInTimeRecovery: false, // Enable in prod
    });

    const calculationResultsTable = new dynamodb.Table(this, 'CalculationResults', {
      tableName: 'CalculationResults',
      partitionKey: { name: 'flightId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'timestamp', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY, // For dev/test
      pointInTimeRecovery: false,
    });

    // ==================== SQS Queues ====================
    const processDlq = new sqs.Queue(this, 'ProcessDLQ', {
      queueName: 'pos-pipeline-process-dlq',
      retentionPeriod: cdk.Duration.days(14),
      encryption: sqs.QueueEncryption.SQS_MANAGED,
    });

    const processQueue = new sqs.Queue(this, 'ProcessQueue', {
      queueName: 'pos-pipeline-process-queue',
      visibilityTimeout: cdk.Duration.seconds(180), // 3x Lambda timeout
      receiveMessageWaitTime: cdk.Duration.seconds(20), // Long polling
      deadLetterQueue: {
        queue: processDlq,
        maxReceiveCount: 3,
      },
      encryption: sqs.QueueEncryption.SQS_MANAGED,
    });

    // ==================== Lambda Functions ====================
    // Path to the published Lambda packages (will be built from src/)
    const lambdaCodePath = path.join(__dirname, '../../src');

    // Ingest Lambda - log group and role
    const ingestLogGroup = new logs.LogGroup(this, 'IngestLogGroup', {
      logGroupName: '/aws/lambda/pos-pipeline-ingest',
      retention: logs.RetentionDays.TWO_WEEKS,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    const ingestRole = new iam.Role(this, 'IngestRole', {
      assumedBy: new iam.ServicePrincipal('lambda.amazonaws.com'),
    });

    ingestRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['s3:PutObject'],
      resources: [reportsBucket.arnForObjects('pos/*')],
    }));

    ingestRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['logs:CreateLogStream', 'logs:PutLogEvents'],
      resources: [ingestLogGroup.logGroupArn],
    }));

    const ingestFunction = new lambda.Function(this, 'IngestFunction', {
      functionName: 'pos-pipeline-ingest',
      runtime: lambda.Runtime.DOTNET_8,
      handler: 'PosPipeline.Ingest::PosPipeline.Ingest.Function::FunctionHandler',
      role: ingestRole,
      logGroup: ingestLogGroup,
      code: lambda.Code.fromAsset(lambdaCodePath, {
        bundling: {
          image: lambda.Runtime.DOTNET_8.bundlingImage,
          command: [
            '/bin/sh',
            '-c',
            [
              'cd /asset-input',
              'dotnet restore PosPipeline.sln',
              'dotnet build PosPipeline.Ingest/PosPipeline.Ingest.csproj --configuration Release',
              'dotnet publish PosPipeline.Ingest/PosPipeline.Ingest.csproj --configuration Release --runtime linux-x64 --self-contained false --output /asset-output',
            ].join(' && '),
          ],
        },
      }),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      environment: {
        BUCKET_NAME: reportsBucket.bucketName,
      },
    });

    // Parser Lambda - log group and role
    const parserLogGroup = new logs.LogGroup(this, 'ParserLogGroup', {
      logGroupName: '/aws/lambda/pos-pipeline-parser',
      retention: logs.RetentionDays.TWO_WEEKS,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    const parserRole = new iam.Role(this, 'ParserRole', {
      assumedBy: new iam.ServicePrincipal('lambda.amazonaws.com'),
    });

    parserRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['s3:GetObject'],
      resources: [reportsBucket.arnForObjects('pos/*')],
    }));

    parserRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['s3:PutObject'],
      resources: [reportsBucket.arnForObjects('attachment/*')],
    }));

    parserRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['dynamodb:PutItem'],
      resources: [parsedRecordsTable.tableArn],
    }));

    parserRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['sqs:SendMessage'],
      resources: [processQueue.queueArn],
    }));

    parserRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['logs:CreateLogStream', 'logs:PutLogEvents'],
      resources: [parserLogGroup.logGroupArn],
    }));

    const parserFunction = new lambda.Function(this, 'ParserFunction', {
      functionName: 'pos-pipeline-parser',
      runtime: lambda.Runtime.DOTNET_8,
      handler: 'PosPipeline.Parser::PosPipeline.Parser.Function::FunctionHandler',
      role: parserRole,
      logGroup: parserLogGroup,
      code: lambda.Code.fromAsset(lambdaCodePath, {
        bundling: {
          image: lambda.Runtime.DOTNET_8.bundlingImage,
          command: [
            '/bin/sh',
            '-c',
            [
              'cd /asset-input',
              'dotnet restore PosPipeline.sln',
              'dotnet build PosPipeline.Parser/PosPipeline.Parser.csproj --configuration Release',
              'dotnet publish PosPipeline.Parser/PosPipeline.Parser.csproj --configuration Release --runtime linux-x64 --self-contained false --output /asset-output',
            ].join(' && '),
          ],
        },
      }),
      timeout: cdk.Duration.seconds(60),
      memorySize: 512,
      environment: {
        BUCKET_NAME: reportsBucket.bucketName,
        PARSED_RECORDS_TABLE: parsedRecordsTable.tableName,
        PROCESS_QUEUE_URL: processQueue.queueUrl,
      },
    });

    // S3 Event notification: ObjectCreated on pos/* prefix -> Parser Lambda
    reportsBucket.addEventNotification(
      s3.EventType.OBJECT_CREATED,
      new s3n.LambdaDestination(parserFunction),
      { prefix: 'pos/' }
    );

    // Calculator Lambda - log group and role
    const calculatorLogGroup = new logs.LogGroup(this, 'CalculatorLogGroup', {
      logGroupName: '/aws/lambda/pos-pipeline-calculator',
      retention: logs.RetentionDays.TWO_WEEKS,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    const calculatorRole = new iam.Role(this, 'CalculatorRole', {
      assumedBy: new iam.ServicePrincipal('lambda.amazonaws.com'),
    });

    calculatorRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['dynamodb:Query'],
      resources: [parsedRecordsTable.tableArn],
    }));

    calculatorRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['s3:GetObject'],
      resources: [reportsBucket.arnForObjects('attachment/*')],
    }));

    calculatorRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['s3:PutObject'],
      resources: [reportsBucket.arnForObjects('results/*')],
    }));

    calculatorRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['dynamodb:PutItem'],
      resources: [calculationResultsTable.tableArn],
    }));

    calculatorRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['sqs:ReceiveMessage', 'sqs:DeleteMessage', 'sqs:GetQueueAttributes'],
      resources: [processQueue.queueArn],
    }));

    calculatorRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['logs:CreateLogStream', 'logs:PutLogEvents'],
      resources: [calculatorLogGroup.logGroupArn],
    }));

    const calculatorFunction = new lambda.Function(this, 'CalculatorFunction', {
      functionName: 'pos-pipeline-calculator',
      runtime: lambda.Runtime.DOTNET_8,
      handler: 'PosPipeline.Calculator::PosPipeline.Calculator.Function::FunctionHandler',
      role: calculatorRole,
      logGroup: calculatorLogGroup,
      code: lambda.Code.fromAsset(lambdaCodePath, {
        bundling: {
          image: lambda.Runtime.DOTNET_8.bundlingImage,
          command: [
            '/bin/sh',
            '-c',
            [
              'cd /asset-input',
              'dotnet restore PosPipeline.sln',
              'dotnet build PosPipeline.Calculator/PosPipeline.Calculator.csproj --configuration Release',
              'dotnet publish PosPipeline.Calculator/PosPipeline.Calculator.csproj --configuration Release --runtime linux-x64 --self-contained false --output /asset-output',
            ].join(' && '),
          ],
        },
      }),
      timeout: cdk.Duration.seconds(60),
      memorySize: 512,
      environment: {
        BUCKET_NAME: reportsBucket.bucketName,
        PARSED_RECORDS_TABLE: parsedRecordsTable.tableName,
        CALCULATION_RESULTS_TABLE: calculationResultsTable.tableName,
      },
    });

    // SQS event source for Calculator Lambda
    calculatorFunction.addEventSource(
      new lambdaEventSources.SqsEventSource(processQueue, {
        batchSize: 10,
        maxBatchingWindow: cdk.Duration.seconds(5),
        reportBatchItemFailures: true,
      })
    );

    // Status Lambda - log group and role
    const statusLogGroup = new logs.LogGroup(this, 'StatusLogGroup', {
      logGroupName: '/aws/lambda/pos-pipeline-status',
      retention: logs.RetentionDays.TWO_WEEKS,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    const statusRole = new iam.Role(this, 'StatusRole', {
      assumedBy: new iam.ServicePrincipal('lambda.amazonaws.com'),
    });

    statusRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['dynamodb:Query'],
      resources: [parsedRecordsTable.tableArn, calculationResultsTable.tableArn],
    }));

    statusRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['s3:GetObject'],
      resources: [reportsBucket.arnForObjects('results/*')],
    }));

    statusRole.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: ['logs:CreateLogStream', 'logs:PutLogEvents'],
      resources: [statusLogGroup.logGroupArn],
    }));

    const statusFunction = new lambda.Function(this, 'StatusFunction', {
      functionName: 'pos-pipeline-status',
      runtime: lambda.Runtime.DOTNET_8,
      handler: 'PosPipeline.Status::PosPipeline.Status.Function::FunctionHandler',
      role: statusRole,
      logGroup: statusLogGroup,
      code: lambda.Code.fromAsset(lambdaCodePath, {
        bundling: {
          image: lambda.Runtime.DOTNET_8.bundlingImage,
          command: [
            '/bin/sh',
            '-c',
            [
              'cd /asset-input',
              'dotnet restore PosPipeline.sln',
              'dotnet build PosPipeline.Status/PosPipeline.Status.csproj --configuration Release',
              'dotnet publish PosPipeline.Status/PosPipeline.Status.csproj --configuration Release --runtime linux-x64 --self-contained false --output /asset-output',
            ].join(' && '),
          ],
        },
      }),
      timeout: cdk.Duration.seconds(30),
      memorySize: 512,
      environment: {
        BUCKET_NAME: reportsBucket.bucketName,
        PARSED_RECORDS_TABLE: parsedRecordsTable.tableName,
        CALCULATION_RESULTS_TABLE: calculationResultsTable.tableName,
      },
    });

    // ==================== API Gateway HTTP API ====================
    const httpApi = new apigatewayv2.HttpApi(this, 'PosReportsApi', {
      apiName: 'pos-reports-api',
      description: 'API for POS report ingestion and status queries',
      corsPreflight: {
        allowHeaders: ['Content-Type', 'Authorization'],
        allowMethods: [
          apigatewayv2.CorsHttpMethod.GET,
          apigatewayv2.CorsHttpMethod.POST,
        ],
        allowOrigins: ['*'], // Restrict in production
        maxAge: cdk.Duration.days(1),
      },
    });

    // POST /pos-reports -> Ingest Lambda
    httpApi.addRoutes({
      path: '/pos-reports',
      methods: [apigatewayv2.HttpMethod.POST],
      integration: new apigatewayv2Integrations.HttpLambdaIntegration(
        'IngestIntegration',
        ingestFunction
      ),
    });

    // GET /status/{flightId} -> Status Lambda
    httpApi.addRoutes({
      path: '/status/{flightId}',
      methods: [apigatewayv2.HttpMethod.GET],
      integration: new apigatewayv2Integrations.HttpLambdaIntegration(
        'StatusIntegration',
        statusFunction
      ),
    });

    // ==================== Stack Outputs ====================
    new cdk.CfnOutput(this, 'ApiUrl', {
      value: httpApi.apiEndpoint,
      description: 'HTTP API Gateway endpoint URL',
      exportName: 'PosReportsApiUrl',
    });

    new cdk.CfnOutput(this, 'BucketName', {
      value: reportsBucket.bucketName,
      description: 'S3 bucket for POS reports',
      exportName: 'PosReportsBucket',
    });

    new cdk.CfnOutput(this, 'ParsedRecordsTableName', {
      value: parsedRecordsTable.tableName,
      description: 'DynamoDB table for parsed records',
      exportName: 'ParsedRecordsTable',
    });

    new cdk.CfnOutput(this, 'CalculationResultsTableName', {
      value: calculationResultsTable.tableName,
      description: 'DynamoDB table for calculation results',
      exportName: 'CalculationResultsTable',
    });

    new cdk.CfnOutput(this, 'ProcessQueueUrl', {
      value: processQueue.queueUrl,
      description: 'SQS process queue URL',
      exportName: 'ProcessQueueUrl',
    });

    new cdk.CfnOutput(this, 'ProcessDlqUrl', {
      value: processDlq.queueUrl,
      description: 'SQS dead letter queue URL',
      exportName: 'ProcessDlqUrl',
    });
  }
}
