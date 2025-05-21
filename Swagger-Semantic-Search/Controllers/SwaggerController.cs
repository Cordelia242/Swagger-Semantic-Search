using Microsoft.AspNetCore.Mvc;
using Swagger_Semantic_Search.Entities;
using Swagger_Semantic_Search.Services.Database;
using Swagger_Semantic_Search.Services.Embedding;
using Swagger_Semantic_Search.Services.Swagger;

namespace Swagger_Semantic_Search.Controllers;

[ApiController]
[Route("[controller]")]
public class SwaggerController(
    SwaggerService swaggerService,
    IEmbeddingService embeddingService,
    IDatabaseService databaseService
) : ControllerBase
{
    [HttpPost("scan")]
    public async Task<IActionResult> Scan([FromBody] SwaggerControllerScanRequest req)
    {
        string swaggerUiPageUrl = req.SwaggerUiUrl; 

        if (string.IsNullOrWhiteSpace(swaggerUiPageUrl) || !Uri.TryCreate(swaggerUiPageUrl, UriKind.Absolute, out var uriResult) || (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest(new { message = "Invalid Swagger UI URL format. Please provide an absolute HTTP or HTTPS URL." });
        }

        // Get or create the group
        var groupId = await databaseService.GetOrCreateGroupAsync(req.GroupName);

        // Get or create the service. The URL stored for the service is the Swagger UI page URL.
        var serviceId = await databaseService.GetOrCreateServiceAsync(req.ServiceName, swaggerUiPageUrl, groupId);

        List<string> jsonUrls;
        try
        {
            jsonUrls = await swaggerService.DiscoverSwaggerJsonUrlsAsync(swaggerUiPageUrl);
        }
        catch (Exception ex)
        {
            // Log ex: $"Error discovering Swagger JSON URLs from {swaggerUiPageUrl}. Details: {ex.Message}"
            return new ObjectResult(new { message = $"Error discovering Swagger JSON URLs from {swaggerUiPageUrl}. Details: {ex.Message}" })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity
            };
        }

        if (jsonUrls == null || !jsonUrls.Any())
        {
            return Ok(new { message = "No Swagger JSON definitions found on the provided Swagger UI page." });
        }

        List<Document> finalDocumentsToStore = new List<Document>();
        List<string> allDescriptions = new List<string>();
        // Temporary structure to hold info before embeddings are fetched
        List<(string OperationPathKey, string Description, string SourceJsonUrl)> tempOperationDetails = new List<(string, string, string)>();
        // Optional: Collect errors from individual JSON processing
        // List<string> processingErrors = new List<string>();


        foreach (var jsonUrl in jsonUrls)
        {
            OpenApiDocument swaggerData;
            try
            {
                swaggerData = await swaggerService.GetSwaggerDocumentAsync(jsonUrl);
            }
            catch (Exception ex)
            {
                // Log ex: $"Failed to fetch or parse Swagger document from {jsonUrl}. Skipping this URL. Details: {ex.Message}"
                // processingErrors.Add($"Failed to process {jsonUrl}: {ex.Message}");
                continue; // Move to the next jsonUrl
            }

            foreach (var pathItemPair in swaggerData.Paths)
            {
                var pathKey = pathItemPair.Key;
                foreach (var operationPair in pathItemPair.Value.Operations)
                {
                    var operationType = operationPair.Key.ToString().ToUpper();
                    var operation = operationPair.Value;
                    if (!string.IsNullOrEmpty(operation.Description))
                    {
                        tempOperationDetails.Add(($"{operationType} {pathKey}", operation.Description, jsonUrl));
                        allDescriptions.Add(operation.Description);
                    }
                }
            }
        }

        if (!tempOperationDetails.Any())
        {
            // String interpolation fixed here
            return Ok(new { message = $"No operations with descriptions found across all {jsonUrls.Count} discovered Swagger document(s)." });
        }
        
        // Ensure allDescriptions contains unique values if BulkConversion expects unique descriptions, though it usually handles duplicates by key.
        // For safety or if BulkConversion has issues with duplicate keys in its input array (less common):
        // var distinctDescriptions = allDescriptions.Distinct().ToArray();
        // var embeddings = await embeddingService.BulkConversion(distinctDescriptions);
        var embeddings = await embeddingService.BulkConversion(allDescriptions.ToArray());


        foreach (var opDetail in tempOperationDetails)
        {
            if (embeddings.TryGetValue(opDetail.Description, out var embeddingVector) && embeddingVector != null && embeddingVector.Any())
            {
                finalDocumentsToStore.Add(new Document
                {
                    Embedding = embeddingVector,
                    Description = opDetail.Description,
                    Path = opDetail.OperationPathKey 
                    // Future: Could add ApiVersion or SourceJsonUrl (opDetail.SourceJsonUrl) to Document entity if needed
                });
            }
            // else: Log if embedding was not found or empty for a description
        }
        
        if (!finalDocumentsToStore.Any()) 
        {
            return Ok(new { message = $"Although descriptions were found, no valid embeddings could be generated or retrieved for any operations from {jsonUrls.Count} Swagger document(s)." });
        }

        await databaseService.BulkInsertAsync(finalDocumentsToStore, serviceId, HttpContext.RequestAborted);
        // String interpolation fixed here
        return Ok(new { message = $"Data scanned and saved successfully from {jsonUrls.Count} Swagger definition(s). Processed {finalDocumentsToStore.Count} operations." });
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SwaggerControllerSearchRequest req)
    {
        int? groupId = null;
        if (!string.IsNullOrEmpty(req.GroupName))
        {
            groupId = await databaseService.GetGroupIdByNameAsync(req.GroupName);
            if (!groupId.HasValue)
            {
                return NotFound(new { message = $"Group '{req.GroupName}' not found." });
            }
        }

        var embedding = await embeddingService.ConvertTextToEmbedding(req.Description);
        // Pass serviceId and groupId to SearchByDescriptionAsync
        // The serviceId from the request is used to filter by a specific service.
        // The groupId (if provided) is used to filter by a specific group.
        var results = databaseService.SearchByDescriptionAsync(embedding, req.ServiceId, groupId: groupId);

        List<Document> documents = [];
        await foreach (var document in results)
        {
            documents.Add(document);
        }

        return Ok(documents);
    }
}
