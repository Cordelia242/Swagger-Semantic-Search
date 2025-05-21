using Xunit;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Swagger_Semantic_Search.Controllers;
using Swagger_Semantic_Search.Services.Swagger;
using Swagger_Semantic_Search.Services.Embedding;
using Swagger_Semantic_Search.Services.Database;
using Swagger_Semantic_Search.Entities; // For Document
using Microsoft.OpenApi.Models; // For OpenApiDocument, OpenApiPathItem, OpenApiOperation
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq; // Required for .Select, .ToArray, .Any

public class SwaggerControllerTests
{
    private readonly Mock<SwaggerService> _mockSwaggerService;
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly SwaggerController _controller;

    public SwaggerControllerTests()
    {
        // SwaggerService is a concrete class, so mocking it requires it to be virtual or use an interface.
        // For simplicity, I'm assuming SwaggerService can be mocked directly or it implements an interface like ISwaggerService.
        // If SwaggerService methods are not virtual, Moq can't override them. We'll assume they are or ISwaggerService is used.
        _mockSwaggerService = new Mock<SwaggerService>(null); // Assuming constructor takes ILogger or is parameterless for Moq
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockDatabaseService = new Mock<IDatabaseService>();

        _controller = new SwaggerController(
            _mockSwaggerService.Object,
            _mockEmbeddingService.Object,
            _mockDatabaseService.Object
        );

        // Mock HttpContext for RequestAborted token
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task Scan_InvalidUrl_ReturnsBadRequest()
    {
        // Arrange
        var request = new SwaggerControllerScanRequest { Url = "invalid-url", GroupName = "TestGroup", ServiceName = "TestService" };

        // Act
        var result = await _controller.Scan(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Invalid URL format. Please provide an absolute HTTP or HTTPS URL.", ((dynamic)badRequestResult.Value).message);
    }

    [Fact]
    public async Task Scan_SwaggerFetchFails_ReturnsUnprocessableEntity()
    {
        // Arrange
        var request = new SwaggerControllerScanRequest { Url = "http://valid-url.com", GroupName = "TestGroup", ServiceName = "TestService" };
        _mockSwaggerService.Setup(s => s.GetSwaggerDocumentAsync(request.Url))
            .ThrowsAsync(new System.Exception("Fetch failed"));

        // Act
        var result = await _controller.Scan(request);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, objectResult.StatusCode);
        Assert.Contains("Fetch failed", ((dynamic)objectResult.Value).message);
    }

    [Fact]
    public async Task Scan_NoOperationsInSwagger_ReturnsOkWithMessage()
    {
        // Arrange
        var request = new SwaggerControllerScanRequest { Url = "http://valid-url.com", GroupName = "TestGroup", ServiceName = "TestService" };
        var swaggerDoc = new OpenApiDocument(); // Empty document
        _mockSwaggerService.Setup(s => s.GetSwaggerDocumentAsync(request.Url))
            .ReturnsAsync(swaggerDoc);
        _mockDatabaseService.Setup(db => db.GetOrCreateGroupAsync(It.IsAny<string>())).ReturnsAsync(1);
        _mockDatabaseService.Setup(db => db.GetOrCreateServiceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(1);


        // Act
        var result = await _controller.Scan(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("No operations with descriptions found to scan.", ((dynamic)okResult.Value).message);
    }
    
    [Fact]
    public async Task Scan_ValidRequest_ProcessesOperationsCorrectly()
    {
        // Arrange
        var request = new SwaggerControllerScanRequest { Url = "http://valid-url.com", GroupName = "TestGroup", ServiceName = "TestService" };
        
        var swaggerDoc = new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/items"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Get] = new OpenApiOperation { Description = "Get all items" },
                        [OperationType.Post] = new OpenApiOperation { Description = "Create an item" }
                    }
                },
                ["/items/{id}"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Get] = new OpenApiOperation { Description = "Get item by ID" },
                        [OperationType.Put] = new OpenApiOperation { /* No description */ }
                    }
                }
            }
        };

        _mockSwaggerService.Setup(s => s.GetSwaggerDocumentAsync(request.Url)).ReturnsAsync(swaggerDoc);
        _mockDatabaseService.Setup(db => db.GetOrCreateGroupAsync(request.GroupName)).ReturnsAsync(1);
        _mockDatabaseService.Setup(db => db.GetOrCreateServiceAsync(request.ServiceName, request.Url, 1)).ReturnsAsync(101);

        var expectedDescriptions = new[] { "Get all items", "Create an item", "Get item by ID" };
        var embeddingsDict = expectedDescriptions.ToDictionary(
            desc => desc, 
            desc => new[] { 0.1f, 0.2f } // Dummy embedding
        );
        _mockEmbeddingService.Setup(e => e.BulkConversion(It.Is<string[]>(descs => descs.SequenceEqual(expectedDescriptions))))
            .ReturnsAsync(embeddingsDict);

        List<Document> capturedDocuments = null;
        _mockDatabaseService.Setup(db => db.BulkInsertAsync(It.IsAny<IEnumerable<Document>>(), 101, It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Document>, int, CancellationToken>((docs, id, token) => capturedDocuments = docs.ToList())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.Scan(request);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        _mockDatabaseService.Verify(db => db.BulkInsertAsync(It.IsAny<IEnumerable<Document>>(), 101, It.IsAny<CancellationToken>()), Times.Once);
        
        Assert.NotNull(capturedDocuments);
        Assert.Equal(3, capturedDocuments.Count);

        Assert.Contains(capturedDocuments, d => d.Path == "GET /items" && d.Description == "Get all items");
        Assert.Contains(capturedDocuments, d => d.Path == "POST /items" && d.Description == "Create an item");
        Assert.Contains(capturedDocuments, d => d.Path == "GET /items/{id}" && d.Description == "Get item by ID");
        Assert.All(capturedDocuments, d => Assert.NotEmpty(d.Embedding));
    }

    [Fact]
    public async Task Search_GroupNotFound_ReturnsNotFound()
    {
        // Arrange
        var request = new SwaggerControllerSearchRequest { Description = "test", ServiceId = 1, GroupName = "NonExistentGroup" };
        _mockDatabaseService.Setup(db => db.GetGroupIdByNameAsync(request.GroupName))
            .ReturnsAsync((int?)null);

        // Act
        var result = await _controller.Search(request);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Group '{request.GroupName}' not found.", ((dynamic)notFoundResult.Value).message);
    }

    [Fact]
    public async Task Search_ValidRequest_CallsSearchByDescriptionAsyncCorrectly_WithGroup()
    {
        // Arrange
        var request = new SwaggerControllerSearchRequest { Description = "test query", ServiceId = 1, GroupName = "TestGroup" };
        var expectedEmbedding = new[] { 0.1f, 0.2f, 0.3f };
        var expectedGroupId = 5;
        var expectedDocuments = new List<Document> { new Document { Path = "GET /test", Description = "Test desc" } };

        _mockEmbeddingService.Setup(e => e.ConvertTextToEmbedding(request.Description))
            .ReturnsAsync(expectedEmbedding);
        _mockDatabaseService.Setup(db => db.GetGroupIdByNameAsync(request.GroupName))
            .ReturnsAsync(expectedGroupId);
        _mockDatabaseService.Setup(db => db.SearchByDescriptionAsync(expectedEmbedding, request.ServiceId, 5, expectedGroupId))
            .Returns(expectedDocuments.ToAsyncEnumerable());

        // Act
        var result = await _controller.Search(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedDocuments = Assert.IsAssignableFrom<IEnumerable<Document>>(okResult.Value);
        Assert.Single(returnedDocuments);
        _mockDatabaseService.Verify(db => db.SearchByDescriptionAsync(expectedEmbedding, request.ServiceId, It.IsAny<int>(), expectedGroupId), Times.Once);
    }
    
    [Fact]
    public async Task Search_ValidRequest_CallsSearchByDescriptionAsyncCorrectly_NoGroup()
    {
        // Arrange
        var request = new SwaggerControllerSearchRequest { Description = "test query", ServiceId = 1, GroupName = null }; // No GroupName
        var expectedEmbedding = new[] { 0.1f, 0.2f, 0.3f };
        // GroupId should be null in this case
        var expectedDocuments = new List<Document> { new Document { Path = "GET /test", Description = "Test desc" } };

        _mockEmbeddingService.Setup(e => e.ConvertTextToEmbedding(request.Description))
            .ReturnsAsync(expectedEmbedding);
        _mockDatabaseService.Setup(db => db.SearchByDescriptionAsync(expectedEmbedding, request.ServiceId, 5, null)) 
            .Returns(expectedDocuments.ToAsyncEnumerable());

        // Act
        var result = await _controller.Search(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedDocuments = Assert.IsAssignableFrom<IEnumerable<Document>>(okResult.Value);
        Assert.Single(returnedDocuments);
        _mockDatabaseService.Verify(db => db.GetGroupIdByNameAsync(It.IsAny<string>()), Times.Never); // Ensure GetGroupIdByNameAsync is not called
        _mockDatabaseService.Verify(db => db.SearchByDescriptionAsync(expectedEmbedding, request.ServiceId, It.IsAny<int>(), null), Times.Once);
    }
}

// Helper to convert IEnumerable to IAsyncEnumerable for Moq
public static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }
        await Task.CompletedTask; // To make the compiler happy about async iterator
    }
}
