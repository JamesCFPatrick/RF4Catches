using RF4Catches.Services;

namespace RF4Catches.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/dashboard/session");

        group.MapPost("/new", async (SessionService sessionService, HttpResponse response, CancellationToken ct) =>
        {
            await sessionService.StartNewAsync(ct);
            response.Headers.Append("HX-Trigger", "sessionChanged");
            return Results.NoContent();
        }).DisableAntiforgery();

        group.MapPost("/end", async (SessionService sessionService, HttpResponse response, CancellationToken ct) =>
        {
            await sessionService.EndAndProcessAsync(ct);
            response.Headers.Append("HX-Trigger", "sessionChanged");
            return Results.NoContent();
        }).DisableAntiforgery();

        group.MapPost("/details", async Task<IResult> (
            [Microsoft.AspNetCore.Mvc.FromForm] SessionDetailsForm form,
            SessionService sessionService,
            CancellationToken ct) =>
        {
            await sessionService.UpdateDetailsAsync(form.ToDetails(), ct);
            return Results.NoContent();
        }).DisableAntiforgery();

        // API versions
        app.MapGet("/api/session", (SessionService sessions) =>
            Results.Ok(new { activeSessionId = sessions.ActiveSessionId }));

        app.MapPost("/api/session/start", async (SessionService sessions, CancellationToken ct) =>
            Results.Ok(await sessions.StartAsync(ct)));

        app.MapPost("/api/session/end", async (SessionService sessions, CancellationToken ct) =>
            Results.Ok(await sessions.EndAndProcessAsync(ct)));
        
        app.MapDelete("/api/session/{id:int}", async Task<IResult> (
            int id,
            HttpContext context,
            SessionService sessionService,
            CancellationToken ct) =>
        {
            try
            {
                if (!await sessionService.DeleteSessionAsync(id, ct))
                    return Results.NotFound();

                context.Response.Headers.Append("HX-Trigger",
                    """{"showToast":{"message":"Session deleted","type":"success"}}""");
                return Results.Ok();
            }
            catch (InvalidOperationException ex)
            {
                context.Response.Headers.Append("HX-Trigger",
                    $$$"""{"showToast":{"message":{{{System.Text.Json.JsonSerializer.Serialize(ex.Message)}}},"type":"error"}}""");
                return Results.BadRequest();
            }
        });

        app.MapPost("/api/session/{id:int}/restore", async (int id, SessionService sessionService, CancellationToken ct) =>
        {
            var ok = await sessionService.RestoreSessionAsync(id, ct);
            return ok ? Results.Ok() : Results.NotFound();
        });
    }
}

public sealed class SessionDetailsForm
{
    public string? FishingMethod { get; init; }
    public string? Baits { get; init; }
    public string? LineClip { get; init; }
    public decimal? HookDepthCm { get; init; }
    public string? MapName { get; init; }
    public string? MapCoordinates { get; init; }
    public decimal? CafeSilver { get; init; }
    public decimal? MarketSilver { get; init; }

    public SessionDetails ToDetails() => new(
        Normalize(FishingMethod),
        Normalize(Baits),
        Normalize(LineClip),
        HookDepthCm,
        Normalize(MapName),
        Normalize(MapCoordinates),
        CafeSilver,
        MarketSilver);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}