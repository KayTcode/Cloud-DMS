using CleanArchCqrs.Application.Common.Models;
using MediatR;

namespace CleanArchCqrs.Application.Products.Queries.GetProduct;

/// <summary>
/// Handles <see cref="GetProductQuery"/>.
/// Stub handler — returns <c>null</c>, which the controller maps to 404 Not Found.
/// The full version loads the product from the repository — see the Patreon version.
/// </summary>
public class GetProductQueryHandler : IRequestHandler<GetProductQuery, ProductDto?>
{
    public Task<ProductDto?> Handle(GetProductQuery request, CancellationToken cancellationToken)
        => Task.FromResult<ProductDto?>(null);
}
