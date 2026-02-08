# StockStream API

## What the Project Is
A production-ready .NET 8 Web API for warehouse inventory management, supporting product retrieval by category and transactional purchases with low-stock alerting via a message queue.

## What It Does

- Retrieves products by category with Redis caching for fast responses
- Processes purchases with database transactions to ensure stock accuracy
- Sends low-stock alerts to a RabbitMQ queue when stock falls ≤ 10
- Runs a continuous background worker that consumes and processes alerts from the queue asynchronously

## What Problem It Solves
It solves common warehouse management challenges such as:

- Race conditions and overselling during concurrent purchases
- Slow response times for frequent product listings
- Delayed or missed low-stock notifications
- Tight coupling between purchase processing and alerting

## How It Solves It

- Database transactions with row-level locking prevent overselling
- Redis caching (5-minute TTL) delivers sub-100ms responses with fallback to database
- RabbitMQ decouples alerting: purchase endpoint publishes messages instantly, background worker processes them independently

## Technologies Used

- .NET 8 ASP.NET Core Web API
- PostgreSQL (via Entity Framework Core + Npgsql) – main database
- Redis (StackExchange.Redis) – caching
- RabbitMQ – asynchronous message queue for alerts
- BackgroundService worker for continuous queue consumption

## Endpoints

### 1. GET /api/products/{category}
Retrieves all products in a specified category with Redis caching.

**Request Example:**
```
GET /api/products/electronics
```

**Success Response (200):**
```json
[
  {
    "id": 1,
    "name": "Laptop",
    "category": "electronics",
    "price": 999.99,
    "stockQuantity": 50
  }
]
```

### 2. POST /api/products/buy
Processes a product purchase with transactional stock deduction.

**Request Body:**
```json
{
  "productId": 1,
  "quantity": 3
}
```

**Success Response (200):**
```json
{
  "success": true,
  "newStock": 47,
  "message": "Purchase successful"
}
```

**Insufficient Stock (400):**
```json
{
  "success": false,
  "message": "Insufficient stock. Available: 2"
}
```

## Cloud Services

### 1. PostgreSQL (ElephantSQL)

- Free tier: 20MB
- Connection string format: `Server=xxx.elephantsql.com;Port=5432;Database=xxx;User Id=xxx;Password=xxx;`
- SSL mode: `Require` (sometimes needed)

### 2. Redis (Redis Labs)

- Free tier: 30MB
- Connection string format: `YOUR_HOST:YOUR_PORT,password=YOUR_PASSWORD,ssl=False,abortConnect=False`
- Example: `default:6379,password=abc123xyz,ssl=False,abortConnect=False`

### 3. RabbitMQ (CloudAMQP)

- Free tier: 1M messages/month
- Connection string format: `amqps://username:password@host/vhost`
- Example: `amqps://user123:pass456@chimpanzee.rmq.cloudamqp.com/vhost789`

## Local Development Setup

### 1. Clone Repository
```bash
git clone https://github.com/Akil08/warhouse_api.git
cd warhouse_api
```

### 2. Configure Cloud Services

Edit `StockStream.API/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Server=YOUR_HOST;Port=5432;Database=YOUR_DB;User Id=YOUR_USER;Password=YOUR_PASSWORD",
    "Redis": "YOUR_REDIS_HOST:6379,password=YOUR_PASSWORD,ssl=False,abortConnect=False"
  },
  "RabbitMQ": {
    "ConnectionString": "amqps://YOUR_USER:YOUR_PASS@YOUR_HOST/YOUR_VHOST"
  }
}
```

### 3. Restore Dependencies
```bash
dotnet restore
```

### 4. Run Migrations (Create Database Schema)
```bash
dotnet ef database update --project StockStream.API
```

### 5. Start Application
```bash
dotnet run --project StockStream.API
```

**Expected Output:**
```
╔════════════════════════════════════════╗
║    StockStream Warehouse API Started   ║
╚════════════════════════════════════════╝

📊 ENDPOINTS:
  GET  /api/products/{category}  - Get products by category (Redis cached)
  POST /api/products/buy         - Purchase product (transactional)

🔧 BACKGROUND SERVICES:
  ✓ AlertWorker - Listening to CloudAMQP queue for low-stock alerts

📍 API Documentation: /swagger/ui
```

