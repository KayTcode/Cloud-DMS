using CleanArchCqrs.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.DeleteStorageProvider;

public record DeleteStorageProviderCommand(Guid Id) : IRequest<bool>;

public class DeleteStorageProviderCommandHandler : IRequestHandler<DeleteStorageProviderCommand, bool>
{
    private readonly IApplicationDbContext _context;

    public DeleteStorageProviderCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(DeleteStorageProviderCommand request, CancellationToken cancellationToken)
    {
        var provider = await _context.StorageProviders
            .Include(sp => sp.Tenants)
            .FirstOrDefaultAsync(sp => sp.Id == request.Id, cancellationToken);

        if (provider == null)
            return false;

        // Unlink tenants before deleting provider
        foreach (var tenant in provider.Tenants)
        {
            tenant.StorageProviderId = null;
        }

        _context.StorageProviders.Remove(provider);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
