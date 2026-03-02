# StockStream API

## ⚠️ Important Note



This is a personal learning project — intentionally kept simple enough to understand fully, yet realistic enough to reflect actual backend challenges. My goal wasn't to build a production-ready application, but to use it as a playground to deeply understand how different technologies connect in practice: how code flows from controllers to services to repositories, how functions call one another, and how real-world problems like race conditions, abuse prevention, and background automation are solved.

Every feature — from transactions to caching, rate limiting to scheduled jobs — was implemented to learn how it works in real-world scenarios: to test, break, experiment, and truly grasp the underlying mechanics. I focused on clean project structure, OOP principles, and basic SOLID concepts, and explored the trade-offs of each decision along the way.



## What the Project Is
A production ready .NET 8 Web API for warehouse inventory management, supporting product retrieval by category and transactional purchases with low-stock alerting via a message queue.

## What It Does
- Retrieves products by category with Redis caching for fast responses
- Processes purchases with database transactions to ensure stock accuracy
- Sends low stock alerts to a RabbitMQ queue when stock falls ≤ 10
- Runs a continuous background worker that consumes and processes alerts from the queue asynchronously

## What Problem It Solves
It solves common warehouse management challenges such as:

- Race conditions and overselling during concurrent purchases
- Slow response times for frequent product listings
- Delayed or missed low-stock notifications
- Tight coupling between purchase processing and alerting

## How It Solves It
- Database transactions with row-level locking prevent overselling
- Redis caching (5-minute TTL) delivers responses with fallback to database
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

