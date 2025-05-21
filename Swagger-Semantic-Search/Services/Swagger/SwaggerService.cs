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

    public async Task<List<string>> DiscoverSwaggerJsonUrlsAsync(string swaggerUiUrl)
    {
        var discoveredUrls = new HashSet<string>(); // Use HashSet to store unique URLs
        using var client = new HttpClient();

        string htmlContent;
        try
        {
            htmlContent = await client.GetStringAsync(swaggerUiUrl);
        }
        catch (HttpRequestException ex)
        {
            // Propagate the exception or handle it as per requirements (e.g., log and return empty list or throw custom exception)
            throw new Exception($"Error fetching Swagger UI content from {swaggerUiUrl}: {ex.Message}", ex);
        }

        // Strategy 1: Look for swaggerOptions or SwaggerUIBundle configuration
        // Regex to find url: "..."
        var primaryUrlRegex = new System.Text.RegularExpressions.Regex(@"url\s*:\s*""([^""]+)""");
        // Regex to find urls: [...]
        var urlsArrayRegex = new System.Text.RegularExpressions.Regex(@"urls\s*:\s*(\[.*?\])", System.Text.RegularExpressions.RegexOptions.Singleline);

        // Attempt to find the primary URL directly in the configuration
        var primaryMatch = primaryUrlRegex.Match(htmlContent);
        if (primaryMatch.Success)
        {
            var url = primaryMatch.Groups[1].Value;
            discoveredUrls.Add(MakeAbsoluteUrl(swaggerUiUrl, url));
        }

        // Attempt to find the 'urls' array in the configuration
        var urlsArrayMatch = urlsArrayRegex.Match(htmlContent);
        if (urlsArrayMatch.Success)
        {
            var arrayContent = urlsArrayMatch.Groups[1].Value;
            // Now parse the arrayContent for individual url: "..." occurrences
            var urlMatchesInArray = primaryUrlRegex.Matches(arrayContent);
            foreach (System.Text.RegularExpressions.Match match in urlMatchesInArray)
            {
                var url = match.Groups[1].Value;
                discoveredUrls.Add(MakeAbsoluteUrl(swaggerUiUrl, url));
            }
        }
        
        // Strategy 2: Fallback to searching for <a> tags (if Strategy 1 yielded no results or as a complementary strategy)
        // This is a simplified version. A more robust version would parse HTML more carefully.
        if (!discoveredUrls.Any()) // Only use as fallback if primary strategy fails
        {
            var aTagRegex = new System.Text.RegularExpressions.Regex(@"<a\s+(?:[^>]*?\s+)?href=""([^""]*?\.(?:json|yaml|yml))""");
            var aTagMatches = aTagRegex.Matches(htmlContent);
            foreach (System.Text.RegularExpressions.Match match in aTagMatches)
            {
                var url = match.Groups[1].Value;
                discoveredUrls.Add(MakeAbsoluteUrl(swaggerUiUrl, url));
            }
        }

        return discoveredUrls.ToList();
    }

    private string MakeAbsoluteUrl(string baseUrl, string relativeOrAbsoluteUrl)
    {
        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }
        return new Uri(new Uri(baseUrl), relativeOrAbsoluteUrl).ToString();
    }
}
