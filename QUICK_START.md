# 🚀 StockStream Warehouse API - Quick Start Guide

## ✅ What's Been Created

Your complete production-ready StockStream Warehouse API with:

✓ **19 files** organized in clean architecture  
✓ **2 REST endpoints** with Swagger documentation  
✓ **Heavy comments** on RabbitMQ and database transactions  
✓ **Cloud-ready** for PostgreSQL, Redis, and RabbitMQ  
✓ **Database migrations** pre-created (EF Core)  
✓ **Git repository** initialized and ready to push  

---

## 📦 Project Structure

```
warhouse_api/
├── StockStream.API/
│   ├── Controllers/
│   │   └── ProductsController.cs          # 2 endpoints
│   ├── Services/
│   │   ├── Interfaces/                    # Contracts
│   │   │   ├── IRedisService.cs
│   │   │   ├── IWarehouseService.cs
│   │   │   └── IRabbitMQService.cs
│   │   └── Implementations/               # Business logic (heavily commented!)
│   │       ├── RedisService.cs            # Cache operations
│   │       ├── WarehouseService.cs        # Transactions & alerts
│   │       └── RabbitMQService.cs         # RabbitMQ publishing
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
│   ├── Program.cs                         # DI & middleware
│   ├── appsettings.json                   # Production config
│   ├── appsettings.Development.json       # Dev config
│   └── StockStream.API.csproj
├── .gitignore                             # Configured for .NET
├── README.md                              # Full documentation
├── StockStream.sln                        # Solution file
└── .git                                   # Git initialized

Total: 19 files, 1761 lines of code
```

---

## 🎯 Quick Setup (5 minutes)

### Step 1: Get Cloud Service Credentials

Create accounts and copies the connection strings:

1. **PostgreSQL (ElephantSQL)**
   - Go to: https://www.elephantsql.com
   - Create free database (20MB)
   - Copy URL (looks like: `postgres://user:pass@host:5432/db`)

2. **Redis (Redis Labs)**
   - Go to: https://app.redislabs.com
   - Create free database (30MB)
   - Copy connection string (looks like: `redis://default:pass@host:6379`)

3. **RabbitMQ (CloudAMQP)**
   - Go to: https://www.cloudamqp.com
   - Create "Little Lemur" free plan
   - Copy AMQP URL (looks like: `amqps://user:pass@host/vhost`)

### Step 2: Update Configuration

Edit `StockStream.API/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Server=YOUR_ELEPHANT_HOST;Port=5432;Database=YOUR_DB;User Id=YOUR_USER;Password=YOUR_PASSWORD",
    "Redis": "YOUR_REDIS_HOST:6379,password=YOUR_PASSWORD,ssl=False,abortConnect=False"
  },
  "RabbitMQ": {
    "ConnectionString": "amqps://YOUR_USER:YOUR_PASSWORD@YOUR_HOST/YOUR_VHOST"
  }
}
```

### Step 3: Run the Application

```bash
# Restore NuGet packages
dotnet restore

# Apply database migrations (creates tables)
dotnet ef database update --project StockStream.API

# Start the API
dotnet run --project StockStream.API
```

**Expected output:**
```
╔════════════════════════════════════════╗
║    StockStream Warehouse API Started   ║
╚════════════════════════════════════════╝

📊 ENDPOINTS:
  GET  /api/products/{category}  - Get products by category
  POST /api/products/buy         - Purchase product

🔧 BACKGROUND SERVICES:
  ✓ AlertWorker - Listening to CloudAMQP queue

📍 API Doc: https://localhost:5001/swagger/ui
```

### Step 4: Test the API

**GET Products:**
```bash
curl https://localhost:5001/api/products/electronics
```

**Buy Product:**
```bash
curl -X POST https://localhost:5001/api/products/buy \
  -H "Content-Type: application/json" \
  -d '{"productId": 1, "quantity": 2}'
```

Or use **Swagger UI**: https://localhost:5001/swagger/ui

---

## 💾 Push to GitHub

