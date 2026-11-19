using ArchiveExplorer.Index;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ArchiveExplorer.Web;

public static class Api
{
    public static void Map(IEndpointRouteBuilder app, IndexState state, MediaFiles media, UiFiles ui)
    {
        app.MapGet("/api/status", () => Results.Json(new
        {
            state = state.State,
            filesDone = state.FilesDone,
            filesTotal = state.FilesTotal,
            chats = state.Result?.Chats ?? 0,
            messages = state.Result?.Messages ?? 0,
        }));

        app.MapGet("/api/chats", (string? q) => WithQueries(state, queries => Results.Json(queries.ListChats(q))));

        app.MapGet("/api/chats/{id:long}/messages", (long id, long? before, long? after, long? around, long? aroundTs, int? limit) =>
            WithQueries(state, queries =>
                ChatExists(queries, id)
                    ? Results.Json(queries.Messages(id, before, after, around, aroundTs, limit ?? 80))
                    : Results.NotFound()));

        app.MapGet("/api/chats/{id:long}/search", (long id, string? q) =>
            WithQueries(state, queries => Results.Json(queries.SearchInChat(id, q ?? ""))));

        app.MapGet("/api/chats/{id:long}/media", (long id, int? offset, int? limit) =>
            WithQueries(state, queries => Results.Json(queries.Media(id, offset ?? 0, limit ?? 60))));

        app.MapGet("/api/chats/{id:long}/avatar", (long id) =>
            WithQueries(state, queries => queries.AvatarPath(id) is { } path ? media.Serve(path) : Results.NotFound()));

        app.MapGet("/api/search", (string? q, long? chat, bool? fromMe, long? from, long? to, bool? media, int? offset, int? limit) =>
            WithQueries(state, queries =>
            {
                var (hits, total) = queries.Search(q ?? "", chat, fromMe, from, to, media, offset ?? 0, limit ?? 50);
                return Results.Json(new { hits, total });
            }));

        app.MapGet("/media/{**path}", (HttpContext context) =>
            media.Serve(context.Request.Path.Value!["/media/".Length..]));

        app.MapFallback((HttpContext context) =>
            context.Request.Path.StartsWithSegments("/api") ? Results.NotFound() : ui.Serve(context.Request.Path.Value ?? "/"));
    }

    private static bool ChatExists(Queries queries, long id) =>
        queries.Messages(id, null, null, null, null, 1).Items.Count > 0 || queries.ListChats(null).Any(c => c.Id == id);

    private static IResult WithQueries(IndexState state, Func<Queries, IResult> action)
    {
        if (!state.IsReady)
        {
            return Results.Json(new { state = state.State }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        using var connection = state.OpenReader();
        return action(new Queries(connection));
    }
}
