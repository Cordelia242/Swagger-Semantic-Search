namespace Swagger_Semantic_Search.Entities;

public class SwaggerServiceEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int GroupId { get; set; }
    // Optional: public ServiceGroup Group { get; set; }
}
