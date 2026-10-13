using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Features.Files.Commands.ManageAccess;
using CleanArchCqrs.Domain.Enums;
using MediatR;
namespace CleanArchCqrs.Application.Features.Files.Commands.RestoreFile;
public record RestoreFileCommand(Guid FileId,string IdempotencyKey) : IRequest<MutationReceipt>;
public sealed class RestoreFileCommandHandler(FileMutationService mutations) : IRequestHandler<RestoreFileCommand,MutationReceipt>
{
    public Task<MutationReceipt> Handle(RestoreFileCommand request,CancellationToken ct)=>mutations.ChangeLifecycleAsync(request.FileId,FileLifecycle.Active,request.IdempotencyKey,ct);
}
