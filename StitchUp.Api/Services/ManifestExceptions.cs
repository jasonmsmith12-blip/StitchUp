using StitchUp.Contracts.Projects;

namespace StitchUp.Api.Services;

public sealed class ManifestConflictException : Exception
{
    public ManifestConflictException(ProjectManifestConflictDto conflict)
        : base(conflict.Message)
    {
        Conflict = conflict;
    }

    public ProjectManifestConflictDto Conflict { get; }
}

public sealed class ManifestForbiddenException : Exception
{
    public ManifestForbiddenException(string message)
        : base(message)
    {
    }
}

public sealed class ManifestNotFoundException : Exception
{
    public ManifestNotFoundException(string message)
        : base(message)
    {
    }
}
