#!/usr/bin/env node
import 'source-map-support/register';
import * as cdk from 'aws-cdk-lib';
import { PosPipelineStack } from '../lib/pos-pipeline-stack';

const app = new cdk.App();

new PosPipelineStack(app, 'PosPipelineStack', {
  description: 'POS report processing pipeline with S3, Lambda, DynamoDB, SQS, and API Gateway',
  env: {
    account: process.env.CDK_DEFAULT_ACCOUNT,
    region: process.env.CDK_DEFAULT_REGION,
  },
});

app.synth();
