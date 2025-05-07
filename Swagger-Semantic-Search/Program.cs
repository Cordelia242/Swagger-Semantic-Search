using Swagger_Semantic_Search.Services.Database;
using Swagger_Semantic_Search.Services.Embedding;
using Swagger_Semantic_Search.Services.Swagger;

var builder = WebApplication.CreateBuilder(args);
Console.WriteLine("Starting Swagger Semantic Search API...");
builder.Services.AddSingleton<IDatabaseService, PgDatabaseService>(sp =>
{
    var connectionString =
        builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Connection string 'Postgres' not found.");
    var service = new PgDatabaseService(connectionString);
    Console.WriteLine("seeding database...");
    service.SeedDatabase();
    return service;
});

builder.Services.AddSingleton<IEmbeddingService, GoogleEmbeddingService>(sp =>
{
    var apiKey =
        builder.Configuration["GoogleEmbedding:ApiKey"]
        ?? throw new InvalidOperationException("Google API key not found.");
    var embeddingService = new GoogleEmbeddingService(apiKey);
    return embeddingService;
});
builder.Services.AddSingleton<SwaggerService>();

builder.Services.AddControllers();

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
