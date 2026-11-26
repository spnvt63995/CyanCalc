# CyanGraph

CyanGraph provides a read-only Cypher workspace for Neo4j. The standalone window and the OneWare **View → CyanGraph** document host the same editor, results and inspector from `ui/cyangraph`. Each host provides its own query workspace and question profile. Start it from the repository root with the .NET SDK specified in `global.json`.

```sh
dotnet restore CyanGraph.slnx --locked-mode
dotnet run --project apps/cyangraph/CyanGraph.csproj
```

## Query a database

1. Open **Connection → Connection settings…**, enter a Neo4j or Bolt URI, database, username and password, then choose **Connect**. The header shows the verified database and host. The password is held in memory for the connection.
2. Enter Cypher under **Statement** and a JSON object under **Parameters**. Choose **Run** or press **⌘ Enter** on macOS or **Ctrl Enter**. With the official Movies dataset, this query returns Carrie-Anne Moss, her role and the film.

```cypher
MATCH (actor:Person {name: $actor})-[role:ACTED_IN]->(film:Movie {title: $film})
RETURN actor, role, film
```

```json
{"actor":"Carrie-Anne Moss","film":"The Matrix"}
```

3. Select the **Person** circle to read `born = 1967`, the **Movie** circle to read `released = 1999`, or the **ACTED_IN** arrow to read `roles = ["Trinity"]`. Choose **Table** to inspect the original rows and select any cell for its full value. The table presents up to 200 rows. A 201st row changes the status to **First 200 rows · more available**. A successful empty result keeps its column headings.
4. Choose **Stop** to interrupt an active query. Run a new query or change the database to replace the previous result. **Connection → Disconnect** releases the driver and clears the table.

## Explore a graph

Results containing nodes, relationships or paths open in **Graph**. Drag the canvas to pan, scroll at the pointer to zoom, or use **−**, **+** and **Fit**. Select a node, an arrow or its type label to open the inspector on the right. It shows all returned node labels, relationship endpoints and properties. **Full value…** opens lists and long text in a selectable, scrollable window. Click the background to return to the graph overview.

