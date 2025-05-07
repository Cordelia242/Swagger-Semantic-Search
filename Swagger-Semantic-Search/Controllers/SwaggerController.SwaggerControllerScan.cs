namespace Swagger_Semantic_Search.Controllers;

public class SwaggerControllerScanRequest
{
    public string Url { get; set; } = string.Empty;
    public int ServiceId { get; set; }
}

public class SwaggerControllerSearchRequest
{
    public int ServiceId { get; set; }
    public string Description { get; set; } = string.Empty;
}
