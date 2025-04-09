# Cosmos DB vCore Autoscaler

This project is an Azure Function App designed to dynamically scale the tier of a Cosmos DB MongoDB vCore cluster. It provides an HTTP-triggered function to scale the cluster up or down based on the specified direction.

## Features
- Automatically determines the next SKU tier for scaling up or down.
- Uses Azure CLI commands to fetch the current SKU and update the cluster's SKU.
- Supports both query string and JSON body inputs for flexibility.
- Logs detailed information for debugging and monitoring.

## Prerequisites
- .NET 6 SDK
- Azure CLI installed and configured
- An Azure Cosmos DB MongoDB cluster

## Configuration
The project uses the following configuration:
- **Azure Functions Version**: v4
- **Target Framework**: .NET 6
- **Logging**: Integrated with Application Insights (configured in `host.json`).

## How to Use
1. Deploy the Azure Function to your Azure subscription.
2. Trigger the function via HTTP GET or POST requests.

### HTTP Request Format
#### Query String Parameters:
- `resourceGroup`: The name of the resource group containing the Cosmos DB cluster.
- `mongoCluster`: The name of the MongoDB cluster.
- `direction`: The scaling direction (`up` or `down`).

#### JSON Body (alternative to query string):
{ "resourceGroup": "your-resource-group", "mongoCluster": "your-mongo-cluster", "direction": "up" }

### Example Request
#### GET Request:
GET https://<your-function-app>.azurewebsites.net/api/ScaleFunction?resourceGroup=myResourceGroup&mongoCluster=myCluster&direction=up

#### POST Request:
POST https://<your-function-app>.azurewebsites.net/api/ScaleFunction Content-Type: application/json
{ "resourceGroup": "myResourceGroup", "mongoCluster": "myCluster", "direction": "down" }

## How It Works
1. The function reads the input parameters from the query string or JSON body.
2. It fetches the current SKU of the MongoDB cluster using the Azure CLI.
3. Determines the next SKU based on the scaling direction (`up` or `down`).
4. Updates the cluster's SKU using the Azure CLI.

## Logging
The function logs detailed information, including:
- Input parameters
- Current SKU
- Next SKU
- Azure CLI command outputs

Logs are integrated with Application Insights for monitoring.

## Error Handling
- Returns `400 Bad Request` for missing or invalid parameters.
- Returns `500 Internal Server Error` for unexpected errors during execution.

## Dependencies
- `Microsoft.NET.Sdk.Functions` (v4.1.1)

## License
This project is licensed under the MIT License.
