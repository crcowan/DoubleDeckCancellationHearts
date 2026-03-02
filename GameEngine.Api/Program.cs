using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GameEngine.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

// Explicitly bind to the port React expects, bypassing launchSettings when run as an exe
builder.WebHost.UseUrls("http://localhost:5243");

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder =>
        {
            builder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSingleton<GameEngine.Api.Services.GameSessionManager>();
builder.Services.AddTransient<GameEngine.Api.Services.GameLogicService>();
builder.Services.AddTransient<GameEngine.Api.Services.AiService>();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowAll");

// Serve React static files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

// API to gracefully shutdown the server from the React UI
app.MapPost("/api/system/shutdown", (IHostApplicationLifetime lifetime) =>
{
    // Run shutdown on background thread to let HTTP response complete
    _ = Task.Run(async () =>
    {
        await Task.Delay(500);
        lifetime.StopApplication();
    });
    return Results.Ok(new { message = "Shutting down..." });
});

// Fallback all unknown non-API routes to index.html to support React Router natively (if added later)
app.MapFallbackToFile("/index.html");

            app.Run();
        }
    }
}
