# StockStream Warehouse Management System

A production-ready .NET 8 Web API for warehouse management with cloud-based PostgreSQL, Redis caching, and RabbitMQ messaging.

## 🚀 Features

- **GET /api/products/{category}** - Retrieve products with Redis caching
  - 5-minute cache duration
  - Automatic database fallback on cache miss
  - Fast response times (<100ms from cache)

- **POST /api/products/buy** - Process purchases with transactions
  - Database-level transactions prevent race conditions
  - Automatic low-stock alerts
  - Real-time stock updates

- **Background Worker** - Continuous RabbitMQ message processing
  - Listens to CloudAMQP queue 24/7
  - Processes low-stock alerts asynchronously
  - Handles failures gracefully

## 🛠️ Technology Stack

| Technology | Purpose | Tier |
|-----------|---------|------|
| **.NET 8** | Web API framework | Language |
| **PostgreSQL** | Relational database | ElephantSQL cloud (20MB free) |
| **Redis** | In-memory cache | Redis Labs cloud (30MB free) |
| **RabbitMQ** | Message broker | CloudAMQP cloud (1M msgs/month free) |
| **Entity Framework Core** | ORM with migrations | Data access |

## 📋 Prerequisites

- .NET 8 SDK or higher
- Git
- Text editor or Visual Studio Code

## ⚙️ Cloud Services Setup

### 1. PostgreSQL (ElephantSQL)

1. Go to https://www.elephantsql.com
2. Sign up for free account
3. Click "Create New Instance"
4. Select free tier (20MB)
5. Choose region closest to you
6. Copy the connection string (URL)
7. Connection format: `Server=xxx.elephantsql.com;Port=5432;Database=xxx;User Id=xxx;Password=xxx;`

**Note:** ElephantSQL requires disabling SSL verification in some cases:
```
Server=xxx.elephantsql.com;Port=5432;Database=xxx;User Id=xxx;Password=xxx;SSL Mode=Require;
```

### 2. Redis (Redis Labs)

1. Go to https://app.redislabs.com
2. Sign up for free account
3. Click "Create" → "Databases"
4. Select free tier (30MB)
5. Choose region
6. Connection format: `YOUR_HOST:YOUR_PORT,password=YOUR_PASSWORD,ssl=False,abortConnect=False`

**Example:**
```
default:6379,password=abc123xyz,ssl=False,abortConnect=False
```

### 3. RabbitMQ (CloudAMQP)

1. Go to https://www.cloudamqp.com
2. Sign up for free account
3. Click "Create Instance"
4. Select "Little Lemur" free plan
5. Choose region
6. Get connection URL: `amqps://username:password@host/vhost`

**Example:**
```
amqps://user123:pass456@chimpanzee.rmq.cloudamqp.com/vhost789
```

## 🔧 Local Development Setup

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

## 📡 API Endpoints

### GET /api/products/{category}

Retrieve all products in a category with Redis caching.

**Request:**
```
GET /api/products/electronics
```

**Response (200 OK):**
```json
[
  {
    "id": 1,
    "name": "Laptop",
    "category": "electronics",
    "price": 999.99,
    "stockQuantity": 50
  },
  {
    "id": 2,
    "name": "Mouse",
    "category": "electronics",
    "price": 29.99,
    "stockQuantity": 5
  }
]
```

**How It Works:**
1. API checks Redis cache for key `products:electronics`
2. If found: Return immediately from cache (< 100ms)
3. If miss: Query PostgreSQL database
4. Cache results in Redis for 5 minutes
5. Return JSON response

---

### POST /api/products/buy

Process a purchase with database transaction and optional alert.

**Request:**
```
POST /api/products/buy
Content-Type: application/json

{
  "productId": 1,
  "quantity": 3
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "newStock": 47,
  "message": "Purchase successful"
}
```

**Response (400 Bad Request - Insufficient Stock):**
```json
{
  "success": false,
  "newStock": null,
  "message": "Insufficient stock. Available: 2"
}
```

**How It Works:**
1. Validate request (quantity > 0, productId valid)
2. **BEGIN TRANSACTION** - Lock product row in PostgreSQL
3. Load product from database (locked)
4. Check if stock >= quantity
5. Deduct quantity from stock
6. **COMMIT TRANSACTION** - Permanent change, lock released
7. If new stock ≤ 10:
   - Send message to CloudAMQP `low_stock_alerts` queue
   - BackgroundWorker receives and processes asynchronously
8. Return result

**Race Condition Prevention:**

Without transaction: Two buyers of last item = overselling ❌

With transaction:
```
Buyer A: START TRANSACTION → LOCK PRODUCT ROW
Buyer B: START TRANSACTION → WAIT FOR LOCK
Buyer A: Check stock (1) → Deduct → COMMIT → LOCK RELEASED
Buyer B: CHECK STOCK (0) → FAIL → ROLLBACK
Result: Only one purchase, no overselling ✓
```

---

## 🔄 Message Flow (RabbitMQ)

```
┌──────────────────────────────────────────────┐
│  POST /api/products/buy                      │
│  - Process transaction                       │
│  - Stock drops to 8 (below 10)               │
└──────────────┬───────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────┐
│  RabbitMQService.SendLowStockAlertAsync()    │
│  - Create alert object                       │
│  - Serialize to JSON                         │
│  - Convert to bytes                          │
│  - Publish to queue                          │
└──────────────┬───────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────┐
│  CloudAMQP Queue: low_stock_alerts           │
│  - Message stored in cloud                   │
│  - Persistent until processed                │
│  - Available for consumers                   │
└──────────────┬───────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────┐
│  AlertWorker (Background Service)            │
│  - Listening continuously 24/7               │
│  - Receives message from CloudAMQP           │
│  - Deserializes JSON                         │
│  - Processes alert (logs, email, etc)        │
│  - Acknowledges to RabbitMQ                  │
│  - Message removed from queue                │
└──────────────────────────────────────────────┘
```

## 🏗️ Project Structure

```
warhouse_api/
├── StockStream.API/
│   ├── Controllers/
│   │   └── ProductsController.cs          # HTTP endpoints
│   ├── Services/
│   │   ├── Interfaces/
│   │   │   ├── IRedisService.cs
│   │   │   ├── IWarehouseService.cs
│   │   │   └── IRabbitMQService.cs
│   │   └── Implementations/
│   │       ├── RedisService.cs            # Cache operations
│   │       ├── WarehouseService.cs        # Business logic + transactions
│   │       └── RabbitMQService.cs         # Message publishing
│   ├── Workers/
│   │   └── AlertWorker.cs                 # Background message processor
│   ├── Data/
│   │   ├── AppDbContext.cs                # EF Core context
│   │   └── Migrations/                    # Database schema
│   ├── Models/
│   │   └── Product.cs
│   ├── DTOs/
│   │   ├── BuyRequestDto.cs
│   │   └── ProductResponseDto.cs
│   ├── Program.cs                         # Dependency injection, middleware
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   └── StockStream.API.csproj
├── .gitignore
├── README.md
└── StockStream.sln
```

## 🔐 Key Architectural Decisions

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

## 🧪 Testing API Manually

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

## 🐛 Troubleshooting

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

## 📚 Database Schema

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

## 🚀 Deployment

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

## 📄 License

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
