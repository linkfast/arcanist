namespace Arcanist.Core.Api.GraphQL;

/// <summary>
/// GraphiQL (MIT) for developers exploring the API, at <c>/graphql/ui</c>. Loaded from jsDelivr with pinned
/// versions and subresource integrity, so it needs internet access on the viewing device; the API itself does not.
/// Paste an access token into the Headers tab as <c>{"Authorization": "Bearer …"}</c>.
/// </summary>
internal static class GraphiQL
{
    private const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Arcanist GraphQL</title>
          <style>body { margin: 0; height: 100vh; } #graphiql { height: 100vh; }</style>
          <link rel="stylesheet" crossorigin="anonymous"
                href="https://cdn.jsdelivr.net/npm/graphiql@4.1.2/graphiql.min.css"
                integrity="sha384-1el1QShFBnnXTx9x/Cprg82hZWKt0ry29OAYIXt05swbi0KcZDFoj0CWguMpopJr">
        </head>
        <body>
          <div id="graphiql">Loading…</div>
          <script crossorigin="anonymous" src="https://cdn.jsdelivr.net/npm/react@18.3.1/umd/react.production.min.js"
                  integrity="sha384-DGyLxAyjq0f9SPpVevD6IgztCFlnMF6oW/XQGmfe+IsZ8TqEiDrcHkMLKI6fiB/Z"></script>
          <script crossorigin="anonymous" src="https://cdn.jsdelivr.net/npm/react-dom@18.3.1/umd/react-dom.production.min.js"
                  integrity="sha384-gTGxhz21lVGYNMcdJOyq01Edg0jhn/c22nsx0kyqP0TxaV5WVdsSH1fSDUf5YJj1"></script>
          <script crossorigin="anonymous" src="https://cdn.jsdelivr.net/npm/graphiql@4.1.2/graphiql.min.js"
                  integrity="sha384-9XdHu7ajgGJziD4J1zNBKQ842Q/+zZfDv+ib1eMhZWBH6hPAs806x+Jbcf0yNO6B"></script>
          <script>
            const fetcher = GraphiQL.createFetcher({
              url: new URL('/graphql', location.href).href,
              subscriptionUrl: new URL('/graphql', location.href).href.replace(/^http/, 'ws'),
            });
            ReactDOM.createRoot(document.getElementById('graphiql'))
              .render(React.createElement(GraphiQL, { fetcher, defaultEditorToolsVisibility: true }));
          </script>
        </body>
        </html>
        """;

    public static IResult Page() => Results.Content(Html, "text/html; charset=utf-8");
}
