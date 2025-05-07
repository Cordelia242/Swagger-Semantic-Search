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
        var swaggerData = await swaggerService.GetSwaggerDocumentAsync(req.Url);
        Dictionary<string, string> paths = swaggerData
            .Paths.Select(s =>
            {
                var firstOperation = s.Value.Operations.First();
                return new KeyValuePair<string, string>(
                    s.Key.ToString(),
                    firstOperation.Value.Description
                );
            })
            .Where(s => s.Value != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        var embeddings = await embeddingService.BulkConversion([.. paths.Values]);
        IEnumerable<Document> documents = paths.Select(path => new Document
        {
            Embedding = embeddings.FirstOrDefault(x => x.Key == path.Value).Value,
            Description = path.Value,
            Path = path.Key
        });

        await databaseService.BulkInsertAsync(documents, req.ServiceId);

        return Ok(new { message = "Data scanned and saved successfully." });
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SwaggerControllerSearchRequest body)
    {
        var embedding = await embeddingService.ConvertTextToEmbedding(body.Description);
        var results = databaseService.SearchByDescriptionAsync(embedding, body.ServiceId);

        List<Document> documents = [];
        await foreach (var document in results)
        {
            documents.Add(document);
        }

        return Ok(documents);
    }
}
