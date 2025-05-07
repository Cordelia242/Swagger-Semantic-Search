using System;

namespace Swagger_Semantic_Search.Services.Embedding;

public class GoogleEmbeddingServiceResponse
{
    public GoogleEmbeddingServiceResponseEmbedding Embedding { get; set; } = new();
}

public class GoogleEmbeddingServiceResponseEmbedding
{
    public float[] Values { get; set; } = [];
}
