namespace StitchUp.Contracts.Projects;

public class ProjectManifestVersionSummaryDto
{
    public Guid ProjectManifestVersionId { get; set; }

    public Guid ProjectId { get; set; }

    public int VersionNumber { get; set; }

    public int? BasedOnVersionNumber { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedUtc { get; set; }

    public string? ChangeSummary { get; set; }

    public bool IsPublished { get; set; }

    public bool IsProposal { get; set; }

    public string? ProposalStatus { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedUtc { get; set; }
}
