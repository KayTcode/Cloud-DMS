using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Application.Common.Authorization;

public sealed record MutationRequest(
    string ScopeKey,
    string IdempotencyKey,
    string PayloadHash);

public sealed record MutationReceipt(
    Guid OperationId,
    long Version,
    AuthorizationOperationStatus Status);
