using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StitchUp.Contracts.Projects;
using StitchUp.Domain.Entities.Server;
using StitchUp.Infrastructure.Data;

namespace StitchUp.Api.Services;

public sealed class ProjectManifestService : IProjectManifestService
{
    private const string ProposalPending = "Pending";
    private const string ProposalAccepted = "Accepted";
    private const string ProposalRejected = "Rejected";
    private const string ProposalSuperseded = "Superseded";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly StitchUpDbContext _db;

    public ProjectManifestService(StitchUpDbContext db)
    {
        _db = db;
    }

    public async Task<ProjectManifestDto?> GetLatestManifestAsync(Guid projectId, CancellationToken ct = default)
    {
        var currentPublishedVersion = await _db.Projects
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => x.CurrentPublishedVersionNumber)
            .FirstOrDefaultAsync(ct);

        ProjectManifestVersionEntity? entity = null;
        if (currentPublishedVersion.HasValue)
        {
            entity = await _db.ProjectManifestVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.ProjectId == projectId && x.VersionNumber == currentPublishedVersion.Value,
                    ct);
        }

        entity ??= await _db.ProjectManifestVersions
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.IsPublished)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : DeserializeManifest(entity);
    }

    public async Task<ProjectManifestVersionSummaryDto> SaveManifestVersionAsync(
        Guid projectId,
        Guid userId,
        ProjectManifestDto manifest,
        string? changeSummary,
        CancellationToken ct = default)
    {
        if (manifest.ProjectId != projectId)
        {
            throw new ArgumentException("Manifest projectId must match the route projectId.");
        }

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
        if (project is null)
        {
            throw new ManifestNotFoundException($"Project not found for projectId={projectId}");
        }

        var userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(x => x.UserId == userId, ct);
        if (!userExists)
        {
            throw new ManifestNotFoundException($"User not found for userId={userId}");
        }

        var normalizedItems = NormalizeManifestItems(manifest.MediaItems);
        var mediaIds = normalizedItems.Select(x => x.MediaId).Distinct().ToList();
        if (mediaIds.Count > 0)
        {
            var existingCount = await _db.Media
                .AsNoTracking()
                .Where(x => mediaIds.Contains(x.MediaId))
                .CountAsync(ct);
            if (existingCount != mediaIds.Count)
            {
                throw new ArgumentException("Manifest references one or more missing media items.");
            }
        }

        var nextVersion = (await _db.ProjectManifestVersions
            .Where(x => x.ProjectId == projectId)
            .MaxAsync(x => (int?)x.VersionNumber, ct) ?? 0) + 1;

        var manifestToStore = new ProjectManifestDto
        {
            ProjectId = projectId,
            CreatedByUserId = userId,
            Title = manifest.Title,
            Description = manifest.Description,
            Version = nextVersion,
            BasedOnVersion = manifest.BasedOnVersion,
            MediaItems = normalizedItems
        };

        var now = DateTime.UtcNow;
        var entity = new ProjectManifestVersionEntity
        {
            ProjectManifestVersionId = Guid.NewGuid(),
            ProjectId = projectId,
            VersionNumber = nextVersion,
            BasedOnVersionNumber = manifest.BasedOnVersion,
            CreatedByUserId = userId,
            CreatedUtc = now,
            ManifestJson = JsonSerializer.Serialize(manifestToStore, JsonOptions),
            ChangeSummary = string.IsNullOrWhiteSpace(changeSummary) ? null : changeSummary.Trim(),
            IsPublished = false
        };

        _db.ProjectManifestVersions.Add(entity);

        ProjectChangeProposalEntity? proposal = null;
        if (userId != project.AuthorUserId)
        {
            var currentPublishedVersion = await GetCurrentPublishedVersionNumberAsync(project, ct);
            proposal = new ProjectChangeProposalEntity
            {
                ProjectChangeProposalId = Guid.NewGuid(),
                ProjectId = projectId,
                BaseVersionNumber = manifest.BasedOnVersion ?? currentPublishedVersion ?? 0,
                ProposedVersionNumber = nextVersion,
                ProposedByUserId = userId,
                ProjectManifestVersionId = entity.ProjectManifestVersionId,
                Status = ProposalPending,
                ChangeSummary = entity.ChangeSummary,
                CreatedUtc = now
            };
            _db.ProjectChangeProposals.Add(proposal);
        }

        await _db.SaveChangesAsync(ct);
        return ToSummary(entity, proposal);
    }

    public async Task<ProjectManifestDiffDto> GetManifestDiffAsync(
        Guid projectId,
        int fromVersion,
        int toVersion,
        CancellationToken ct = default)
    {
        var versions = await _db.ProjectManifestVersions
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && (x.VersionNumber == fromVersion || x.VersionNumber == toVersion))
            .ToListAsync(ct);

        var fromEntity = versions.FirstOrDefault(x => x.VersionNumber == fromVersion);
        var toEntity = versions.FirstOrDefault(x => x.VersionNumber == toVersion);
        if (fromEntity is null || toEntity is null)
        {
            throw new ManifestNotFoundException("One or both manifest versions were not found for this project.");
        }

        var fromManifest = DeserializeManifest(fromEntity);
        var toManifest = DeserializeManifest(toEntity);
        var changes = BuildSemanticDiffChanges(fromManifest, toManifest);

        return new ProjectManifestDiffDto
        {
            ProjectId = projectId,
            FromVersion = fromVersion,
            ToVersion = toVersion,
            Changes = changes
        };
    }

    public async Task<ProjectManifestVersionSummaryDto> PublishManifestVersionAsync(Guid projectId, int version, CancellationToken ct = default)
    {
        var project = await _db.Projects
            .FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
        if (project is null)
        {
            throw new ManifestNotFoundException("Project not found.");
        }

        var targetVersion = await LoadVersionByProjectAndVersionAsync(projectId, version, ct);
        if (targetVersion is null)
        {
            throw new ManifestNotFoundException("Manifest version not found.");
        }

        if (targetVersion.IsPublished)
        {
            var proposal = await _db.ProjectChangeProposals
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ProjectManifestVersionId == targetVersion.ProjectManifestVersionId, ct);
            return ToSummary(targetVersion, proposal);
        }

        var currentPublishedVersion = await GetCurrentPublishedVersionNumberAsync(project, ct);
        if (targetVersion.BasedOnVersionNumber.HasValue &&
            currentPublishedVersion.HasValue &&
            targetVersion.BasedOnVersionNumber.Value != currentPublishedVersion.Value)
        {
            throw BuildStaleBaseConflict(projectId, version, targetVersion.BasedOnVersionNumber, currentPublishedVersion);
        }

        return await PublishManifestInternalAsync(project, targetVersion, null, ct);
    }

    public async Task<IReadOnlyList<ProjectManifestVersionSummaryDto>> GetManifestVersionsAsync(Guid projectId, int take = 20, CancellationToken ct = default)
    {
        var normalizedTake = take <= 0 ? 20 : Math.Min(take, 100);

        var versions = await _db.ProjectManifestVersions
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.VersionNumber)
            .Take(normalizedTake)
            .ToListAsync(ct);

        if (versions.Count == 0)
        {
            return Array.Empty<ProjectManifestVersionSummaryDto>();
        }

        var manifestIds = versions.Select(x => x.ProjectManifestVersionId).ToList();
        var proposalsByManifestId = await _db.ProjectChangeProposals
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && manifestIds.Contains(x.ProjectManifestVersionId))
            .GroupBy(x => x.ProjectManifestVersionId)
            .Select(group => group
                .OrderByDescending(x => x.CreatedUtc)
                .ThenByDescending(x => x.ProjectChangeProposalId)
                .First())
            .ToDictionaryAsync(x => x.ProjectManifestVersionId, x => x, ct);

        return versions
            .Select(version => ToSummary(
                version,
                proposalsByManifestId.GetValueOrDefault(version.ProjectManifestVersionId)))
            .ToList();
    }

    public async Task<IReadOnlyList<ProjectManifestVersionSummaryDto>> GetPendingProposalsAsync(Guid projectId, CancellationToken ct = default)
    {
        var rows = await _db.ProjectChangeProposals
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Status == ProposalPending)
            .Join(
                _db.ProjectManifestVersions.AsNoTracking(),
                proposal => proposal.ProjectManifestVersionId,
                version => version.ProjectManifestVersionId,
                (proposal, version) => new { proposal, version })
            .Where(x => !x.version.IsPublished)
            .OrderByDescending(x => x.version.VersionNumber)
            .ToListAsync(ct);

        return rows
            .Select(x => ToSummary(x.version, x.proposal))
            .ToList();
    }

    public async Task<ProjectManifestVersionSummaryDto> AcceptProposalAsync(Guid projectId, int version, Guid reviewedByUserId, CancellationToken ct = default)
    {
        var project = await _db.Projects
            .FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
        if (project is null)
        {
            throw new ManifestNotFoundException("Project not found.");
        }

        var proposal = await _db.ProjectChangeProposals
            .Include(x => x.ProjectManifestVersion)
            .FirstOrDefaultAsync(x =>
                x.ProjectId == projectId &&
                x.ProposedVersionNumber == version, ct);
        if (proposal is null)
        {
            throw new ManifestNotFoundException("Pending proposal not found for this version.");
        }

        if (!string.Equals(proposal.Status, ProposalPending, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Proposal is not pending.");
        }

        var currentPublishedVersion = await GetCurrentPublishedVersionNumberAsync(project, ct) ?? 0;
        if (proposal.BaseVersionNumber != currentPublishedVersion)
        {
            throw BuildStaleBaseConflict(projectId, version, proposal.BaseVersionNumber, currentPublishedVersion);
        }

        return await PublishManifestInternalAsync(project, proposal.ProjectManifestVersion, reviewedByUserId, ct);
    }

    public async Task<ProjectManifestVersionSummaryDto> RejectProposalAsync(Guid projectId, int version, Guid reviewedByUserId, CancellationToken ct = default)
    {
        var proposal = await _db.ProjectChangeProposals
            .Include(x => x.ProjectManifestVersion)
            .FirstOrDefaultAsync(x =>
                x.ProjectId == projectId &&
                x.ProposedVersionNumber == version &&
                x.Status == ProposalPending, ct);

        if (proposal is null)
        {
            throw new ManifestNotFoundException("Pending proposal not found for this version.");
        }

        proposal.Status = ProposalRejected;
        proposal.ReviewedUtc = DateTime.UtcNow;
        proposal.ReviewedByUserId = reviewedByUserId;
        await _db.SaveChangesAsync(ct);

        return ToSummary(proposal.ProjectManifestVersion, proposal);
    }

    private async Task<ProjectManifestVersionSummaryDto> PublishManifestInternalAsync(
        ProjectEntity project,
        ProjectManifestVersionEntity targetVersion,
        Guid? reviewedByUserId,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var publishedVersions = await _db.ProjectManifestVersions
            .Where(x => x.ProjectId == project.ProjectId && x.IsPublished)
            .ToListAsync(ct);
        foreach (var published in publishedVersions)
        {
            published.IsPublished = false;
        }

        targetVersion.IsPublished = true;

        var manifest = DeserializeManifest(targetVersion);
        project.Title = string.IsNullOrWhiteSpace(manifest.Title) ? project.Title : manifest.Title!;
        project.Description = manifest.Description;
        project.UpdatedUtc = now;
        project.CurrentPublishedVersionNumber = targetVersion.VersionNumber;

        var mediaIds = manifest.MediaItems.Select(x => x.MediaId).Distinct().ToList();
        if (mediaIds.Count > 0)
        {
            var existingCount = await _db.Media
                .AsNoTracking()
                .Where(x => mediaIds.Contains(x.MediaId))
                .CountAsync(ct);
            if (existingCount != mediaIds.Count)
            {
                throw new ArgumentException("Cannot publish manifest: one or more referenced media items do not exist.");
            }
        }

        var existingRows = await _db.ProjectMedia
            .Where(x => x.ProjectId == project.ProjectId)
            .ToListAsync(ct);
        if (existingRows.Count > 0)
        {
            _db.ProjectMedia.RemoveRange(existingRows);
        }

        var usedProjectMediaIds = new HashSet<Guid>();
        foreach (var item in manifest.MediaItems.OrderBy(x => x.OrderIndex))
        {
            var parsedProjectMediaId = Guid.TryParse(item.ProjectMediaKey, out var keyGuid)
                ? keyGuid
                : Guid.NewGuid();
            if (!usedProjectMediaIds.Add(parsedProjectMediaId))
            {
                parsedProjectMediaId = Guid.NewGuid();
                usedProjectMediaIds.Add(parsedProjectMediaId);
            }

            _db.ProjectMedia.Add(new ProjectMediaEntity
            {
                ProjectMediaId = parsedProjectMediaId,
                ProjectId = project.ProjectId,
                MediaId = item.MediaId,
                OrderIndex = item.OrderIndex,
                ItemTitle = item.Title,
                ItemDescription = item.Description,
                AddedUtc = now
            });
        }

        var proposal = await _db.ProjectChangeProposals
            .FirstOrDefaultAsync(x =>
                x.ProjectId == project.ProjectId &&
                x.ProjectManifestVersionId == targetVersion.ProjectManifestVersionId &&
                x.Status == ProposalPending, ct);
        if (proposal is not null)
        {
            proposal.Status = ProposalAccepted;
            proposal.ReviewedUtc = now;
            proposal.ReviewedByUserId = reviewedByUserId;
        }

        var supersededBaseVersion = targetVersion.BasedOnVersionNumber;
        if (supersededBaseVersion.HasValue)
        {
            var superseded = await _db.ProjectChangeProposals
                .Where(x =>
                    x.ProjectId == project.ProjectId &&
                    x.Status == ProposalPending &&
                    x.BaseVersionNumber == supersededBaseVersion.Value &&
                    x.ProposedVersionNumber != targetVersion.VersionNumber)
                .ToListAsync(ct);
            foreach (var row in superseded)
            {
                row.Status = ProposalSuperseded;
                row.ReviewedUtc = now;
                row.ReviewedByUserId = reviewedByUserId;
            }
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        proposal ??= await _db.ProjectChangeProposals
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectManifestVersionId == targetVersion.ProjectManifestVersionId, ct);

        return ToSummary(targetVersion, proposal);
    }

    private async Task<int?> GetCurrentPublishedVersionNumberAsync(ProjectEntity project, CancellationToken ct)
    {
        if (project.CurrentPublishedVersionNumber.HasValue)
        {
            return project.CurrentPublishedVersionNumber.Value;
        }

        var fallback = await _db.ProjectManifestVersions
            .AsNoTracking()
            .Where(x => x.ProjectId == project.ProjectId && x.IsPublished)
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => (int?)x.VersionNumber)
            .FirstOrDefaultAsync(ct);

        return fallback;
    }

    private async Task<ProjectManifestVersionEntity?> LoadVersionByProjectAndVersionAsync(Guid projectId, int version, CancellationToken ct)
    {
        return await _db.ProjectManifestVersions
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.VersionNumber == version, ct);
    }

    private static List<ProjectManifestMediaItemDto> NormalizeManifestItems(List<ProjectManifestMediaItemDto>? items)
    {
        var ordered = (items ?? new List<ProjectManifestMediaItemDto>())
            .OrderBy(x => x.OrderIndex)
            .ToList();

        var normalized = new List<ProjectManifestMediaItemDto>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            var source = ordered[index];
            normalized.Add(new ProjectManifestMediaItemDto
            {
                MediaId = source.MediaId,
                AddedByUserId = source.AddedByUserId,
                ProjectMediaKey = source.ProjectMediaKey,
                Title = source.Title,
                Description = source.Description,
                OrderIndex = index
            });
        }

        return normalized;
    }

    private static ManifestConflictException BuildStaleBaseConflict(
        Guid projectId,
        int requestedVersion,
        int? basedOnVersion,
        int? currentPublishedVersion)
    {
        return new ManifestConflictException(new ProjectManifestConflictDto
        {
            ProjectId = projectId,
            RequestedVersion = requestedVersion,
            BasedOnVersion = basedOnVersion,
            CurrentPublishedVersion = currentPublishedVersion,
            Code = "ManifestBaseVersionConflict",
            Message = "The requested manifest version is based on a stale published version."
        });
    }

    private static ProjectManifestDto DeserializeManifest(ProjectManifestVersionEntity entity)
    {
        var parsed = JsonSerializer.Deserialize<ProjectManifestDto>(entity.ManifestJson, JsonOptions) ??
                     new ProjectManifestDto();

        parsed.ProjectId = entity.ProjectId;
        parsed.CreatedByUserId = entity.CreatedByUserId;
        parsed.Version = entity.VersionNumber;
        parsed.BasedOnVersion = entity.BasedOnVersionNumber;
        parsed.MediaItems ??= new List<ProjectManifestMediaItemDto>();

        return parsed;
    }

    private static ProjectManifestVersionSummaryDto ToSummary(
        ProjectManifestVersionEntity entity,
        ProjectChangeProposalEntity? proposal = null)
    {
        return new ProjectManifestVersionSummaryDto
        {
            ProjectManifestVersionId = entity.ProjectManifestVersionId,
            ProjectId = entity.ProjectId,
            VersionNumber = entity.VersionNumber,
            BasedOnVersionNumber = entity.BasedOnVersionNumber,
            CreatedByUserId = entity.CreatedByUserId,
            CreatedUtc = entity.CreatedUtc,
            ChangeSummary = entity.ChangeSummary,
            IsPublished = entity.IsPublished,
            IsProposal = proposal is not null,
            ProposalStatus = proposal?.Status,
            ReviewedByUserId = proposal?.ReviewedByUserId,
            ReviewedUtc = proposal?.ReviewedUtc
        };
    }

    private static List<ProjectManifestChangeDto> BuildSemanticDiffChanges(
        ProjectManifestDto oldManifest,
        ProjectManifestDto newManifest)
    {
        var oldByKey = BuildItemMap(oldManifest.MediaItems);
        var newByKey = BuildItemMap(newManifest.MediaItems);
        var oldKeys = oldByKey.Keys.ToHashSet(StringComparer.Ordinal);
        var newKeys = newByKey.Keys.ToHashSet(StringComparer.Ordinal);
        var changes = new List<ProjectManifestChangeDto>();

        if (!string.Equals(oldManifest.Title?.Trim(), newManifest.Title?.Trim(), StringComparison.Ordinal))
        {
            changes.Add(new ProjectManifestChangeDto
            {
                Type = "ProjectTitleChanged",
                OldTitle = oldManifest.Title,
                NewTitle = newManifest.Title,
                Message = "Project title changed"
            });
        }

        if (!string.Equals(oldManifest.Description?.Trim(), newManifest.Description?.Trim(), StringComparison.Ordinal))
        {
            changes.Add(new ProjectManifestChangeDto
            {
                Type = "ProjectDescriptionChanged",
                OldDescription = oldManifest.Description,
                NewDescription = newManifest.Description,
                Message = "Project description changed"
            });
        }

        foreach (var key in oldKeys.Except(newKeys, StringComparer.Ordinal))
        {
            var oldItem = oldByKey[key];
            changes.Add(new ProjectManifestChangeDto
            {
                Type = "MediaRemoved",
                MediaId = oldItem.MediaId,
                ProjectMediaKey = oldItem.ProjectMediaKey,
                AddedByUserId = oldItem.AddedByUserId,
                OldTitle = oldItem.Title,
                OldDescription = oldItem.Description,
                OldOrderIndex = oldItem.OrderIndex,
                Message = $"Clip removed from position {oldItem.OrderIndex}"
            });
        }

        foreach (var key in newKeys.Except(oldKeys, StringComparer.Ordinal))
        {
            var newItem = newByKey[key];
            changes.Add(new ProjectManifestChangeDto
            {
                Type = "MediaAdded",
                MediaId = newItem.MediaId,
                ProjectMediaKey = newItem.ProjectMediaKey,
                AddedByUserId = newItem.AddedByUserId,
                NewTitle = newItem.Title,
                NewDescription = newItem.Description,
                NewOrderIndex = newItem.OrderIndex,
                Message = $"Clip added at position {newItem.OrderIndex}"
            });
        }

        foreach (var key in oldKeys.Intersect(newKeys, StringComparer.Ordinal))
        {
            var oldItem = oldByKey[key];
            var newItem = newByKey[key];

            if (oldItem.OrderIndex != newItem.OrderIndex)
            {
                changes.Add(new ProjectManifestChangeDto
                {
                    Type = "MediaReordered",
                    MediaId = newItem.MediaId,
                    ProjectMediaKey = newItem.ProjectMediaKey,
                    AddedByUserId = newItem.AddedByUserId,
                    OldOrderIndex = oldItem.OrderIndex,
                    NewOrderIndex = newItem.OrderIndex,
                    Message = $"Clip moved from position {oldItem.OrderIndex} to {newItem.OrderIndex}"
                });
            }

            if (!string.Equals(oldItem.Title?.Trim(), newItem.Title?.Trim(), StringComparison.Ordinal))
            {
                changes.Add(new ProjectManifestChangeDto
                {
                    Type = "MediaTitleChanged",
                    MediaId = newItem.MediaId,
                    ProjectMediaKey = newItem.ProjectMediaKey,
                    AddedByUserId = newItem.AddedByUserId,
                    OldTitle = oldItem.Title,
                    NewTitle = newItem.Title,
                    NewOrderIndex = newItem.OrderIndex,
                    Message = "Clip title changed"
                });
            }

            if (!string.Equals(oldItem.Description?.Trim(), newItem.Description?.Trim(), StringComparison.Ordinal))
            {
                changes.Add(new ProjectManifestChangeDto
                {
                    Type = "MediaDescriptionChanged",
                    MediaId = newItem.MediaId,
                    ProjectMediaKey = newItem.ProjectMediaKey,
                    AddedByUserId = newItem.AddedByUserId,
                    OldDescription = oldItem.Description,
                    NewDescription = newItem.Description,
                    NewOrderIndex = newItem.OrderIndex,
                    Message = "Clip description changed"
                });
            }
        }

        return changes
            .OrderBy(GetChangeSortRank)
            .ThenBy(x => x.NewOrderIndex ?? x.OldOrderIndex ?? int.MaxValue)
            .ThenBy(x => x.ProjectMediaKey ?? x.MediaId?.ToString() ?? string.Empty, StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, ProjectManifestMediaItemDto> BuildItemMap(IEnumerable<ProjectManifestMediaItemDto> items)
    {
        var map = new Dictionary<string, ProjectManifestMediaItemDto>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = GetManifestItemKey(item);
            if (!map.ContainsKey(key))
            {
                map[key] = item;
            }
        }

        return map;
    }

    private static string GetManifestItemKey(ProjectManifestMediaItemDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.ProjectMediaKey))
        {
            return $"k:{item.ProjectMediaKey.Trim()}";
        }

        return $"m:{item.MediaId:D}";
    }

    private static int GetChangeSortRank(ProjectManifestChangeDto change) => change.Type switch
    {
        "ProjectTitleChanged" => 0,
        "ProjectDescriptionChanged" => 1,
        "MediaRemoved" => 2,
        "MediaAdded" => 3,
        "MediaReordered" => 4,
        "MediaTitleChanged" => 5,
        "MediaDescriptionChanged" => 6,
        _ => 99
    };
}
