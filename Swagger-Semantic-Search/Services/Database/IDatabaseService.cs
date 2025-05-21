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

    public Task BulkInsertAsync(IEnumerable<Document> documents, int serviceId, CancellationToken cancellationToken = default);

    public IAsyncEnumerable<Document> SearchByDescriptionAsync(
        float[] descriptionEmbedding,
        int serviceId,
        int limit = 5,
        int? groupId = null // Added optional groupId
    );
    public void SeedDatabase();
    public Task<int> GetOrCreateGroupAsync(string groupName);
    public Task<int> GetOrCreateServiceAsync(string serviceName, string serviceUrl, int groupId);
    public Task<int?> GetGroupIdByNameAsync(string groupName); // Added for fetching groupId by name
}
