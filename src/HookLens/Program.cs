using HookLens.Data;
using HookLens.Endpoints;
using HookLens.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("HookLens") ?? "Data Source=hooklens.db";

builder.Services.AddDbContext<HookLensDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<IWebhookCaptureStore, SqliteWebhookCaptureStore>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<HookLensDbContext>();
    dbContext.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseDefaultFiles();
app.UseStaticFiles();

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