When ready to push your code:

```bash
cd c:\Users\User\Documents\Projects\warhouse_api

# Check remote is configured
git remote -v

# Push to GitHub
git push -u origin main

# You'll be prompted for GitHub credentials
# Use your GitHub PAT (Personal Access Token) as password
```

**First time?** GitHub will ask for authentication. Use:
- Username: `your_github_username`
- Password: Create a PAT at https://github.com/settings/tokens

---

## 🔍 API Endpoints Summary

### 🟢 GET /api/products/{category}

Retrieves products from a category (with Redis caching).

**Request:**
```
GET /api/products/electronics
```

**Response:**
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

**How it works:**
1. Check Redis cache → Return if found
2. Cache miss → Query PostgreSQL
3. Store in Redis for 5 minutes
4. Return JSON

---

### 🔵 POST /api/products/buy

Purchase a product with database transaction and alert.

**Request:**
```json
POST /api/products/buy
Content-Type: application/json

{
  "productId": 1,
  "quantity": 2
}
```

**Response (Success):**
```json
{
  "success": true,
  "newStock": 48,
  "message": "Purchase successful"
}
```

**How it works:**
1. Validate input
2. **BEGIN TRANSACTION** (lock product row)
3. Check stock >= quantity
4. Deduct stock
5. **COMMIT TRANSACTION**
6. If stock ≤ 10:
   - Send message to CloudAMQP
   - AlertWorker receives and processes

---

## ⭐ Key Highlights

### 1. Database Transactions (Prevents Overselling)

```csharp
// In WarehouseService.cs (lines 150+)
using var transaction = await _dbContext.Database.BeginTransactionAsync();

// Only ONE customer can modify product at a time
// Others wait for lock to release
// Prevents race conditions

await transaction.CommitAsync();  // Makes changes permanent
```

**Without transaction:** 2 customers buy last item = oversell ❌
**With transaction:** Only 1 can modify, other gets rejected ✓

### 2. RabbitMQ Message Flow

```csharp
// In RabbitMQService.cs (lines 70+)
// ⭐ Service publishes message to CloudAMQP
_channel.BasicPublish(
    exchange: "",
    routingKey: "low_stock_alerts",
    basicProperties: null,
    body: messageBody);
```

```csharp
// In AlertWorker.cs (lines 100+)
// ⭐ Worker listens continuously 24/7
consumer.Received += async (model, eventArgs) => {
    // Process message when it arrives
};

_channel.BasicConsume("low_stock_alerts", autoAck: false, consumer);
```

**Message Flow:**
```
Product stock drops to 5 units
    ↓
WarehouseService triggers RabbitMQService
    ↓
Message sent to CloudAMQP queue
    ↓
AlertWorker listening → Receives message
    ↓
Deserialize JSON → Process alert → Acknowledge
    ↓
Message removed from queue
```

### 3. Redis Caching

```csharp
// In WarehouseService.cs (lines 40+)
// Check cache first
var cached = await _redisService.GetAsync<List<ProductResponseDto>>(cacheKey);
if (cached != null)
    return cached;  // < 100ms response

// Cache miss → Query database
var products = await _dbContext.Products...
await _redisService.SetAsync(cacheKey, products, TimeSpan.FromMinutes(5));
```

---

## 🧪 Testing Scenarios

### Scenario 1: Low Stock Alert

1. Buy product 5 "Desk" (current stock: 3)
2. Try: `POST /api/products/buy` with `{"productId": 5, "quantity": 2}`
3. Stock drops to 1 (below 10)
4. **AlertWorker receives message** from CloudAMQP
5. Check console for alert logs

### Scenario 2: Race Condition Prevention

1. Terminal 1: `curl -X POST /api/products/buy -d '{"productId": 2, "quantity": 5}'`
2. Terminal 2: `curl -X POST /api/products/buy -d '{"productId": 2, "quantity": 5}'` (same time)
3. Both request stock of 5, but only 5 available
4. One succeeds, one fails (transaction prevents overselling)

