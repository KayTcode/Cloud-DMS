using CleanArchCqrs.Application.Common.Models;
using MediatR;

namespace CleanArchCqrs.Application.Products.Queries.GetProducts;

/// <summary>
/// Handles <see cref="GetProductsQuery"/>.
/// Stub handler — returns an empty <see cref="PagedResult{T}"/>.
/// The full version queries the repository with search and pagination — see the Patreon version.
/// </summary>
public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, PagedResult<ProductDto>>
{
    public Task<PagedResult<ProductDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new PagedResult<ProductDto>());
}
