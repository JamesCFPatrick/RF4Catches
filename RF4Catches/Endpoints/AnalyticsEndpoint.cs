using RF4Catches.Services;

namespace RF4Catches.Endpoints;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/analytics", () => Results.Redirect("/analytics.html"));

        app.MapGet("/analytics/content", async (
            HistoryDashboardService dashboard,
            CancellationToken ct) =>
        {
            var data = await dashboard.GetDashboardAsync(ct);
            return Results.Content(HistoryDashboardMarkup.RenderPage(data), "text/html");
        });

        app.MapGet("/api/history/dashboard", async (
            HistoryDashboardService dashboard,
            CancellationToken ct) =>
        {
            var data = await dashboard.GetDashboardAsync(ct);
            return Results.Ok(data);
        });
    }
}