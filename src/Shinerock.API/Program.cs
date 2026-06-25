using Scalar.AspNetCore;
using Shinerock.Application;
using Shinerock.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

// Register Clean Architecture layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// API docs — available in all environments
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.Title = "Jellyfin AI Playlist Maker";
    options.Theme = ScalarTheme.BluePlanet;
});

app.UseCors();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
