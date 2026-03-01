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



// ⭐ REDIS CONNECTION - REDIS LABS CLOUD
// Singleton: Only one connection shared across application

// why redis is singleton?
// Because creating multiple connections to Redis can lead to
// resource exhaustion and performance issues. 
// A single, shared connection allows for efficient reuse 
// and better performance when accessing the cache.

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

// ⭐ SERVICE REGISTRATION

// both reids and que should be singleton , ok ! 

builder.Services.AddSingleton<IRedisService, RedisService>();
builder.Services.AddSingleton<IRabbitMQService, RabbitMQService>();
builder.Services.AddScoped<IWarehouseService, WarehouseService>();

// ⭐ BACKGROUND WORKER - RABBITMQ MESSAGE CONSUMER
// Runs continuously listening to CloudAMQP queue
builder.Services.AddHostedService<AlertWorker>();

// ⭐ CORS POLICY (Optional - allow cross-origin requests)
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

// ⭐ APPLY MIGRATIONS ON STARTUP (Create database schema if not exists)

// code first approach 


// so if we forget to create the db , below code does it auto create ? 
// Yes, the code uses Entity Framework Core's migration system to automatically
// apply any pending migrations to the database when the application starts. 
//If the database does not exist, it will be created based on the migrations 
//defined in your project. This ensures that the database schema is up-to-date 
//with your application's data model without requiring manual intervention.


// does that means with the migratin code , we can just create db 
// and never manually create tables or schema ?
// Yes, with Entity Framework Core's migration system, you can define your data model
// using C# classes (known as entities) and then create migrations that represent changes

// u r taling about changes but i am talking full db creation with tables and schema !
// Yes, the migration system can handle both the initial creation of the database and 
//its schema, as well as any subsequent changes. When you create an initial migration 
//(e.g., "InitialCreate"), it will generate the necessary SQL to create the database, 
//tables, and schema based on your defined data model. When you run the application, 
//it will apply this migration, effectively creating the entire database structure 
//without any manual SQL scripting needed.

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



// Model → What is the data?

// Data → Where is it stored?

// DTO → How is it formatted for the user?

// Service → What are the rules?

// Controller → How do we trigger the rules?

// Worker → What happens automatically in the background?