using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.Users.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Features.Users.Queries.GetMyStorageQuota;

/// <summary>
/// Truy vấn hạn mức lưu trữ cá nhân của người dùng hiện tại đang đăng nhập.
/// </summary>
public record GetMyStorageQuotaQuery : IRequest<Result<PersonalStorageQuotaDto>>;
