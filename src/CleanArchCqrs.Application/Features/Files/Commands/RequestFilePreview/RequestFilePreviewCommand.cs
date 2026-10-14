using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Interfaces;
using MediatR;
namespace CleanArchCqrs.Application.Features.Files.Commands.RequestFilePreview;
public sealed record RequestFilePreviewCommand(Guid FileId) : IRequest<PreviewResult>, ISensitiveReadRequest;
public sealed class RequestFilePreviewCommandHandler(IFileAccessContextResolver resolver, IFileAccessService access,
    IPreviewProvider previews) : IRequestHandler<RequestFilePreviewCommand, PreviewResult>
{
    public async Task<PreviewResult> Handle(RequestFilePreviewCommand request, CancellationToken ct)
    {
        var context = await resolver.ResolveContextAsync(ct);
        var grant = await access.CheckAsync(context, request.FileId, PermissionNames.FileRead, ct);
        if (grant is null) return new("unavailable", 0, "FILE_UNAVAILABLE");
        var result = await previews.GetAsync(context, grant, true, ct);
        await access.AssertStillValidAsync(context, new[] { grant }, ct);
        return result;
    }
}
