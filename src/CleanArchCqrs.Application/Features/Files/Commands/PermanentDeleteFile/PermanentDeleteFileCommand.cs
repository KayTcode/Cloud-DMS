using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Features.Files.Commands.ManageAccess;
using CleanArchCqrs.Domain.Enums;
using MediatR;
namespace CleanArchCqrs.Application.Features.Files.Commands.PermanentDeleteFile;
public record PermanentDeleteFileCommand(Guid FileId,string IdempotencyKey) : IRequest<MutationReceipt>;
public sealed class PermanentDeleteFileCommandHandler(FileMutationService mutations) : IRequestHandler<PermanentDeleteFileCommand,MutationReceipt>
{
    public Task<MutationReceipt> Handle(PermanentDeleteFileCommand request,CancellationToken ct)=>mutations.ChangeLifecycleAsync(request.FileId,FileLifecycle.Purging,request.IdempotencyKey,ct);
}
