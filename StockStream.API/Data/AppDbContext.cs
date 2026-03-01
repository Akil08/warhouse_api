using Microsoft.EntityFrameworkCore;
using StockStream.API.Models;

namespace StockStream.API.Data;

// this file is for db settings type , make sure db has constrains 
// and also make relations between tables if needed

/// <summary>
/// Entity Framework Core database context for StockStream
/// </summary>
///     
// what is this file is for ? 

// This file defines the AppDbContext class, 
//which is the Entity Framework Core database context for the StockStream application.
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) 
        : base(options)
    {
    }

    public DbSet<Product> Products { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Seed initial data
        modelBuilder.Entity<Product>().HasData(
            new Product
            {
                Id = 1,
                Name = "Laptop",
                Category = "electronics",
                Price = 999.99m,
                StockQuantity = 50,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Product
            {
                Id = 2,
                Name = "Mouse",
                Category = "electronics",
                Price = 29.99m,
                StockQuantity = 5,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Product
            {
                Id = 3,
                Name = "Keyboard",
                Category = "electronics",
                Price = 79.99m,
                StockQuantity = 8,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Product
            {
                Id = 4,
                Name = "Office Chair",
                Category = "furniture",
                Price = 299.99m,
                StockQuantity = 15,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Product
            {
                Id = 5,
                Name = "Desk",
                Category = "furniture",
                Price = 499.99m,
                StockQuantity = 3,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        );
    }
}









// using Microsoft.EntityFrameworkCore;
// using StockStream.API.Models;

// namespace StockStream.API.Data;

// public class AppDbContext : DbContext
// {
//     public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
//     {
//     }

//     // This represents the physical "Products" table in PostgreSQL
//     public DbSet<Product> Products { get; set; }

//     protected override void OnModelCreating(ModelBuilder modelBuilder)
//     {
//         base.OnModelCreating(modelBuilder);

//         // 1. Configure the 'Product' Entity
//         modelBuilder.Entity<Product>(entity =>
//         {
//             // Set primary key (though EF does this by convention, being explicit is safer)
//             entity.HasKey(p => p.Id);

//             // 2. Database Integrity: Ensure Name is required and has a max length
//             entity.Property(p => p.Name)
//                 .IsRequired()
//                 .HasMaxLength(200);

//             // 3. Precision: Force Decimal to (18,2) for financial accuracy
//             entity.Property(p => p.Price)
//                 .HasPrecision(18, 2);

//             // 4. Performance: Add an index to 'Category' for faster GET searches
//             entity.HasIndex(p => p.Category);

//             // 5. Constraints: Ensure no two products have the same Name
//             entity.HasIndex(p => p.Name).IsUnique();
//         });
//     }
// }