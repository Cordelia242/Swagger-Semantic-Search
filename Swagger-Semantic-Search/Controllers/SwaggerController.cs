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
        if (
            string.IsNullOrWhiteSpace(req.Url)
            || !Uri.TryCreate(req.Url, UriKind.Absolute, out var uriResult)
            || (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps)
        )
        {
            return BadRequest(
                new
                {
                    message = "Invalid URL format. Please provide an absolute HTTP or HTTPS URL."
                }
            );
        }

        // Get or create the group
        var groupId = await databaseService.GetOrCreateGroupAsync(req.GroupName);

        // Get or create the service
        var serviceId = await databaseService.GetOrCreateServiceAsync(
            req.ServiceName,
            req.Url,
            groupId
        );

        Microsoft.OpenApi.Models.OpenApiDocument swaggerData;
        try
        {
            swaggerData = await swaggerService.GetSwaggerDocumentAsync(req.Url);
        }
        catch (Exception ex)
        {
            // Log the exception ex here if logging is set up
            return new ObjectResult(
                new
                {
                    message = $"Failed to fetch or parse Swagger document from the provided URL. Details: {ex.Message}"
                }
            )
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity
            };
        }

        var operationsToEmbed = new List<(string OperationPathKey, string Description)>();

        foreach (var pathItemPair in swaggerData.Paths) // pathItemPair is KeyValuePair<string, OpenApiPathItem>
        {
            var pathKey = pathItemPair.Key; // e.g., "/users/{id}"
            foreach (var operationPair in pathItemPair.Value.Operations) // operationPair is KeyValuePair<OperationType, OpenApiOperation>
            {
                var operationType = operationPair.Key.ToString().ToUpper(); // "GET", "POST", etc.
                var operation = operationPair.Value;

                if (!string.IsNullOrEmpty(operation.Description))
                {
                    // Store the unique path for this operation and its description
                    operationsToEmbed.Add(($"{operationType} {pathKey}", operation.Description));
                }
            }
        }

        if (!operationsToEmbed.Any())
        {
            return Ok(new { message = "No operations with descriptions found to scan." });
        }

        // Then, extract descriptions for bulk embedding:
        var descriptionsToEmbed = operationsToEmbed.Select(o => o.Description).ToArray();
        // embeddings is Dictionary<string, float[]> where string is the description
        var embeddings = await embeddingService.BulkConversion(descriptionsToEmbed);

        // Then, create Document entities:
        List<Document> documents = operationsToEmbed
            .Select(opInfo => new Document
            {
                Embedding = embeddings.TryGetValue(opInfo.Description, out var embedding)
                    ? embedding
                    : Array.Empty<float>(), // Handle case where description might not be in embeddings dictionary, though it should be.
                Description = opInfo.Description,
                Path = opInfo.OperationPathKey // This is now "GET /path"
            })
            .ToList();

        // Filter out documents with empty embeddings if necessary, though BulkConversion should ideally handle all provided descriptions.
        documents = documents.Where(d => d.Embedding.Length > 0).ToList();

        if (!documents.Any())
        {
            return Ok(new { message = "No operations could be processed for embedding." });
        }

        await databaseService.BulkInsertAsync(documents, serviceId, HttpContext.RequestAborted);

        return Ok(new { message = "Data scanned and saved successfully." });
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SwaggerControllerSearchRequest req)
    {
        var embedding = await embeddingService.ConvertTextToEmbedding(req.Description);
        var results = databaseService.SearchByDescriptionAsync(embedding);

        List<Document> documents = [];
        await foreach (var document in results)
        {
            documents.Add(document);
        }

        return Ok(documents);
    }
}
