using MediatR;

namespace CleanArchCqrs.Application.Products.Commands.CreateProduct;

/// <summary>
/// Handles <see cref="CreateProductCommand"/>.
/// Stub handler — returns <see cref="Guid.Empty"/>.
/// The full version persists the product and returns its new identifier — see the Patreon version.
/// </summary>
public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Guid>
{
    public Task<Guid> Handle(CreateProductCommand request, CancellationToken cancellationToken)
        => Task.FromResult(Guid.Empty);
}
