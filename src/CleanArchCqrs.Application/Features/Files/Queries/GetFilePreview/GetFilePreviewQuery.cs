using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Interfaces;
using MediatR;
namespace CleanArchCqrs.Application.Features.Files.Queries.GetFilePreview;
public sealed record GetFilePreviewQuery(Guid FileId) : IRequest<PreviewResult>, ISensitiveReadRequest;
public sealed class GetFilePreviewQueryHandler(IFileAccessContextResolver resolver, IFileAccessService access,
    IPreviewProvider previews) : IRequestHandler<GetFilePreviewQuery, PreviewResult>
{
    public async Task<PreviewResult> Handle(GetFilePreviewQuery request, CancellationToken ct)
    {
        var context = await resolver.ResolveContextAsync(ct);
        var grant = await access.CheckAsync(context, request.FileId, PermissionNames.FileRead, ct);
        if (grant is null) return new("unavailable", 0, "FILE_UNAVAILABLE");
        var result = await previews.GetAsync(context, grant, false, ct);
        await access.AssertStillValidAsync(context, new[] { grant }, ct);
        return result;
    }
}
