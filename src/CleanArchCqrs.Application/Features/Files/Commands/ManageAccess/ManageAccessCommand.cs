using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Enums;
using MediatR;
namespace CleanArchCqrs.Application.Features.Files.Commands.ManageAccess;
public enum AccessMutationKind { UpsertFileGrant, RevokeFileGrant, UpsertFolderGrant, RevokeFolderGrant, AddTeamMember, RemoveTeamMember, MoveFolder }
public sealed record ManageAccessCommand(AccessMutationKind Kind, Guid ResourceId, Guid PrincipalId, PrincipalKind PrincipalKind, ResourceRole Role, DateTimeOffset? ExpiresAt, Guid? ParentFolderId, string IdempotencyKey) : IRequest<MutationReceipt>;
public sealed class ManageAccessCommandHandler(FileMutationService mutations) : IRequestHandler<ManageAccessCommand,MutationReceipt>
{
    public Task<MutationReceipt> Handle(ManageAccessCommand command,CancellationToken ct)=>mutations.ManageAsync(command,ct);
}