The view collects graph elements from returned columns, paths, lists and maps using their element IDs within this result. Repeated nodes appear once. Distinct relationships between the same endpoints keep separate arrows, including reverse and self relationships. A relationship returned on its own supplies endpoint IDs. Return the endpoint nodes or a path to include their labels and properties. [Neo4j driver graph types](https://neo4j.com/docs/dotnet-manual/current/data-types/)

### Expand and restore a neighborhood

Run the following Movies query and right-click the **The Matrix** node. Choose **Expand neighbors** to read its incoming and outgoing relationships and endpoint nodes from the connected database. The inspector also offers this action. Each page contains up to ten relationships. If there are more, choose **Load next 10**. The inspector shows how many have been loaded. A completed expansion keeps one copy of each node and relationship.

```cypher
MATCH (movie:Movie {title:'The Matrix'}) RETURN movie
```

The graph now shows nine nodes and eight relationships. The original **Table** and **Raw** still contain the single movie row. The inspector distinguishes elements from the query and neighbor expansion. Right-click **Carrie-Anne Moss** and choose **Hide node** to hide her and her credit. **Hide relationship** hides an edge alone. The graph toolbar and the background menu offer **Undo hide** in reverse order. The background menu previews the number of nodes and relationships that will return.

To check the database while keeping this graph open, enter a second statement and choose **Query → Run in separate window…**. Closing that result returns to the first graph with its current visibility. Running a new main query or switching databases starts a fresh graph.

## Link a graph and table

Run this Movies query, then choose **Graph + Table**.

```cypher
MATCH (actor:Person)-[credit:ACTED_IN]->(film:Movie {title:'The Matrix'})
RETURN actor, credit, credit.roles AS roles, film ORDER BY actor.name
```

Click **Carrie-Anne Moss** in the first actor cell to select the same node and read its properties. Click her **ACTED_IN** arrow on the canvas to highlight her one credit cell. Click **The Matrix** to highlight all five film cells. The matching-cell count includes off-screen cells. Clicking a scalar cell shows its value and clears the graph selection. On narrow windows the graph sits above the table.

The links use element kind and ID within the current result. A path, list or map cell is highlighted once for each selected element it contains. A directly returned node or relationship can be picked from its cell. Starting another query or changing the connection clears the selection.

**Graph**, **Table**, **Graph + Table** and **Raw** use the same fetched records. **Raw** shows selectable JSON with ordered `columns`, positional `rows`, and `hasMore` for the 200-row preview. Nodes include their labels, element IDs and properties. Relationships include their type and both endpoint IDs. Results containing only ordinary values offer **Table** and **Raw**.

## Run history and saved questions

Enter a Cypher statement under **Statement** and its bindings under **Parameters (JSON)**. The header shows the active database and host. For example, enter this Movies question and use `{"actor":"Keanu Reeves"}`, then change the value to `{"actor":"Tom Hanks"}`. Run the same statement for each actor.

```cypher
MATCH (a:Person {name: $actor})-[:ACTED_IN]->(m:Movie)<-[:ACTED_IN]-(b:Person)
WHERE a <> b
RETURN b.name AS actor, count(DISTINCT m) AS films
ORDER BY films DESC, actor
```

**Questions → Run history** lists the 100 most recent submissions with their database and time, including runs in a separate window. Pick a row to load its statement. **Questions → Save question…** stores the editor text under a name. Open it from **Saved questions**, even after restarting the app. Both lists load the statement into an empty result view and keep the current database and parameter editor. Choose **Run** to fetch rows. Editing the statement or its parameters after a run marks the displayed result as belonging to the previous input.

Opening a saved question and adding `LIMIT 1` to the Tom Hanks query returns Meg Ryan and three films. Remove the limit and run again to restore all 34 rows. Use **Save question… → Save changes** to update the stored name or statement. The desktop library is `CyanGraph/questions.json` under local application data. Use `--profile FILE` to use a different library. Bound values, result rows and connection settings are held in the window.

## Export query results

Choose **Results → Export → CSV…** or **JSON…** and select a local file. The menu shows how many rows and columns will be written. If the 200-row preview limit was reached and more rows exist, the menu and save confirmation show **First 200 rows only**. The suggested filename also includes this range. All views, including a separate result window, export the records from their own last query in return-column order.

CSV uses UTF-8 and a header row. Fields containing commas, quotes or line breaks are quoted. An unquoted `\N` represents a null, `""` an empty string. A non-null field beginning with a backslash gains one leading backslash. Node and relationship fields include their properties.

JSON contains an array with one object per row, keyed by column name. Node objects include labels, properties and the element ID. Relationships carry their Cypher type, properties, start node element ID and end node element ID. A result with zero rows produces a header-only CSV and an empty JSON array.

## Data and connection behavior

`CyanGraph.Neo4j` returns ordered column names and native Neo4j driver records. Node and relationship element IDs, relationship endpoints, paths, maps, lists, temporal values and nulls remain available after a query finishes.

The client opens a read-mode session, rolls back its preview transaction and uses a 30-second transaction timeout. Stop identifies its own transaction by request metadata and terminates it using a second session. When connecting to a server that other people use, select an account or database with server-enforced read-only access. [Neo4j driver transactions](https://neo4j.com/docs/dotnet-manual/current/transactions/)

## Checks

```sh
dotnet run --project tests/CyanGraph.Checks
dotnet run --project tests/CyanGraph.Checks -- --movies
```

The second command needs a read-only database populated from the official Movies fixture. Set `CYANGRAPH_TEST_URI`, `CYANGRAPH_TEST_DATABASE`, `CYANGRAPH_TEST_USER` and `CYANGRAPH_TEST_PASSWORD` in the test process environment. The live checks cover connection retries, native values, row limits, cancellation on the server, overlapping requests and a switch to the system database. [Movies fixture](https://github.com/neo4j-graph-examples/movies/blob/51cf90d18c1a7f74bce7a77083543697dfb0139d/scripts/movies.cypher)

To check parallel relationships of the same type, a reverse edge, a self-loop and full Korean property values, load the graph examples once into a disposable Movies copy before setting that database to read-only. Run the same checks with `--graph-examples` in place of `--movies`. [Graph example seed](../../tests/CyanGraph.Checks/graph-examples.cypher)

To exercise two nodes with the same caption, also load the selection examples into that copy before enabling read-only access, then run with `--selection-examples`. [Same-name example seed](../../tests/CyanGraph.Checks/selection-examples.cypher)

Use `--northwind` with the same environment variables pointing to a read-only Northwind fixture to check the Chai export, quoted multiline CSV fields, JSON types and preview bounds. [Official Northwind source](https://github.com/neo4j-graph-examples/northwind/tree/56ee0507d33eecd8ef2a9c960ec967cd432eb57e/import)
