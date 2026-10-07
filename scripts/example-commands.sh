#!/bin/bash
# Example curl commands for POS report pipeline API
# Replace API_URL with your actual API Gateway endpoint after deployment

API_URL="https://your-api-id.execute-api.region.amazonaws.com"

# 1. Submit a POS report
echo "=== Submitting POS report ==="
RESPONSE=$(curl -X POST "$API_URL/pos-reports" \
  -H "Content-Type: text/plain" \
  -d "POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800" \
  -w "\n%{http_code}")

echo "$RESPONSE"

# Extract flightId from response (assumes jq is installed)
FLIGHT_ID=$(echo "$RESPONSE" | grep -o '"flightId":"[^"]*"' | cut -d'"' -f4)
echo "Flight ID: $FLIGHT_ID"

# 2. Query status immediately (should be RECEIVED or PROCESSING)
echo -e "\n=== Checking status immediately ==="
curl -X GET "$API_URL/status/$FLIGHT_ID" | jq '.'

# 3. Wait a few seconds for processing
echo -e "\n=== Waiting 5 seconds for processing ==="
sleep 5

# 4. Query status again (should be COMPLETE if pipeline succeeded)
echo -e "\n=== Checking status after processing ==="
curl -X GET "$API_URL/status/$FLIGHT_ID" | jq '.'

# Example with explicit flight ID
echo -e "\n=== Example: Query specific flight ==="
curl -X GET "$API_URL/status/UL20420260904RGNBKK" | jq '.'

# Invalid request examples
echo -e "\n=== Example: Invalid POS format (400 error) ==="
curl -X POST "$API_URL/pos-reports" \
  -H "Content-Type: text/plain" \
  -d "INVALID MESSAGE"

echo -e "\n=== Example: Unknown flight (404 error) ==="
curl -X GET "$API_URL/status/UNKNOWN12320261001XXXYYY"
