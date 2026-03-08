using Microsoft.EntityFrameworkCore;
using StitchUp.Contracts.Media;
using StitchUp.Infrastructure.Data;

namespace StitchUp.Api.Services;

public sealed class MediaCanonicalService : IMediaCanonicalService
{
    private readonly StitchUpDbContext _db;

    public MediaCanonicalService(StitchUpDbContext db)
    {
        _db = db;
    }

    public async Task<PromoteMediaCanonicalResponseDto> PromoteMediaToCanonicalAsync(Guid mediaId, CancellationToken ct = default)
    {
        var media = await _db.Media
            .FirstOrDefaultAsync(x => x.MediaId == mediaId, ct);

        if (media is null)
        {
            throw new KeyNotFoundException($"Media not found for mediaId={mediaId}");
        }

        if (string.Equals(media.StorageState, "CloudCanonical", StringComparison.OrdinalIgnoreCase))
        {
            return ToDto(media);
        }

        if (string.Equals(media.StorageState, "CloudTempConverted", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(media.CanonicalBlobPath))
            {
                throw new InvalidOperationException("Cannot promote media to canonical because CanonicalBlobPath is null.");
            }

            if (string.IsNullOrWhiteSpace(media.CanonicalContainer))
            {
                throw new InvalidOperationException("Cannot promote media to canonical because CanonicalContainer is null.");
            }

            media.StorageState = "CloudCanonical";
            media.IsTemporary = false;
            media.TemporaryExpiresUtc = null;
        }

        var mediaBlobs = await _db.MediaBlobs
            .Where(x => x.MediaId == media.MediaId && x.DeletedUtc == null)
            .ToListAsync(ct);

        foreach (var blob in mediaBlobs)
        {
            if (string.Equals(blob.BlobRole, "Converted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(blob.BlobRole, "Canonical", StringComparison.OrdinalIgnoreCase))
            {
                blob.IsTemporary = false;
                blob.TemporaryExpiresUtc = null;
            }
        }

        await _db.SaveChangesAsync(ct);
        return ToDto(media);
    }

    private static PromoteMediaCanonicalResponseDto ToDto(Domain.Entities.Server.MediaEntity media)
    {
        return new PromoteMediaCanonicalResponseDto
        {
            MediaId = media.MediaId,
            CanonicalBlobPath = media.CanonicalBlobPath,
            CanonicalContainer = media.CanonicalContainer,
            StorageState = media.StorageState,
            IsTemporary = media.IsTemporary
        };
    }
}
