namespace Swagger_Semantic_Search.Services.Embedding;

public interface IEmbeddingService
{
    public Task<float[]> ConvertTextToEmbedding(string text);
    public Task<Dictionary<string, float[]>> BulkConversion(string[] texts);
}
