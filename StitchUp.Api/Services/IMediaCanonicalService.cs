using StitchUp.Contracts.Media;

namespace StitchUp.Api.Services;

public interface IMediaCanonicalService
{
    Task<PromoteMediaCanonicalResponseDto> PromoteMediaToCanonicalAsync(Guid mediaId, CancellationToken ct = default);
}
