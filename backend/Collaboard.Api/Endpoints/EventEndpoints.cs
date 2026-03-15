using Collaboard.Api.Events;

namespace Collaboard.Api.Endpoints;

internal static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/boards/{boardId:guid}/events", async (BoardEventBroadcaster broadcaster, HttpContext http, Guid boardId, CancellationToken ct) =>
        {
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers.Connection = "keep-alive";

            var reader = broadcaster.Subscribe(boardId);

            try
            {
                await foreach (var eventType in reader.ReadAllAsync(ct))
                {
                    await http.Response.WriteAsync($"event: {eventType}\ndata: {{}}\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected
            }
            finally
            {
                broadcaster.Unsubscribe(boardId, reader);
            }
        });

        return app;
    }
}
