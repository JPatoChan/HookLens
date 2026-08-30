using HookLens.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapStatusEndpoints();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "HookLens",
    timestampUtc = DateTimeOffset.UtcNow
}));

app.Run();
