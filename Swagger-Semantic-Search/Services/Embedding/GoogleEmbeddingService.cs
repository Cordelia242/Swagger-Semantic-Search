namespace Swagger_Semantic_Search.Services.Embedding;

public class GoogleEmbeddingService(string apiKey) : IEmbeddingService
{
    private readonly HttpClient httpClient = new();

    public async Task<Dictionary<string, float[]>> BulkConversion(string[] texts)
    {
        Dictionary<string, float[]> result = [];
        foreach (var text in texts)
        {
            await Task.Delay(200);

            var embedding = await ConvertTextToEmbedding(text);
            result.Add(text, embedding);
        }
        return result;
    }

    public async Task<float[]> ConvertTextToEmbedding(string text)
    {
        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/text-embedding-004:embedContent?key={apiKey}";

        var response = await httpClient.PostAsJsonAsync(
            url,
            new
            {
                model = "models/text-embedding-004",
                content = new { parts = new List<object>() { new { text } } }
            }
        );

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to get embedding from Google API: {error}");
        }

        var embedding = await response.Content.ReadFromJsonAsync<GoogleEmbeddingServiceResponse>();
        if (embedding is null)
            throw new Exception("Failed to get embedding from Google API");

        return embedding.Embedding.Values;
    }
}
