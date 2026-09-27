using MediatR;

namespace CleanArchCqrs.Application.Products.Commands.UpdateProduct;

/// <summary>
/// Handles <see cref="UpdateProductCommand"/>.
/// Stub handler — returns <see cref="Unit.Value"/>.
/// The full version updates the product through the repository — see the Patreon version.
/// </summary>
public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, Unit>
{
    public Task<Unit> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
        => Task.FromResult(Unit.Value);
}
