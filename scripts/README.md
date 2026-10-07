# POS Pipeline Scripts

## Sample Data

- `sample-pos.txt` - Gold example POS report message
- `example-commands.sh` - Sample curl commands for testing the API

## Usage

After deploying the CDK stack:

1. Get the API URL from the CDK output:
   ```bash
   cd cdk
   npx cdk deploy --outputs-file outputs.json
   cat outputs.json | jq -r '.PosPipelineStack.ApiUrl'
   ```

2. Update `API_URL` in `example-commands.sh` with your actual endpoint

3. Run the example commands:
   ```bash
   chmod +x example-commands.sh
   ./example-commands.sh
   ```

## Expected Flow

1. **POST /pos-reports** → 202 RECEIVED with flightId
2. Pipeline processes asynchronously (parse → calculate)
3. **GET /status/{flightId}** → 200 with status:
   - PROCESSING (if calculation not complete)
   - COMPLETE (with time/fuel calculations)
   - 404 (if flight never received)

## Gold Example Results

For the sample POS report:
```
POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800
```

Expected results:
- Flight ID: `UL20420260904RGNBKK`
- Latitude: 16.705°
- Longitude: 96.2083°
- Distance to BKK: ~319 NM
- Remaining time: ~43 minutes
- Fuel at arrival: ~10,513 kg
- Low fuel warning: false
