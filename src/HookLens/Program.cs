using HookLens.Endpoints;
using HookLens.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IWebhookCaptureStore, InMemoryWebhookCaptureStore>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapStatusEndpoints();
app.MapWebhookEndpoints();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "HookLens",
    timestampUtc = DateTimeOffset.UtcNow
}));

app.Run();

public partial class Program
{
}
