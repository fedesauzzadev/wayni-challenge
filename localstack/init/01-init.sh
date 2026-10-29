#!/bin/bash
set -euo pipefail

bucket="${S3_BUCKET:-bcra-local}"
queue="import-completed"

echo "[init] creating S3 bucket '${bucket}'"
awslocal s3api create-bucket --bucket "${bucket}" 2>/dev/null \
  || echo "[init] bucket '${bucket}' already exists"

echo "[init] creating SQS queue '${queue}'"
awslocal sqs create-queue --queue-name "${queue}" \
  || echo "[init] queue '${queue}' already exists"

echo "[init] buckets:"
awslocal s3 ls
echo "[init] queues:"
awslocal sqs list-queues