Navigate to `https://localhost:5001/swagger/ui` to view interactive API docs.

## Key Architectural Decisions

### 1. Database Transactions
- Prevents race conditions (overselling)
- Uses PostgreSQL row-level locks
- Automatic rollback on error
- Isolation level: Read Committed

### 2. Redis Caching
- 5-minute TTL for category listings
- Reduces database load
- Improves response time
- Graceful degradation on cache miss

### 3. Async RabbitMQ
- Decouples alert processing from API response
- Non-blocking purchase endpoint
- Fault-tolerant (messages persist in queue)
- Background worker runs 24/7

### 4. Clean Architecture
- Services handle business logic
- Controllers only handle HTTP
- Dependency injection for testability
- Interface-based design

## Testing API Manually

### Using curl

**Get products:**
```bash
curl https://localhost:5001/api/products/electronics
```

**Buy product:**
```bash
curl -X POST https://localhost:5001/api/products/buy \
  -H "Content-Type: application/json" \
  -d '{"productId": 1, "quantity": 2}'
```

### Using Postman

1. Import collection from Swagger: `https://localhost:5001/swagger/v1/swagger.json`
2. Or manually create requests:
   - **GET** `https://localhost:5001/api/products/electronics`
   - **POST** `https://localhost:5001/api/products/buy`

### Using VS Code REST Client

Create `test.http`:
```http
GET https://localhost:5001/api/products/electronics

###

POST https://localhost:5001/api/products/buy
Content-Type: application/json

{
  "productId": 1,
  "quantity": 2
}
```

## Troubleshooting

### Redis Connection Error
```
Connection failed: SocketFailure: The operation timed out
```

**Solution:**
1. Check Redis Labs dashboard - database is running
2. Verify connection string (host, port, password)
3. Check SSL settings (Redis Labs uses SSL by default)

### PostgreSQL Connection Error
```
Server does not exist or access denied
```

**Solution:**
1. Check ElephantSQL dashboard - instance is running
2. Verify connection string credentials
3. Ensure IP whitelist allows your connection

### RabbitMQ Connection Error
```
The response indicates a failure
```

**Solution:**
1. Check CloudAMQP dashboard - instance is running
2. Verify full URL with username, password, host
3. Check vhost exists (usually auto-created)

### Migrations Not Applied
```
Unable to apply database migrations
```

**Solution:**
```bash
# Drop and recreate (be careful!)
dotnet ef database drop --project StockStream.API -f

# Reapply migrations
dotnet ef database update --project StockStream.API
```

## Database Schema

```sql
-- Products table (auto-created by EF Core)
CREATE TABLE "Products" (
    "Id" INTEGER PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Category" TEXT NOT NULL,
    "Price" NUMERIC NOT NULL,
    "StockQuantity" INTEGER NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL,
    "UpdatedAt" TIMESTAMP NOT NULL
);

-- Seeded data
INSERT INTO "Products" VALUES
(1, 'Laptop', 'electronics', 999.99, 50, now(), now()),
(2, 'Mouse', 'electronics', 29.99, 5, now(), now()),
(3, 'Keyboard', 'electronics', 79.99, 8, now(), now()),
(4, 'Office Chair', 'furniture', 299.99, 15, now(), now()),
(5, 'Desk', 'furniture', 499.99, 3, now(), now());
```

## Deployment

### Build Release
```bash
dotnet publish -c Release -o ./Publish
```

### Environment Variables (Production)
```bash
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__PostgreSQL="..."
export ConnectionStrings__Redis="..."
export RabbitMQ__ConnectionString="..."
```

### Run Production Image
```bash
dotnet StockStream.API.dll
```

## License

MIT License - Feel free to use for learning and portfolio projects

## 👨‍💻 Author

Created as a portfolio project demonstrating:
- Cloud-native architecture
- Database transactions and optimization
- Message-driven design
- Clean code practices
- Production-ready API design

---

**Last Updated:** February 2026
**Version:** 1.0.0
