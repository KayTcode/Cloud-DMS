using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Features.Files.Commands.ManageAccess;
using CleanArchCqrs.Domain.Enums;
using MediatR;
namespace CleanArchCqrs.Application.Features.Files.Commands.DeleteFile;
public record DeleteFileCommand(Guid FileId,string IdempotencyKey) : IRequest<MutationReceipt>;
public sealed class DeleteFileCommandHandler(FileMutationService mutations) : IRequestHandler<DeleteFileCommand,MutationReceipt>
{
    public Task<MutationReceipt> Handle(DeleteFileCommand request,CancellationToken ct)=>mutations.ChangeLifecycleAsync(request.FileId,FileLifecycle.Binned,request.IdempotencyKey,ct);
}
