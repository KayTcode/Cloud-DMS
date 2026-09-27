using MediatR;

namespace CleanArchCqrs.Application.Products.Commands.DeleteProduct;

/// <summary>
/// Handles <see cref="DeleteProductCommand"/>.
/// Stub handler — returns <see cref="Unit.Value"/>.
/// The full version deletes the product through the repository — see the Patreon version.
/// </summary>
public class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand, Unit>
{
    public Task<Unit> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
        => Task.FromResult(Unit.Value);
}
