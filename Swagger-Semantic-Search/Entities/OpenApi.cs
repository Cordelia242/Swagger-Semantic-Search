namespace Swagger_Semantic_Search.Entities;

public class OpenApiDocument
{
    public string Openapi { get; set; } = string.Empty;
    public Info Info { get; set; } = new();
    public Dictionary<string, PathItem> Paths { get; set; } = [];
    public Components Components { get; set; } = new();
}

public class Info
{
    public string Title { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TermsOfService { get; set; } = string.Empty;
    public Contact Contact { get; set; } = new();
    public License License { get; set; } = new();
}

public class Contact
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class License
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class PathItem
{
    public Operation Get { get; set; } = new();
    public Operation Post { get; set; } = new();
    public Operation Put { get; set; } = new();
    public Operation Delete { get; set; } = new();
    public Operation Patch { get; set; } = new();
    public Operation Head { get; set; } = new();
    public Operation Options { get; set; } = new();
    public Operation Trace { get; set; } = new();
}

public class Operation
{
    public List<string> Tags { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public List<Parameter> Parameters { get; set; } = [];
    public RequestBody RequestBody { get; set; } = new();
    public Dictionary<string, Response> Responses { get; set; } = [];
}

public class Parameter
{
    public string Name { get; set; } = string.Empty;
    public string In { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Required { get; set; }
    public Schema Schema { get; set; } = new();
}

public class RequestBody
{
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, MediaType> Content { get; set; } = [];
    public bool Required { get; set; }
}

public class Response
{
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, MediaType> Content { get; set; } = [];
}

public class MediaType
{
    public Schema Schema { get; set; } = new();
}

public class Schema
{
    public string Type { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public Dictionary<string, Schema> Properties { get; set; } = [];
    public Schema Items { get; set; } = new();
    public string Ref { get; set; } = string.Empty;
}

public class Components
{
    public Dictionary<string, Schema> Schemas { get; set; } = [];
}