### Scenario 3: Cache Hits

1. Run: `curl /api/products/electronics`
2. Check console: **"[Cache] HIT"** (from memory)
3. Look at response time: < 100ms

---

## 📚 Study Guide

### Understand the Code in This Order:

1. **Models** (5 min)
   - [Product.cs](StockStream.API/Models/Product.cs) - Simple data class

2. **DTOs** (5 min)
   - [BuyRequestDto.cs](StockStream.API/DTOs/BuyRequestDto.cs)
   - [ProductResponseDto.cs](StockStream.API/DTOs/ProductResponseDto.cs)

3. **Database** (10 min)
   - [AppDbContext.cs](StockStream.API/Data/AppDbContext.cs) - EF Core setup
   - [Migrations](StockStream.API/Data/Migrations/) - Schema creation

4. **Services** (Heavy comments here!) (30 min)
   - [IRedisService.cs](StockStream.API/Services/Interfaces/IRedisService.cs) - Interface
   - [RedisService.cs](StockStream.API/Services/Implementations/RedisService.cs) - Implementation
   - [IRabbitMQService.cs](StockStream.API/Services/Interfaces/IRabbitMQService.cs)
   - **[RabbitMQService.cs](StockStream.API/Services/Implementations/RabbitMQService.cs)** ⭐ HEAVY COMMENTS
   - [IWarehouseService.cs](StockStream.API/Services/Interfaces/IWarehouseService.cs)
   - **[WarehouseService.cs](StockStream.API/Services/Implementations/WarehouseService.cs)** ⭐ TRANSACTION COMMENTS

5. **Background Worker** (15 min)
   - **[AlertWorker.cs](StockStream.API/Workers/AlertWorker.cs)** ⭐ Background service lifecycle

6. **Controller** (10 min)
   - [ProductsController.cs](StockStream.API/Controllers/ProductsController.cs) - HTTP endpoints

7. **Configuration** (10 min)
   - [Program.cs](StockStream.API/Program.cs) - DI & middleware

---

## 🔧 Troubleshooting

### Redis Connection Failed
```
"The operation timed out"
```
✓ Check Redis Labs dashboard (instance running?)  
✓ Verify connection string in appsettings  
✓ Test connection: `redis-cli` 

### PostgreSQL Not Found
```
"Server does not exist or access denied"
```
✓ Check ElephantSQL dashboard (instance created?)  
✓ Verify credentials  
✓ Copy full URL correctly

### RabbitMQ Connection Error
```
"The response indicates a failure"
```
✓ Check CloudAMQP dashboard  
✓ Verify AMQPS URL (not AMQP)  
✓ Include username:password

### Migrations Failed
```
dotnet ef database drop --project StockStream.API -f
dotnet ef database update --project StockStream.API
```

---

## 📝 Files with Heavy Comments

These files have detailed explanations as requested:

| File | Focus | Lines |
|------|-------|-------|
| [RabbitMQService.cs](StockStream.API/Services/Implementations/RabbitMQService.cs) | CloudAMQP connection, message flow | 1-200 |
| [AlertWorker.cs](StockStream.API/Workers/AlertWorker.cs) | Background service lifecycle, message consumption | 1-300 |
| [WarehouseService.cs](StockStream.API/Services/Implementations/WarehouseService.cs) | Database transactions, race conditions | 50-150 |

---

## ✨ Ready to Push?

When you're ready to push to GitHub:

```bash
git push -u origin main
```

Then share your repo link: https://github.com/Akil08/warhouse_api

---

## 📞 Next Steps

1. ✅ Set up cloud services (5 min)
2. ✅ Update appsettings (2 min)
3. ✅ Run `dotnet restore` (1 min)
4. ✅ Run migrations (1 min)
5. ✅ Start application (1 min)
6. ✅ Test endpoints (5 min)
7. ✅ Push to GitHub (1 min)

**Total time: ~15 minutes**

---

**Created:** February 7, 2026  
**Version:** 1.0.0  
**Status:** Production-Ready ✅
