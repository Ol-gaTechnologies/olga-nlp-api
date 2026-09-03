using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Olga.Nlp.Application;
using Olga.Nlp.Contracts;
using Olga.Nlp.Domain;
using Olga.Nlp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
var connection = builder.Configuration.GetConnectionString("AzureSql");
if (!string.IsNullOrWhiteSpace(connection)) builder.Services.AddDbContext<NlpDbContext>(o => o.UseSqlServer(connection));
else builder.Services.AddDbContext<NlpDbContext>(o => o.UseInMemoryDatabase("olga-nlp-local"));
builder.Services.AddSingleton<IPiiChecker, PiiChecker>();
builder.Services.AddSingleton<ITextNormalizer, TextNormalizer>();
builder.Services.AddSingleton<IEmbeddingProvider, FakeEmbeddingProvider>();
builder.Services.AddSingleton<IReciprocalScorer, ReciprocalScorer>();
builder.Services.AddSingleton<IExplanationGenerator, ExplanationGenerator>();
builder.Services.AddSingleton<IMatchRanker, MatchRanker>();
builder.Services.AddScoped<ICandidateRepository, CandidateRepository>();
builder.Services.AddScoped<IMatchResultRepository, MatchResultRepository>();
builder.Services.AddScoped<IMatchingService>(sp => new MatchingService(sp.GetRequiredService<ICandidateRepository>(), sp.GetRequiredService<IMatchResultRepository>(), sp.GetRequiredService<ITextNormalizer>(), sp.GetRequiredService<IEmbeddingProvider>(), sp.GetRequiredService<IReciprocalScorer>(), sp.GetRequiredService<IMatchRanker>(), new RankingConfig("ranking-v1")));
builder.Services.AddHealthChecks();
var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Correlation-Id"] = ctx.TraceIdentifier;
    if (ctx.Request.ContentLength is > 256_000) { ctx.Response.StatusCode = 413; await ctx.Response.WriteAsJsonAsync(new { type = "about:blank", title = "Payload too large", status = 413, code = "PAYLOAD_TOO_LARGE" }); return; }
    var expected = app.Configuration["ServiceAuthorization__Token"];
    if (!string.IsNullOrWhiteSpace(expected) && !string.Equals(ctx.Request.Headers["X-Service-Token"], expected, StringComparison.Ordinal)) { ctx.Response.StatusCode = 401; await ctx.Response.WriteAsJsonAsync(new { type = "about:blank", title = "Unauthorized", status = 401, code = "UNAUTHORIZED" }); return; }
    await next();
});
app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapGet("/ready", async (NlpDbContext db, CancellationToken ct) => { try { await db.Database.CanConnectAsync(ct); return Results.Ok(new { status = "ready" }); } catch { return Results.StatusCode(503); } });

app.MapPost("/v1/matches/search", async Task<Results<Ok<MatchSearchResponse>, ProblemHttpResult>> (MatchSearchRequest request, IMatchingService service, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.MemberId) || string.IsNullOrWhiteSpace(request.IntentId) || string.IsNullOrWhiteSpace(request.ContextId) || request.Limit is < 3 or > 7) return TypedResults.Problem("Invalid match search request.", statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = "INVALID_REQUEST" });
    try { return TypedResults.Ok(await service.SearchAsync(request, ct)); } catch (InvalidOperationException e) { return TypedResults.Problem(e.Message, statusCode: 404, extensions: new Dictionary<string, object?> { ["code"] = e.Message }); }
});
app.MapPost("/v1/matches/score-pair", async (ScorePairRequest request, IMatchingService service, CancellationToken ct) => Results.Ok(await service.ScorePairAsync(request, ct)));
app.MapPost("/v1/normalize", (NormalizeRequest request, ITextNormalizer normalizer, IPiiChecker pii) => { var x = normalizer.Normalize(request.Text, request.Language); return Results.Ok(new NormalizeResponse(x.Value, x.Language, x.Hash, x.ContainsPii)); });
app.MapPost("/v1/feedback", async (FeedbackRequest request, IMatchResultRepository repo, CancellationToken ct) => { await repo.SaveFeedbackAsync(request.RequestId, request.RequesterId, request.CandidateId, request.Label, request.Reason, ct); return Results.Accepted(); });
app.Run();

public partial class Program { }
