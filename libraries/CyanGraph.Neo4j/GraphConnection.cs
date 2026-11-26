using Neo4j.Driver;

namespace CyanGraph.Data;

/// <summary>Records retain the driver's immutable values (including INode, IRelationship and IPath)
/// and remain readable after the cursor and transaction have been closed.</summary>
public sealed record GraphResult(IReadOnlyList<string> Columns, IReadOnlyList<IRecord> Rows, bool HasMore);

internal sealed record NeighborLink(INode Source, IRelationship Relationship, INode Other);
internal sealed record NeighborPage(IReadOnlyList<NeighborLink> Links, bool HasMore);

public sealed class GraphConnection : IAsyncDisposable
{
    public const int RowLimit = 200;
    public const int NeighborPageSize = 10;
    readonly IDriver driver;
    public string Database { get; }
    public Uri Address { get; }

    GraphConnection(IDriver driver, Uri address, string database)
    {
        this.driver = driver;
        Address = address;
        Database = database;
    }

    public static async Task<GraphConnection> OpenAsync(string address, string database, string username,
        string password, CancellationToken token = default)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("neo4j" or "neo4j+s" or "neo4j+ssc" or "bolt" or "bolt+s" or "bolt+ssc") ||
            uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" ||
            uri.Fragment.Length != 0 || uri.Query.Length != 0)
            throw new ArgumentException("Enter a Neo4j or Bolt URI with a host and optional port.");
        if (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Enter a database name and username.");

        token.ThrowIfCancellationRequested();
        var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username.Trim(), password), options => options
            .WithConnectionTimeout(TimeSpan.FromSeconds(6))
            .WithConnectionAcquisitionTimeout(TimeSpan.FromSeconds(8)));
        try
        {
            // Verification must open the selected database, not just authenticate with the server.
            await using var session = driver.AsyncSession(c => c.WithDatabase(database.Trim()).WithDefaultAccessMode(AccessMode.Read));
            var probe = database.Trim().Equals("system", StringComparison.OrdinalIgnoreCase) ? "SHOW CURRENT USER" : "RETURN 1";
            var cursor = await session.RunAsync(probe, c => c.WithTimeout(TimeSpan.FromSeconds(6)));
            var summary = await cursor.ConsumeAsync();
            token.ThrowIfCancellationRequested();
            return new GraphConnection(driver, uri, summary.Database.Name);
        }
        catch
        {
            await driver.DisposeAsync();
            throw;
        }
    }

    public async Task<GraphResult> QueryAsync(string cypher, IDictionary<string, object> parameters,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cypher);
        token.ThrowIfCancellationRequested();
        var request = Guid.NewGuid().ToString("N");
        await using var session = driver.AsyncSession(c => c.WithDatabase(Database)
            .WithDefaultAccessMode(AccessMode.Read).WithFetchSize(RowLimit + 1));
        await using var tx = await session.BeginTransactionAsync(c => c.WithTimeout(TimeSpan.FromSeconds(30))
            .WithMetadata(new Dictionary<string, object> { ["cyanGraphRequest"] = request }));
        token.ThrowIfCancellationRequested();

        var read = ReadAsync(tx, cypher, parameters, token);
        try
        {
            var result = await read.WaitAsync(token);
            // A preview never commits. In particular, stop pulling as soon as we have our lookahead.
            await tx.RollbackAsync();
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!read.IsCompleted)
            {
                // The driver's RunAsync/FetchAsync have no cancellation-token overload. Interrupt the
                // specific server transaction, including a query still computing its first record.
                try { await InterruptAsync(request); }
                finally
                {
                    try { await read; }
                    catch (Exception) when (token.IsCancellationRequested) { }
                }
            }
            throw;
        }
    }

    internal async Task<NeighborPage> ReadNeighborsAsync(string nodeId, string after, CancellationToken token)
    {
        // One undirected hop finds both incoming and outgoing edges. Keep the driver's stored direction.
        const string query = """
            MATCH (source)-[edge]-(other)
            WHERE elementId(source) = $node AND elementId(edge) > $after
            RETURN DISTINCT source, edge, other, elementId(edge) AS cursor
            ORDER BY cursor LIMIT $limit
            """;
        var page = await QueryAsync(query, new Dictionary<string, object>
            { ["node"] = nodeId, ["after"] = after, ["limit"] = NeighborPageSize + 1 }, token);
        var links = page.Rows.Take(NeighborPageSize).Select(row =>
            new NeighborLink(row["source"].As<INode>(), row["edge"].As<IRelationship>(), row["other"].As<INode>())).ToArray();
        return new NeighborPage(links, page.Rows.Count > NeighborPageSize);
    }

    static async Task<GraphResult> ReadAsync(IAsyncTransaction tx, string cypher,
        IDictionary<string, object> parameters, CancellationToken token)
    {
        var cursor = await tx.RunAsync(cypher, parameters);
        var columns = await cursor.KeysAsync();
        var rows = new List<IRecord>();
        while (await cursor.FetchAsync())
        {
            token.ThrowIfCancellationRequested();
            if (rows.Count == RowLimit)
                return new GraphResult(columns, rows.AsReadOnly(), true);
            rows.Add(cursor.Current);
        }
        return new GraphResult(columns, rows.AsReadOnly(), false);
    }

    async Task InterruptAsync(string request)
    {
        await using var control = driver.AsyncSession(c => c.WithDatabase("system"));
        var cursor = await control.RunAsync(
            "SHOW TRANSACTIONS YIELD transactionId, metaData WHERE metaData.cyanGraphRequest = $request RETURN transactionId",
            new { request }, c => c.WithTimeout(TimeSpan.FromSeconds(5)));
        var ids = await cursor.ToListAsync(record => record["transactionId"].As<string>());
        if (ids.Count == 0) return;
        var stopped = await control.RunAsync("TERMINATE TRANSACTIONS $ids", new { ids },
            c => c.WithTimeout(TimeSpan.FromSeconds(5)));
        await stopped.ConsumeAsync();
    }

    public ValueTask DisposeAsync() => driver.DisposeAsync();
}
