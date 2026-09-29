using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

/// <summary>
/// Represents a product entity.
/// </summary>
public class Product : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
}
