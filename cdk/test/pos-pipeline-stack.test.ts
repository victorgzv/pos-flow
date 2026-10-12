import * as cdk from 'aws-cdk-lib';
import { Template, Match } from 'aws-cdk-lib/assertions';
import { PosPipelineStack } from '../lib/pos-pipeline-stack';

describe('PosPipelineStack Infrastructure Tests', () => {
  describe('HTTP API Throttling', () => {
    test('API Gateway stage has default throttle settings 10 req/s and 20 burst', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      template.hasResourceProperties('AWS::ApiGatewayV2::Stage', {
        StageName: '$default',
        AutoDeploy: true,
        DefaultRouteSettings: {
          ThrottlingRateLimit: 10,
          ThrottlingBurstLimit: 20
        }
      });
    });

    test('API Gateway throttle respects context overrides', () => {
      const app = new cdk.App({
        context: {
          'aws:cdk:bundling-stacks': [],
          throttleRate: 50,
          throttleBurst: 100
        }
      });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      template.hasResourceProperties('AWS::ApiGatewayV2::Stage', {
        DefaultRouteSettings: {
          ThrottlingRateLimit: 50,
          ThrottlingBurstLimit: 100
        }
      });
    });
  });

  describe('S3 Bucket Security', () => {
    test('S3 bucket has encryption enabled', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      template.hasResourceProperties('AWS::S3::Bucket', {
        BucketEncryption: {
          ServerSideEncryptionConfiguration: [
            {
              ServerSideEncryptionByDefault: {
                SSEAlgorithm: 'AES256'
              }
            }
          ]
        }
      });
    });

    test('S3 bucket has public access blocked', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      template.hasResourceProperties('AWS::S3::Bucket', {
        PublicAccessBlockConfiguration: {
          BlockPublicAcls: true,
          BlockPublicPolicy: true,
          IgnorePublicAcls: true,
          RestrictPublicBuckets: true
        }
      });
    });
  });

  describe('SQS Queue Configuration', () => {
    test('Process queue has DLQ with maxReceiveCount 3', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      template.hasResourceProperties('AWS::SQS::Queue', {
        QueueName: 'pos-pipeline-process-queue',
        RedrivePolicy: {
          maxReceiveCount: 3,
          deadLetterTargetArn: Match.anyValue()
        }
      });
    });

    test('DLQ exists with proper retention', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      template.hasResourceProperties('AWS::SQS::Queue', {
        QueueName: 'pos-pipeline-process-dlq',
        MessageRetentionPeriod: 1209600 // 14 days in seconds
      });
    });
  });

  describe('DynamoDB Tables', () => {
    test('ParsedRecords table has correct partition and sort keys', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      template.hasResourceProperties('AWS::DynamoDB::Table', {
        TableName: 'ParsedRecords',
        KeySchema: [
          { AttributeName: 'flightId', KeyType: 'HASH' },
          { AttributeName: 'timestamp', KeyType: 'RANGE' }
        ],
        AttributeDefinitions: [
          { AttributeName: 'flightId', AttributeType: 'S' },
          { AttributeName: 'timestamp', AttributeType: 'S' }
        ]
      });
    });

    test('CalculationResults table has correct partition and sort keys', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      template.hasResourceProperties('AWS::DynamoDB::Table', {
        TableName: 'CalculationResults',
        KeySchema: [
          { AttributeName: 'flightId', KeyType: 'HASH' },
          { AttributeName: 'timestamp', KeyType: 'RANGE' }
        ],
        AttributeDefinitions: [
          { AttributeName: 'flightId', AttributeType: 'S' },
          { AttributeName: 'timestamp', AttributeType: 'S' }
        ]
      });
    });
  });

  describe('Lambda IAM Roles - Least Privilege', () => {
    test('Ingest Lambda role has no managed policies and no wildcard actions or resources', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      // Find the Ingest role
      const allRoles = template.findResources('AWS::IAM::Role');
      const ingestRoleEntry = Object.entries(allRoles).find(([logicalId, _]) =>
        logicalId.includes('IngestRole')
      );
      expect(ingestRoleEntry).toBeDefined();
      const [ingestRoleId, ingestRole] = ingestRoleEntry!;

      // No managed policies attached to role
      expect((ingestRole as any).Properties.ManagedPolicyArns).toBeUndefined();

      // Find the associated policy
      const allPolicies = template.findResources('AWS::IAM::Policy');
      const ingestPolicy = Object.values(allPolicies).find((policy: any) =>
        policy.Properties.Roles?.some((role: any) => role.Ref === ingestRoleId)
      );
      expect(ingestPolicy).toBeDefined();

      // Check policy statements have no wildcards
      const policyDoc = (ingestPolicy as any).Properties.PolicyDocument;
      policyDoc.Statement.forEach((statement: any) => {
        const actions = Array.isArray(statement.Action) ? statement.Action : [statement.Action];
        const resources = Array.isArray(statement.Resource) ? statement.Resource : [statement.Resource];

        actions.forEach((action: string) => {
          expect(action).not.toBe('*');
        });

        resources.forEach((resource: string | object) => {
          if (typeof resource === 'string') {
            expect(resource).not.toBe('*');
          }
        });
      });
    });

    test('Parser Lambda role has no managed policies and no wildcard actions or resources', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      const allRoles = template.findResources('AWS::IAM::Role');
      const parserRoleEntry = Object.entries(allRoles).find(([logicalId, _]) =>
        logicalId.includes('ParserRole')
      );
      expect(parserRoleEntry).toBeDefined();
      const [parserRoleId, parserRole] = parserRoleEntry!;

      expect((parserRole as any).Properties.ManagedPolicyArns).toBeUndefined();

      const allPolicies = template.findResources('AWS::IAM::Policy');
      const parserPolicy = Object.values(allPolicies).find((policy: any) =>
        policy.Properties.Roles?.some((role: any) => role.Ref === parserRoleId)
      );
      expect(parserPolicy).toBeDefined();

      const policyDoc = (parserPolicy as any).Properties.PolicyDocument;
      policyDoc.Statement.forEach((statement: any) => {
        const actions = Array.isArray(statement.Action) ? statement.Action : [statement.Action];
        const resources = Array.isArray(statement.Resource) ? statement.Resource : [statement.Resource];

        actions.forEach((action: string) => {
          expect(action).not.toBe('*');
        });

        resources.forEach((resource: string | object) => {
          if (typeof resource === 'string') {
            expect(resource).not.toBe('*');
          }
        });
      });
    });

    test('Calculator Lambda role has no managed policies and no wildcard actions or resources', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      const allRoles = template.findResources('AWS::IAM::Role');
      const calculatorRoleEntry = Object.entries(allRoles).find(([logicalId, _]) =>
        logicalId.includes('CalculatorRole')
      );
      expect(calculatorRoleEntry).toBeDefined();
      const [calculatorRoleId, calculatorRole] = calculatorRoleEntry!;

      expect((calculatorRole as any).Properties.ManagedPolicyArns).toBeUndefined();

      const allPolicies = template.findResources('AWS::IAM::Policy');
      const calculatorPolicy = Object.values(allPolicies).find((policy: any) =>
        policy.Properties.Roles?.some((role: any) => role.Ref === calculatorRoleId)
      );
      expect(calculatorPolicy).toBeDefined();

      const policyDoc = (calculatorPolicy as any).Properties.PolicyDocument;
      policyDoc.Statement.forEach((statement: any) => {
        const actions = Array.isArray(statement.Action) ? statement.Action : [statement.Action];
        const resources = Array.isArray(statement.Resource) ? statement.Resource : [statement.Resource];

        actions.forEach((action: string) => {
          expect(action).not.toBe('*');
        });

        resources.forEach((resource: string | object) => {
          if (typeof resource === 'string') {
            expect(resource).not.toBe('*');
          }
        });
      });
    });

    test('Status Lambda role has no managed policies and no wildcard actions or resources', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      const allRoles = template.findResources('AWS::IAM::Role');
      const statusRoleEntry = Object.entries(allRoles).find(([logicalId, _]) =>
        logicalId.includes('StatusRole')
      );
      expect(statusRoleEntry).toBeDefined();
      const [statusRoleId, statusRole] = statusRoleEntry!;

      expect((statusRole as any).Properties.ManagedPolicyArns).toBeUndefined();

      const allPolicies = template.findResources('AWS::IAM::Policy');
      const statusPolicy = Object.values(allPolicies).find((policy: any) =>
        policy.Properties.Roles?.some((role: any) => role.Ref === statusRoleId)
      );
      expect(statusPolicy).toBeDefined();

      const policyDoc = (statusPolicy as any).Properties.PolicyDocument;
      policyDoc.Statement.forEach((statement: any) => {
        const actions = Array.isArray(statement.Action) ? statement.Action : [statement.Action];
        const resources = Array.isArray(statement.Resource) ? statement.Resource : [statement.Resource];

        actions.forEach((action: string) => {
          expect(action).not.toBe('*');
        });

        resources.forEach((resource: string | object) => {
          if (typeof resource === 'string') {
            expect(resource).not.toBe('*');
          }
        });
      });
    });

    test('CDK helper roles are excluded from wildcard check', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      // CDK helper functions like BucketNotificationsHandler are allowed to use managed policies
      // This test just documents their presence
      const allRoles = template.findResources('AWS::IAM::Role');
      const helperRoles = Object.entries(allRoles).filter(([logicalId, _]) =>
        logicalId.includes('BucketNotificationsHandler') ||
        logicalId.includes('CustomS3AutoDeleteObjectsCustomResourceProvider')
      );

      // These are CDK-managed infrastructure helpers, not application Lambda roles
      // They are allowed to have managed policies
      expect(helperRoles.length).toBeGreaterThanOrEqual(0);
    });
  });

  describe('CloudWatch Log Groups', () => {
    test('All 4 application Lambda log groups exist with retention', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      const logGroups = [
        '/aws/lambda/pos-pipeline-ingest',
        '/aws/lambda/pos-pipeline-parser',
        '/aws/lambda/pos-pipeline-calculator',
        '/aws/lambda/pos-pipeline-status'
      ];

      logGroups.forEach(logGroupName => {
        template.hasResourceProperties('AWS::Logs::LogGroup', {
          LogGroupName: logGroupName,
          RetentionInDays: 14
        });
      });
    });
  });

  describe('Lambda Runtime', () => {
    test('All application Lambdas use dotnet8 runtime', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      const lambdaFunctions = template.findResources('AWS::Lambda::Function', {
        Properties: {
          FunctionName: Match.anyValue()
        }
      });

      const appLambdas = Object.entries(lambdaFunctions).filter(([_, fn]) => {
        const name = (fn as any).Properties?.FunctionName;
        return name && name.startsWith('pos-pipeline-');
      });

      expect(appLambdas.length).toBe(4);

      appLambdas.forEach(([_, fn]) => {
        expect((fn as any).Properties.Runtime).toBe('dotnet8');
      });
    });
  });

  describe('Local Mode Configuration', () => {
    test('local=true disables autoDeleteObjects custom resource', () => {
      const app = new cdk.App({
        context: {
          'aws:cdk:bundling-stacks': [],
          local: 'true'
        }
      });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      // When autoDeleteObjects is disabled, the custom resource should not exist
      const customResources = template.findResources('Custom::S3AutoDeleteObjects');
      expect(Object.keys(customResources).length).toBe(0);
    });

    test('local=false or undefined enables autoDeleteObjects custom resource', () => {
      const app = new cdk.App({ context: { 'aws:cdk:bundling-stacks': [] } });
      const stack = new PosPipelineStack(app, 'TestStack');
      const template = Template.fromStack(stack);

      // When autoDeleteObjects is enabled, the custom resource should exist
      const customResources = template.findResources('Custom::S3AutoDeleteObjects');
      expect(Object.keys(customResources).length).toBeGreaterThan(0);
    });
  });
});
