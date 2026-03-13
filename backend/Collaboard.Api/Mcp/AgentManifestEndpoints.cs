namespace Collaboard.Api.Mcp;

public static class AgentManifestEndpoints
{
    public static IEndpointRouteBuilder MapAgentManifest(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mcp", () =>
        {
            return Results.Ok(new
            {
                name = "collaboard",
                protocol = "modelcontextprotocol",
                description = "MCP-facing endpoint metadata for AI agents.",
                tools = new[]
                {
                    "get_board",
                    "create_card",
                    "update_card",
                    "add_comment",
                    "move_card",
                },
            });
        });

        return app;
    }
}
