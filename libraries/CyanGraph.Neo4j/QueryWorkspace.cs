using Neo4j.Driver;

namespace CyanGraph.Data;

public enum QueryPhase { Disconnected, Connecting, Ready, Running, Stopping, Complete, Stopped, Failed, Disconnecting }

/// <summary>Call from the UI context. Serialize ownership of the driver; a superseded request
/// finishes its cleanup before the next request starts, and can never publish into the new view.</summary>
public sealed class QueryWorkspace
{
    GraphConnection? connection;
    CancellationTokenSource? current;
    Task tail = Task.CompletedTask;
    public GraphConnection? Connected { get; private set; }
    public GraphResult? Result { get; private set; }
    public GraphScene? Scene { get; private set; }
    public QueryPhase Phase { get; private set; } = QueryPhase.Disconnected;
    public string Status { get; private set; } = "Not connected";
    public event Action? Changed;

    /// <summary>An independent result on the same driver. Its owner must keep the connection
    /// open until the secondary workspace has disconnected.</summary>
    public QueryWorkspace CreateSeparateQuery()
    {
        var target = Connected ?? throw new InvalidOperationException("Connect to a database first.");
        var child = new QueryWorkspace { Connected = target };
        child.Set(QueryPhase.Ready, $"Connected to {target.Database}");
        return child;
    }

    public Task ConnectAsync(string address, string database, string username, string password)
    {
        Connected = null;
        return Schedule(QueryPhase.Connecting, "Connecting…", async token =>
        {
            await CloseConnectionAsync();
            var next = await GraphConnection.OpenAsync(address, database, username, password, token);
            if (token.IsCancellationRequested) { await next.DisposeAsync(); return; }
            connection = Connected = next;
            Set(QueryPhase.Ready, $"Connected to {next.Database}");
        });
    }

    public Task RunAsync(string text, string parameterJson)
    {
        var target = Connected ?? throw new InvalidOperationException("Connect to a database first.");
        return Schedule(QueryPhase.Running, "Running query…", async token =>
        {
            var result = await target.QueryAsync(text, QueryParameters.Parse(parameterJson), token);
            if (token.IsCancellationRequested) return;
            Scene = new GraphScene(target, ResultGraph.From(result));
            Result = result;
            var count = result.Rows.Count;
            Set(QueryPhase.Complete, result.HasMore ? $"First {count} rows · more available" :
                count == 0 ? "0 rows · no matches" : $"{count} {(count == 1 ? "row" : "rows")}");
        });
    }

    public void Stop()
    {
        if (Phase != QueryPhase.Running) return;
        Set(QueryPhase.Stopping, "Stopping query…");
        current?.Cancel();
        Changed?.Invoke();
    }

    public Task DisconnectAsync()
    {
        Connected = null;
        return Schedule(QueryPhase.Disconnecting, "Disconnecting…", async token =>
        {
            await CloseConnectionAsync();
            if (!token.IsCancellationRequested) Set(QueryPhase.Disconnected, "Disconnected");
        });
    }

    /// <summary>Opening a stored statement prepares an empty result on the current database.</summary>
    public Task ClearResultAsync() => Connected == null ? Task.CompletedTask :
        Schedule(QueryPhase.Ready, "No results", _ => Task.CompletedTask);

    async Task CloseConnectionAsync()
    {
        var old = connection;
        connection = null;
        if (old != null) await old.DisposeAsync();
    }

    Task Schedule(QueryPhase phase, string status, Func<CancellationToken, Task> work)
    {
        current?.Cancel();
        var source = current = new CancellationTokenSource();
        var previous = Scene == null ? tail : Task.WhenAll(tail, Scene.RetireAsync());
        Scene = null;
        Result = null;
        Set(phase, status);
        var scheduled = ExecuteAsync(previous, source, work);
        tail = scheduled;
        Changed?.Invoke();
        return scheduled;
    }

    async Task ExecuteAsync(Task previous, CancellationTokenSource source, Func<CancellationToken, Task> work)
    {
        try
        {
            await Task.Yield();
            await previous;
            if (!source.IsCancellationRequested) await work(source.Token);
        }
        catch (Exception error)
        {
            if (!source.IsCancellationRequested) Set(QueryPhase.Failed, Explain(error));
        }
        finally
        {
            if (ReferenceEquals(current, source))
            {
                current = null;
                if (Phase == QueryPhase.Stopping) Set(QueryPhase.Stopped, "Query stopped");
                Changed?.Invoke();
            }
            source.Dispose();
        }
    }

    void Set(QueryPhase phase, string message) { Phase = phase; Status = message; }

    static string Explain(Exception e) => e switch
    {
        AuthenticationException => "Authentication failed. Check the username and password.",
        ServiceUnavailableException => "Could not reach Neo4j. Check the address, port and server.",
        Neo4jException neo when neo.Code.Contains("DatabaseNotFound", StringComparison.Ordinal) =>
            "Database not found. Check the database name.",
        Neo4jException neo when neo.Code.EndsWith(".ParameterMissing", StringComparison.Ordinal) =>
            $"{neo.Message} Set the missing value in Parameters (JSON) and run again.",
        System.Text.Json.JsonException json => $"Invalid parameter JSON: {json.Message}",
        _ => e.Message
    };
}
