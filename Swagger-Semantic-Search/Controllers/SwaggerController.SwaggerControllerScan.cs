namespace Swagger_Semantic_Search.Controllers;

public class SwaggerControllerScanRequest
{
    public string Url { get; set; } = string.Empty;

    // public int ServiceId { get; set; } // ServiceId might become redundant
    public string GroupName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
}

public class SwaggerControllerSearchRequest
{
    public string Description { get; set; } = string.Empty;
}
