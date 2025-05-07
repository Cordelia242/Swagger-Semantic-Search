namespace Swagger_Semantic_Search.Entities;

public class Document
{
    public int Id { get; set; }
    public float[] Embedding { get; set; } = [];
    public string Description { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}
