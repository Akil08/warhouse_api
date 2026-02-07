# 📚 Understanding StockStream - Deep Dive Guide

## Table of Contents
1. [Database Transactions Explained](#database-transactions)
2. [RabbitMQ Message System Explained](#rabbitmq-explained)
3. [Redis Caching Explained](#redis-caching)
4. [Architecture Overview](#architecture)

---

## Database Transactions

### The Problem: Race Conditions

Imagine your warehouse has **1 laptop left** in stock:

**Time | Customer A | Customer B | Stock DB**
|---|---|---|---
|10:00:00 | Check stock → 1 item | - | 1 |
|10:00:01 | - | Check stock → 1 item | 1 |
|10:00:02 | Deduct 1 → Stock now 0 | - | 0 |
|10:00:03 | - | Deduct 1 → Stock now -1 | **-1** ❌ |

**Result:** Laptop oversold! Customer B gets a negative stock!

### Solution: Database Transactions

With a transaction, the database ensures **only one customer** can modify the product at a time:

**Time | Customer A | Customer B | Stock DB | Lock Status**
|---|---|---|---|---
|10:00:00 | BEGIN TRANSACTION | - | - | Waiting... |
|10:00:01 | LOCK product row ⛔ | - | - | Row Locked |
|10:00:02 | Check stock → 1 item | Waiting for lock... | 1 | Locked |
|10:00:03 | Deduct 1 → Stock 0 | Still waiting... | 0 | Locked |
|10:00:04 | COMMIT TRANSACTION | - | 0 | Lock Released ✅ |
|10:00:05 | - | Lock acquired! | 0 | Locked |
|10:00:06 | - | Check stock → 0 | 0 | Locked |
|10:00:07 | - | REJECT (insufficient stock) | 0 | Lock Released |

**Result:** Only customer A succeeds, B gets rejected. No overselling! ✅

### How Transactions Work in Code

```csharp
// ⭐ BEGIN TRANSACTION - All-or-nothing operation starts
using var transaction = await _dbContext.Database.BeginTransactionAsync();

try
{
    // ⭐ DATABASE LOCK ACQUIRED (on first query)
    var product = await _dbContext.Products
        .FirstOrDefaultAsync(p => p.Id == productId);
    // ↑ At this point, the product row is LOCKED
    //   No other transaction can modify it
    
    // ⭐ CHECK CONDITION (safe because row is locked)
    if (product.StockQuantity < quantity)
    {
        await transaction.RollbackAsync();  // ← Undo everything
        return PurchaseResult.FailureResult("Insufficient stock");
    }
    
    // ⭐ MODIFY DATA (inside transaction)
    product.StockQuantity -= quantity;
    await _dbContext.SaveChangesAsync();  // ← Not permanent yet!
    
    // ⭐ COMMIT TRANSACTION - All changes permanent
    await transaction.CommitAsync();      // ← NOW it's permanent!
    // ↑ Lock is released here
    //   Next transaction can proceed
    
    return PurchaseResult.SuccessResult(product.StockQuantity);
}
catch (Exception ex)
{
    // ⭐ ROLLBACK ON ERROR - Undo all changes
    await transaction.RollbackAsync();    // ← Revert to before transaction
    return PurchaseResult.FailureResult($"Failed: {ex.Message}");
}
```

### Key Concepts

| Concept | Meaning |
|---------|---------|
| **BEGIN** | Start transaction (all-or-nothing operation) |
| **LOCK** | Database prevents other transactions from modifying this row |
| **COMMIT** | Make all changes permanent |
| **ROLLBACK** | Undo all changes, go back to before transaction |
| **Isolation Level** | How much other transactions can see your data |

### What Actually Gets Locked?

- **✓ Locked:** The specific product row you're modifying
- **✓ Locked:** Only for the duration of the transaction
- **✗ NOT Locked:** Other products (can be modified freely)
- **✗ NOT Locked:** Read-only queries (no lock needed)

### Visual Example

```
DATABASE PHYSICAL STRUCTURE:

Products Table (PostgreSQL)
┌─────────┬──────────┬────────┐
│ ID      │ Name     │ Stock  │
├─────────┼──────────┼────────┤
│ 1       │ Laptop   │ 0      │ ← LOCKED (Transaction A)
│ 2       │ Mouse    │ 50     │ ← Can be modified (Transaction B)
│ 3       │ Keyboard │ 25     │ ← Can be modified (Transaction C)
└─────────┴──────────┴────────┘

Time sequence:
10:00 Transaction A: Lock row 1
      Transaction B: Lock row 2 (OK)
      Transaction C: Lock row 3 (OK)
      
10:05 Transaction A: Try to lock row 2 (WAIT - B has it)
      
10:10 Transaction B: Release lock on row 2
      
10:11 Transaction A: Lock on row 2 acquired (OK)
```

---

## RabbitMQ Explained

### What is RabbitMQ?

RabbitMQ is a **message broker** - a system that stores and delivers messages between applications.

**Analogy:** Think of it like a post office:
- Sender (Service) drops message in mailbox
- Post office (RabbitMQ) stores it
- Receiver (Worker) picks it up and reads it

### CloudAMQP vs Local RabbitMQ

| Aspect | Local | CloudAMQP |
|--------|-------|-----------|
| **Location** | Your computer | Cloud (maintained by CloudAMQP) |
| **Installation** | Must install RabbitMQ server | Already running |
| **Management** | You manage it | CloudAMQP manages it |
| **Cost** | Free | Free tier available |
| **Uptime** | Depends on your PC | 99.9% SLA |
| **Access** | `localhost:5672` | AMQPS URL (internet) |

### The Message Flow

```
┌──────────────┐
│   Your API   │
│   Endpoint   │
└──────┬───────┘
       │
       │ 1. Stock drops below 10
       ↓
┌─────────────────────────────────────┐
│  RabbitMQService.SendAlert()        │
│  - Create alert object              │
│  - Convert to JSON:                 │
│    {                                │
│      "ProductId": 5,                │
│      "StockLevel": 8,               │
│      "AlertTime": "2026-02-07..."   │
│    }                                │
│  - Send to CloudAMQP                │
└──────┬────────────────────────────────┘
       │
       │ 2. Message in transit (AMQPS encrypted)
       ↓
┌─────────────────────────────────────┐
│  CloudAMQP Queue                    │
│  "low_stock_alerts"                 │
│                                     │
│  Stored Message:                    │
│  ┌─────────────────────────────┐   │
│  │ {"ProductId": 5, ...}       │   │
│  └─────────────────────────────┘   │
│                                     │
│  (persists until consumed)          │
└──────┬────────────────────────────────┘
       │
       │ 3. AlertWorker listening...
       ↓
┌─────────────────────────────────────┐
│  AlertWorker (Background Service)   │
│  - Listening to queue 24/7          │
│  - Receives message                 │
│  - Deserialize JSON → Object        │
│  - Process alert:                   │
│    * Log message                    │
│    * Send email to manager          │
│    * Create alert record            │
│  - Acknowledge message              │
└──────┬────────────────────────────────┘
       │
       │ 4. Message processed
       ↓
┌─────────────────────────────────────┐
│  CloudAMQP                          │
│  - Remove message from queue        │
│  - Free up space                    │
└─────────────────────────────────────┘
```

### Message Format (JSON)

```json
// What gets sent by RabbitMQService
{
  "ProductId": 5,           // Which product
  "StockLevel": 8,          // Current stock
  "AlertTime": "2026-02-07T15:30:45.123Z",  // When alert was created
  "Threshold": 10           // Alert threshold
}

// AlertWorker deserializes into:
public class LowStockAlert
{
    public int ProductId { get; set; }
    public int StockLevel { get; set; }
    public DateTime AlertTime { get; set; }
    public int Threshold { get; set; }
}
```

### Why RabbitMQ Instead of Direct Email?

**Without RabbitMQ (Direct Email):**
```
POST /api/products/buy
  ↓
Buy product
  ↓
Send email to manager (WAITS for email server)
  ↓
Email server down? API hangs for 30 seconds!
  ↓
Customer sees slow response
```

**With RabbitMQ (Async):**
```
POST /api/products/buy
  ↓
Buy product
  ↓
Send message to RabbitMQ (instant, <10ms)
  ↓
Return response to customer (fast! 100ms total)
  ↓
AlertWorker processes later (whenever email is ready)
```

### Durability: What If Server Crashes?

```
Scenario: Server crashes while AlertWorker is processing

Without durability:
1. Message arrives
2. Worker starts processing
3. Server crashes 💥
4. Message lost forever ❌

With durability (what we use):
1. Message arrives
2. RabbitMQ saves to disk
3. Worker starts processing
4. Server crashes 💥
5. Server reboots
6. AlertWorker reconnects
7. RabbitMQ redelivers message
8. Processing completes ✓
```

### Manual Acknowledgment

```csharp
// When AutoAck = false (what we use)

1. Message arrives
2. Worker processes it
3. If successful:
   _channel.BasicAck(deliveryTag, false);
   ↓ Message removed from queue
4. If error:
   _channel.BasicNack(deliveryTag, false, false);
   ↓ Message sent to dead-letter queue OR redelivered

Benefits:
- Message not lost if worker crashes mid-processing
- Only removed after confirmed successful processing
```

---

## Redis Caching

### Problem: Database Queries Are Slow

```
Each database query to PostgreSQL:
- Network roundtrip: 5-10ms
- Query execution: 10-20ms
- Total: 15-30ms per request

If 1000 requests per minute:
1000 × 30ms = 30 seconds wasted in database queries!
```

### Solution: Redis Caching

```
Redis (In-Memory):
- Stored in RAM (super fast)
- Network roundtrip: 1-2ms
- Return data: < 1ms
- Total: < 2ms per request!

Same 1000 requests:
1000 × 2ms = 2 seconds (not 30!)
```

### How Our Cache Works

```csharp
public async Task<List<ProductResponseDto>> GetProductsByCategoryAsync(string category)
{
    // Step 1: Generate cache key
    string cacheKey = $"products:{category.ToLower()}";
    // Example: "products:electronics"
    
    // Step 2: Check Redis
    var cachedProducts = await _redisService.GetAsync<List<ProductResponseDto>>(cacheKey);
    if (cachedProducts != null)
    {
        return cachedProducts;  // FAST! < 2ms
    }
    
    // Step 3: Cache miss - query database (slower)
    var products = await _dbContext.Products
        .Where(p => p.Category.ToLower() == category.ToLower())
        .ToListAsync();  // 15-30ms
    
    // Step 4: Store in cache for 5 minutes
    await _redisService.SetAsync(cacheKey, products, TimeSpan.FromMinutes(5));
    
    return products;
}
```

### Cache Lifecycle

```
Request 1 (10:00:00):
├─ Check Redis → Not found
├─ Query database (20ms)
├─ Cache in Redis
└─ Return (20ms total)

Request 2 (10:00:05):
├─ Check Redis → Found! ✓
└─ Return (2ms total) ← 10x faster!

Request 3 (10:05:01):
├─ Check Redis → Expired (5 min passed)
├─ Query database (20ms)
├─ Update cache
└─ Return (20ms total)

Request 4-100 (10:05:05 to 10:09:59):
├─ Check Redis → Found! ✓
└─ Return (2ms total)
```

---

## Architecture Overview

### Layered Architecture

```
┌─────────────────────────────────────┐
│     HTTP REQUESTS (Clients)         │
│  GET /api/products/electronics      │
│  POST /api/products/buy             │
└────────────────┬────────────────────┘
                 │
                 ↓
         ┌───────────────┐
         │  CONTROLLER   │
         │ (HTTP Layer)  │
         └────────┬──────┘
                  │
      ┌───────────┴────────────┐
      │                        │
      ↓                        ↓
  ┌─────────┐          ┌──────────────┐
  │ Service │          │  Service     │
  │(Business│          │  (Business   │
  │ Logic)  │          │   Logic)     │
  └────┬────┘          └──────┬───────┘
       │                      │
       │        ┌─────────────┴────────┐
       ↓        ↓                      ↓
    ┌────┐  ┌───────┐            ┌──────────┐
    │ORM │  │CACHE  │            │MESSAGE   │
    │    │  │SYSTEM │            │BROKER    │
    │PGSQL  │REDIS  │            │RABBITMQ  │
    └────┘  └───────┘            └──────────┘

Client sends HTTP request
  ↓
Controller handles route
  ↓
Service implements business logic:
  - Validation
  - Transaction management
  - Cache checking
  - Message publishing
  ↓
Data access layer:
  - Query database (PostgreSQL)
  - Check cache (Redis)
  - Publish messages (RabbitMQ)
  ↓
Response sent back to client
```

### Responsibility Division

| Layer | Responsibility | Example |
|-------|---|---------|
| **Controller** | HTTP handling | Validate request, call service, return HTTP response |
| **Service** | Business logic | Check stock, validate quantity, handle transaction |
| **Database** | Data persistence | Store/retrieve products, maintain ACID properties |
| **Cache** | Performance | Store frequently accessed data |
| **Message Broker** | Async communication | Store alerts for workers to process |
| **Worker** | Background processing | Listen for messages, send emails, log alerts |

### No Direct Database Calls in Controller

✓ CORRECT:
```csharp
public class ProductsController
{
    public async Task<IActionResult> Buy(BuyRequestDto request)
    {
        // Call service - service handles everything
        var result = await _warehouseService.ProcessPurchaseAsync(...);
    }
}
```

✗ WRONG (don't do this):
```csharp
public class ProductsController
{
    public async Task<IActionResult> Buy(BuyRequestDto request)
    {
        // Should NOT directly access database!
        var product = await _dbContext.Products.FirstOrDefaultAsync(...);
        // This breaks clean architecture
    }
}
```

---

## Summary

### Database Transactions
- Prevent overselling through row-level locks
- Atomic: All changes succeed or all fail
- COMMIT makes changes permanent
- ROLLBACK undoes all changes

### RabbitMQ
- Message broker in the cloud (CloudAMQP)
- Decouples alert processing from API
- Messages persist in queue
- AlertWorker processes asynchronously
- Durable by default (survives crashes)

### Redis Caching
- In-memory store (super fast)
- 5-minute TTL for category listings
- Gracefully falls back to database on miss
- Reduces load on PostgreSQL

### Architecture
- Clean separation of concerns
- Controller → Service → Data layer
- Dependency injection for testability
- Production-ready design

---

**This is what makes StockStream a professional, production-ready API** ✨
