using Microsoft.EntityFrameworkCore;
using StitchUp.Domain.Entities.Server;

namespace StitchUp.Infrastructure.Data;

public class StitchUpDbContext : DbContext
{
    public StitchUpDbContext(DbContextOptions<StitchUpDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserEntity> Users => Set<UserEntity>();

    public DbSet<ProjectEntity> Projects => Set<ProjectEntity>();

    public DbSet<MediaEntity> Media => Set<MediaEntity>();

    public DbSet<ProjectMediaEntity> ProjectMedia => Set<ProjectMediaEntity>();

    public DbSet<MediaConversionAttemptEntity> MediaConversionAttempts => Set<MediaConversionAttemptEntity>();

    public DbSet<MediaBlobEntity> MediaBlobs => Set<MediaBlobEntity>();

    public DbSet<ProjectManifestVersionEntity> ProjectManifestVersions => Set<ProjectManifestVersionEntity>();

    public DbSet<ProjectChangeProposalEntity> ProjectChangeProposals => Set<ProjectChangeProposalEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");

        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("User");
            entity.HasKey(x => x.UserId);

            entity.Property(x => x.UserName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Email)
                .HasMaxLength(255);

            entity.HasIndex(x => x.UserName)
                .IsUnique();
        });

        modelBuilder.Entity<ProjectEntity>(entity =>
        {
            entity.ToTable("Project");
            entity.HasKey(x => x.ProjectId);

            entity.Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(200);

            entity.HasOne(x => x.AuthorUser)
                .WithMany(x => x.Projects)
                .HasForeignKey(x => x.AuthorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CurrentPublishedManifestVersion)
                .WithMany()
                .HasForeignKey(x => new { x.ProjectId, x.CurrentPublishedVersionNumber })
                .HasPrincipalKey(x => new { x.ProjectId, x.VersionNumber })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MediaEntity>(entity =>
        {
            entity.ToTable("Media");
            entity.HasKey(x => x.MediaId);

            entity.Property(x => x.MediaType)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(x => x.Title)
                .HasMaxLength(200);

            entity.Property(x => x.BlobPath)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(x => x.OriginalBlobPath)
                .HasMaxLength(500);

            entity.Property(x => x.WasCloudConverted)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(x => x.CloudConversionStatus)
                .IsRequired()
                .HasMaxLength(32)
                .HasDefaultValue("NotRequested");

            entity.Property(x => x.CloudConversionError)
                .HasMaxLength(1024);

            entity.Property(x => x.CanonicalBlobPath)
                .HasMaxLength(500);

            entity.Property(x => x.CanonicalContainer)
                .HasMaxLength(100);

            entity.Property(x => x.StorageState)
                .IsRequired()
                .HasMaxLength(32)
                .HasDefaultValue("LocalOnly");

            entity.Property(x => x.IsTemporary)
                .IsRequired()
                .HasDefaultValue(false);

            entity.HasOne(x => x.AuthorUser)
                .WithMany(x => x.Media)
                .HasForeignKey(x => x.AuthorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MediaBlobEntity>(entity =>
        {
            entity.ToTable("MediaBlob");
            entity.HasKey(x => x.MediaBlobId);

            entity.Property(x => x.MediaBlobId)
                .HasDefaultValueSql("NEWID()");

            entity.Property(x => x.BlobRole)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(x => x.ContainerName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.BlobPath)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(x => x.IsTemporary)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(x => x.CreatedUtc)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(x => x.Media)
                .WithMany(x => x.MediaBlobs)
                .HasForeignKey(x => x.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaConversionAttemptEntity>(entity =>
        {
            entity.ToTable("MediaConversionAttempt");
            entity.HasKey(x => x.ConversionAttemptId);

            entity.Property(x => x.ConversionAttemptId)
                .HasDefaultValueSql("NEWID()");

            entity.Property(x => x.AttemptedUtc)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property(x => x.AttemptSource)
                .IsRequired()
                .HasMaxLength(16);

            entity.Property(x => x.InputFormatSummary)
                .HasMaxLength(256);

            entity.Property(x => x.ErrorMessage)
                .HasMaxLength(1024);

            entity.Property(x => x.OutputBlobPath)
                .HasMaxLength(500);

            entity.HasOne(x => x.Media)
                .WithMany(m => m.ConversionAttempts)
                .HasForeignKey(x => x.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProjectMediaEntity>(entity =>
        {
            entity.ToTable("ProjectMedia");
            entity.HasKey(x => x.ProjectMediaId);

            entity.Property(x => x.ItemTitle)
                .HasMaxLength(200);

            entity.HasIndex(x => new { x.ProjectId, x.OrderIndex })
                .IsUnique(false);

            entity.HasOne(x => x.Project)
                .WithMany(x => x.ProjectMedia)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Media)
                .WithMany(x => x.ProjectMedia)
                .HasForeignKey(x => x.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProjectManifestVersionEntity>(entity =>
        {
            entity.ToTable("ProjectManifestVersion");
            entity.HasKey(x => x.ProjectManifestVersionId);

            entity.Property(x => x.ProjectManifestVersionId)
                .HasDefaultValueSql("NEWID()");

            entity.Property(x => x.CreatedUtc)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property(x => x.ManifestJson)
                .IsRequired();

            entity.Property(x => x.ChangeSummary)
                .HasMaxLength(1000);

            entity.Property(x => x.IsPublished)
                .IsRequired()
                .HasDefaultValue(false);

            entity.HasIndex(x => new { x.ProjectId, x.VersionNumber })
                .IsUnique();

            entity.HasIndex(x => x.ProjectId)
                .IsUnique()
                .HasFilter("[IsPublished] = 1");

            entity.HasOne(x => x.Project)
                .WithMany(x => x.ManifestVersions)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.CreatedByUser)
                .WithMany(x => x.ProjectManifestVersions)
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectChangeProposalEntity>(entity =>
        {
            entity.ToTable("ProjectChangeProposal");
            entity.HasKey(x => x.ProjectChangeProposalId);

            entity.Property(x => x.ProjectChangeProposalId)
                .HasDefaultValueSql("NEWID()");

            entity.Property(x => x.Status)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(x => x.ChangeSummary)
                .HasMaxLength(1000);

            entity.Property(x => x.CreatedUtc)
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => new { x.ProjectId, x.ProposedVersionNumber })
                .IsUnique();

            entity.HasOne(x => x.Project)
                .WithMany(x => x.ChangeProposals)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ProposedByUser)
                .WithMany(x => x.ProjectChangeProposals)
                .HasForeignKey(x => x.ProposedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ReviewedByUser)
                .WithMany(x => x.ReviewedProjectChangeProposals)
                .HasForeignKey(x => x.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ProjectManifestVersion)
                .WithMany(x => x.ChangeProposals)
                .HasForeignKey(x => x.ProjectManifestVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
