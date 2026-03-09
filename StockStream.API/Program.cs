using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using StockStream.API.Data;
using StockStream.API.Services.Implementations;
using StockStream.API.Services.Interfaces;
using StockStream.API.Workers;

var builder = WebApplicationBuilder.CreateBuilder(args);

// ⭐ LOGGING SETUP

builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
});

// ⭐ CONTROLLERS
builder.Services.AddControllers();

// ⭐ SWAGGER/OPENAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "StockStream Warehouse API",
        Version = "v1",
        Description = "Warehouse management system with Redis caching, PostgreSQL transactions, and RabbitMQ alerts"
    });
});

// ⭐ DATABASE CONTEXT - POSTGRESQL
// Uses ElephantSQL cloud connection from appsettings.json
var postgresConnection = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? throw new InvalidOperationException("PostgreSQL connection string 'PostgreSQL' not found");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(postgresConnection));


try
{
    var redisConnection = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Redis connection string 'Redis' not found");

    var redis = ConnectionMultiplexer.Connect(redisConnection);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    Console.WriteLine("[Startup] ✓ Redis connected successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"[Startup] ⚠️  Redis connection warning: {ex.Message}");
    Console.WriteLine("[Startup] The application will start but caching will be unavailable");
}


// both reids and que should be singleton , ok ! 

builder.Services.AddSingleton<IRedisService, RedisService>();
builder.Services.AddSingleton<IRabbitMQService, RabbitMQService>();
builder.Services.AddScoped<IWarehouseService, WarehouseService>();

builder.Services.AddHostedService<AlertWorker>();

//  CORS POLICY (Optional - allow cross-origin requests)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAny", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// APPLY MIGRATIONS ON STARTUP (Create database schema if not exists)


using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        Console.WriteLine("[Startup] Applying database migrations...");
        await dbContext.Database.MigrateAsync();
        Console.WriteLine("[Startup] ✓ Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] ⚠️  Migration warning: {ex.Message}");
    }
}

// ⭐ MIDDLEWARE PIPELINE

// Enable HTTPS redirection
if (app.Environment.IsDevelopment())
{
    // Swagger UI in development
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "StockStream v1"));
}

app.UseHttpsRedirection();

// CORS
app.UseCors("AllowAny");

// Authorization
app.UseAuthorization();

// Map controllers
app.MapControllers();


await app.RunAsync();

