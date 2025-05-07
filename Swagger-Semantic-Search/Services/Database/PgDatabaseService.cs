using Npgsql;
using NpgsqlTypes;
using Pgvector;
using Swagger_Semantic_Search.Entities;

namespace Swagger_Semantic_Search.Services.Database;

public class PgDatabaseService : IDatabaseService
{
    private readonly NpgsqlDataSource dataSource;

    public PgDatabaseService(string connectionString)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        dataSource = dataSourceBuilder.Build();
    }

    public async IAsyncEnumerable<Document> SearchByDescriptionAsync(
        float[] descriptionEmbedding,
        int serviceId,
        int limit = 5
    )
    {
        using var connection = dataSource.OpenConnection();
        await using var cmd = new NpgsqlCommand(
            "SELECT description, path FROM swagger_data ORDER BY embedding <-> $1 LIMIT 5",
            connection
        );

        var embedding = new Vector(descriptionEmbedding);
        cmd.Parameters.AddWithValue(embedding);

        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            yield return new Document
            {
                Description = reader.GetString(0),
                Path = reader.GetString(1)
            };
        }
    }

    public async Task SaveDataAsync(
        string name,
        float[] descriptionEmbedding,
        string description,
        string path,
        int serviceId
    )
    {
        using var connection = dataSource.OpenConnection();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO swagger_data (embedding, description, path, service_id ) VALUES ($1, $2, $3, $4)",
            connection
        );
        var embedding = new Vector(descriptionEmbedding);
        cmd.Parameters.AddWithValue(embedding);
        cmd.Parameters.AddWithValue(description);
        cmd.Parameters.AddWithValue(path);
        cmd.Parameters.AddWithValue(serviceId);
        await cmd.ExecuteNonQueryAsync();

        await connection.CloseAsync();
    }

    public async void SeedDatabase()
    {
        var conn = dataSource.OpenConnection();
        await using (var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        conn.ReloadTypes();

        await using (
            var cmd = new NpgsqlCommand(
                "CREATE TABLE IF NOT EXISTS swagger_services (id SERIAL PRIMARY KEY, name TEXT, url TEXT)",
                conn
            )
        )
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (
            var cmd = new NpgsqlCommand(
                @"CREATE TABLE IF NOT EXISTS swagger_data (
            id SERIAL PRIMARY KEY,
            description TEXT, 
            path TEXT, 
            embedding VECTOR(768),
            service_id INT REFERENCES swagger_services(id)
        )",
                conn
            )
        )
        {
            await cmd.ExecuteNonQueryAsync();
        }

        conn.Close();
    }

    public async Task BulkInsertAsync(IEnumerable<Document> documents, int serviceId)
    {
        using var connection = dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var document in documents)
        {
            using var cmd = new NpgsqlCommand(
                "INSERT INTO swagger_data (embedding, description, path, service_id) VALUES ($1, $2, $3, $4)",
                connection
            );
            var embedding = new Vector(document.Embedding);
            cmd.Parameters.AddWithValue(embedding);
            cmd.Parameters.AddWithValue(document.Description);
            cmd.Parameters.AddWithValue(document.Path);
            cmd.Parameters.AddWithValue(serviceId);
            await cmd.ExecuteNonQueryAsync();
        }

        transaction.Commit();

        await connection.CloseAsync();
    }

    public async Task BulkInsertAsync(
        IEnumerable<Document> documents,
        int serviceId,
        CancellationToken cancellationToken = default
    )
    {
        var copyCommand =
            "COPY swagger_data (embedding, description, path, service_id) FROM STDIN BINARY";

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (
            var importer = await connection.BeginBinaryImportAsync(copyCommand, cancellationToken)
        )
        {
            foreach (var document in documents)
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(new Vector(document.Embedding), cancellationToken);
                await importer.WriteAsync(
                    document.Description,
                    NpgsqlDbType.Text,
                    cancellationToken
                );
                await importer.WriteAsync(document.Path, NpgsqlDbType.Text, cancellationToken); // O Varchar
                await importer.WriteAsync(serviceId, NpgsqlDbType.Integer, cancellationToken);
            }
            await importer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
