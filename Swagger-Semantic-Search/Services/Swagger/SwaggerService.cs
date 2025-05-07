using System.IO;
using System.Text.Json;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace Swagger_Semantic_Search.Services.Swagger;

public class SwaggerService()
{
    public async Task<OpenApiDocument> GetSwaggerDocumentAsync(string url)
    {
        using var client = new HttpClient();
        var stream = await client.GetStreamAsync(url);
        var openApiReader = new OpenApiStreamReader();
        var document = openApiReader.Read(stream, out var diagnostic);

        if (diagnostic.Errors.Count > 0)
        {
            throw new Exception(
                "Errores al leer el documento OpenAPI: "
                    + string.Join(", ", diagnostic.Errors.Select(e => e.Message))
            );
        }

        return document;
    }
}
