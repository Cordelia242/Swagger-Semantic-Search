using Swagger_Semantic_Search.Entities;

namespace Swagger_Semantic_Search.Services.Database;

public interface IDatabaseService
{
    public Task SaveDataAsync(
        string name,
        float[] embedding,
        string description,
        string path,
        int serviceId
    );

    public Task BulkInsertAsync(IEnumerable<Document> documents, int serviceId);

    public IAsyncEnumerable<Document> SearchByDescriptionAsync(
        float[] descriptionEmbedding,
        int serviceId,
        int limit = 5
    );
    public void SeedDatabase();
}
