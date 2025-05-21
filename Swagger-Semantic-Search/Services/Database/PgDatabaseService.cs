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
        int limit = 5
    )
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var query =
            @"
            SELECT sd.description, sd.path 
            FROM swagger_data sd";

        query += " ORDER BY sd.embedding <-> @embedding LIMIT @limit";

        await using var cmd = new NpgsqlCommand(query, connection);

        cmd.Parameters.AddWithValue("embedding", new Vector(descriptionEmbedding));
        cmd.Parameters.AddWithValue("limit", limit);

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
        using var conn = dataSource.OpenConnection();
        await using (var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        conn.ReloadTypes();

        // Create service_groups table
        await using (
            var cmd = new NpgsqlCommand(
                "CREATE TABLE IF NOT EXISTS service_groups (id SERIAL PRIMARY KEY, name TEXT UNIQUE NOT NULL)",
                conn
            )
        )
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Create swagger_services table
        await using (
            var cmd = new NpgsqlCommand(
                // Ensure this table creation is compatible with potential existing structure or handle alteration carefully.
                // For simplicity, creating it with group_id if it doesn't exist.
                // A more robust approach for existing databases would be to check and alter.
                "CREATE TABLE IF NOT EXISTS swagger_services (id SERIAL PRIMARY KEY, name TEXT, url TEXT, group_id INT REFERENCES service_groups(id))",
                conn
            )
        )
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Alter swagger_services table to add group_id if it doesn't exist (safer for existing dbs)
        // This is a common pattern; exact syntax might vary or need to be split into multiple commands
        // depending on PostgreSQL version and existing constraints.
        await using (
            var cmd = new NpgsqlCommand(
                @"
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name='swagger_services' AND column_name='group_id'
                ) THEN
                    ALTER TABLE swagger_services
                    ADD COLUMN group_id INT,
                    ADD CONSTRAINT fk_group FOREIGN KEY (group_id) REFERENCES service_groups(id);
                END IF;
            END$$;",
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

    public async Task<int> GetOrCreateGroupAsync(string groupName)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        // Check if group exists
        await using (
            var cmd = new NpgsqlCommand("SELECT id FROM service_groups WHERE name = $1", connection)
        )
        {
            cmd.Parameters.AddWithValue(groupName);
            var groupId = await cmd.ExecuteScalarAsync();
            if (groupId != null)
            {
                return (int)groupId;
            }
        }

        // Create group if not exists
        await using (
            var cmd = new NpgsqlCommand(
                "INSERT INTO service_groups (name) VALUES ($1) RETURNING id",
                connection
            )
        )
        {
            cmd.Parameters.AddWithValue(groupName);
            return (int)(
                await cmd.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Failed to create group.")
            );
        }
    }

    public async Task<int> GetOrCreateServiceAsync(
        string serviceName,
        string serviceUrl,
        int groupId
    )
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        // Check if service exists
        await using (
            var cmd = new NpgsqlCommand(
                "SELECT id FROM swagger_services WHERE name = $1 AND group_id = $2",
                connection
            )
        )
        {
            cmd.Parameters.AddWithValue(serviceName);
            cmd.Parameters.AddWithValue(groupId);
            var serviceId = await cmd.ExecuteScalarAsync();
            if (serviceId != null)
            {
                // Update URL if service exists
                await using (
                    var updateCmd = new NpgsqlCommand(
                        "UPDATE swagger_services SET url = $1 WHERE id = $2",
                        connection
                    )
                )
                {
                    updateCmd.Parameters.AddWithValue(serviceUrl);
                    updateCmd.Parameters.AddWithValue((int)serviceId);
                    await updateCmd.ExecuteNonQueryAsync();
                }
                return (int)serviceId;
            }
        }

        // Create service if not exists
        await using (
            var cmd = new NpgsqlCommand(
                "INSERT INTO swagger_services (name, url, group_id) VALUES ($1, $2, $3) RETURNING id",
                connection
            )
        )
        {
            cmd.Parameters.AddWithValue(serviceName);
            cmd.Parameters.AddWithValue(serviceUrl);
            cmd.Parameters.AddWithValue(groupId);
            return (int)(
                await cmd.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Failed to create service.")
            );
        }
    }

    public async Task<int?> GetGroupIdByNameAsync(string groupName)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM service_groups WHERE name = $1",
            connection
        );
        cmd.Parameters.AddWithValue(groupName);
        var groupId = await cmd.ExecuteScalarAsync();
        if (groupId != null)
        {
            return (int)groupId;
        }
        return null;
    }
}
