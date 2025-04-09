using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

public static class ScaleFunction
{
    private static readonly string[] Skus = { "M10", "M20", "M30", "M40", "M50", "M60", "M80", "M200" };

    [FunctionName("ScaleFunction")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
        ILogger log)
    {
        log.LogInformation("C# HTTP trigger function 'ScaleFunction' to process a request.");

        // Read from query string
        string resourceGroup = req.Query["resourceGroup"];
        string mongoCluster = req.Query["mongoCluster"];
        string direction = req.Query["direction"];

        // If not provided via query string, fallback to body
        if (string.IsNullOrEmpty(resourceGroup) || string.IsNullOrEmpty(mongoCluster) || string.IsNullOrEmpty(direction))
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(requestBody))
            {
                try
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(requestBody);

                    resourceGroup ??= data.GetProperty("resourceGroup").GetString();
                    mongoCluster ??= data.GetProperty("mongoCluster").GetString();
                    direction ??= data.GetProperty("direction").GetString();
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Failed to parse JSON body.");
                    return new BadRequestObjectResult("Invalid JSON body.");
                }
            }
        }

        if (string.IsNullOrEmpty(resourceGroup) || string.IsNullOrEmpty(mongoCluster) || string.IsNullOrEmpty(direction))
            return new BadRequestObjectResult("Missing required parameters: resourceGroup, mongoCluster, or direction.");

        if (direction != "up" && direction != "down")
            return new BadRequestObjectResult("Direction must be either 'up' or 'down'.");

        try
        {
            string currentSku = await GetCurrentSku(resourceGroup, mongoCluster, log);
            if (string.IsNullOrEmpty(currentSku))
                return new BadRequestObjectResult("Failed to fetch or parse current SKU.");

            log.LogInformation($"Current SKU: {currentSku}");

            string nextSku = GetNextSku(currentSku, direction, log);
            if (string.IsNullOrEmpty(nextSku))
                return new OkObjectResult($"Cannot scale {direction} from SKU {currentSku}.");

            log.LogInformation($"Scaling {direction} to SKU: {nextSku}");

            string result = await UpdateSku(resourceGroup, mongoCluster, nextSku, log);
            return new OkObjectResult($"Scaled {direction} from {currentSku} to {nextSku}. CLI output: {result}");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unexpected error while scaling.");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<string> GetCurrentSku(string resourceGroup, string mongoCluster, ILogger log)
    {
        log.LogInformation("Executing GetCurrentSku method.");

        string showCommand = $"az cosmosdb mongocluster show --resource-group {resourceGroup} --cluster-name {mongoCluster} --output json";
        string currentSkuJson = await ExecuteCliCommand(showCommand, log);

        if (string.IsNullOrWhiteSpace(currentSkuJson))
            return null;

        try
        {
            return ExtractCurrentSkuFromJson(currentSkuJson, log);
        }
        catch (JsonException ex)
        {
            log.LogError(ex, "GetCurrentSku - Failed to parse JSON response.");
            return null;
        }
    }

    private static string GetNextSku(string currentSku, string direction, ILogger log)
    {
        log.LogInformation("Executing GetNextSku method.");

        int index = Array.IndexOf(Skus, currentSku);
        if (index == -1)
        {
            log.LogWarning("GetNextSku - Current SKU not recognized.");
            return null;
        }

        return direction.ToLower() switch
        {
            "up" when index < Skus.Length - 1 => Skus[index + 1],
            "down" when index > 0 => Skus[index - 1],
            _ => null
        };
    }

    private static string? ExtractCurrentSkuFromJson(string json, ILogger log)
    {
        log.LogInformation("Executing ExtractCurrentSkuFromJson method.");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Navigate to properties → nodeGroupSpecs[0] → sku
            if (root.TryGetProperty("properties", out var properties) &&
                properties.TryGetProperty("nodeGroupSpecs", out var nodeGroups) &&
                nodeGroups.ValueKind == JsonValueKind.Array &&
                nodeGroups.GetArrayLength() > 0)
            {
                var firstNode = nodeGroups[0];
                if (firstNode.TryGetProperty("sku", out var skuProp))
                {
                    return skuProp.GetString();
                }
            }

            log.LogWarning("ExtractCurrentSkuFromJson - Could not find SKU in JSON.");
            return null;
        }
        catch (JsonException ex)
        {
            log.LogError(ex, "ExtractCurrentSkuFromJson - Failed to parse or navigate JSON.");
            return null;
        }
    }

    private static async Task<string> UpdateSku(string resourceGroup, string mongoCluster, string nextSku, ILogger log)
    {
        log.LogInformation("Executing UpdateSku method.");

        string updateCommand = $"az cosmosdb mongocluster update --resource-group {resourceGroup} --cluster-name {mongoCluster} --shard-node-tier {nextSku}";
        return await ExecuteCliCommand(updateCommand, log);
    }

    private static async Task<string> ExecuteCliCommand(string command, ILogger log)
    {
        log.LogInformation("ExecuteCliCommand started.");

        try
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            log.LogInformation($"System PATH: {pathEnv}");

            // Log the full CLI command before executing
            log.LogInformation($"Running CLI command: {command}");

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };

            process.Start();

            // Read output/error asynchronously
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            process.WaitForExit();

            log.LogInformation("ExecuteCliCommand - CLI STDOUT:");
            log.LogInformation(string.IsNullOrWhiteSpace(output) ? "(no output)" : output);

            if (!string.IsNullOrWhiteSpace(error))
            {
                log.LogWarning("ExecuteCliCommand - CLI STDERR:");
                log.LogWarning(error);
            }

            if (process.ExitCode != 0)
            {
                log.LogError($"ExecuteCliCommand - CLI command failed with exit code {process.ExitCode}.");
                throw new InvalidOperationException($"CLI error: {error}");
            }

            return output;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "ExecuteCliCommand - Failed to execute CLI command.");
            throw;
        }
    }

}