using Microsoft.EntityFrameworkCore;
using MuscleForge.Core.Exercises;
using MuscleForge.Core.Sessions;
using MuscleForge.Core.Templates;
using MuscleForge.Infrastructure.Identity;

namespace MuscleForge.Infrastructure.Persistence;

internal static class WorkoutMapping
{
    internal static void ConfigureWorkouts(this ModelBuilder builder)
    {
        builder.Entity<WorkoutTemplate>(entity =>
        {
            entity.ToTable("WorkoutTemplates", table =>
            {
                table.HasCheckConstraint("CK_WorkoutTemplates_Version", "\"Version\" >= 1");
                table.HasCheckConstraint(
                    "CK_WorkoutTemplates_Name",
                    "\"Name\" ~ " + MuscleForgeDbContext.NonWhitespacePattern);
            });
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.Id, x.OwnerId });
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Exercises)
                .WithOne()
                .HasForeignKey(x => new { x.TemplateId, x.OwnerId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.Exercises).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<TemplateExercise>(entity =>
        {
            entity.ToTable("TemplateExercises", table =>
                table.HasCheckConstraint(
                    "CK_TemplateExercises_Position",
                    "\"Position\" BETWEEN 0 AND 99 AND (\"RestSeconds\" IS NULL OR \"RestSeconds\" BETWEEN 0 AND 86400)"));
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.Id, x.OwnerId });
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasIndex(x => new { x.TemplateId, x.Position }).IsUnique();
            entity.HasOne<Exercise>()
                .WithMany()
                .HasForeignKey(x => new { x.ExerciseId, x.OwnerId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Sets)
                .WithOne()
                .HasForeignKey(x => new { x.TemplateExerciseId, x.OwnerId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.Sets).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<TemplateSet>(entity =>
        {
            entity.ToTable("TemplateSets", table =>
            {
                table.HasCheckConstraint("CK_TemplateSets_Position", "\"Position\" BETWEEN 0 AND 99");
                table.HasCheckConstraint("CK_TemplateSets_Kind", "\"Kind\" IN ('Warmup', 'Working')");
                table.HasCheckConstraint(
                    "CK_TemplateSets_Targets",
                    "(\"TargetWeightKg\" IS NULL OR \"TargetWeightKg\" >= 0 AND \"TargetWeightKg\" <= 9999999.999) AND ((\"TargetRepsMin\" IS NULL AND \"TargetRepsMax\" IS NULL) OR (\"TargetRepsMin\" IS NOT NULL AND \"TargetRepsMax\" IS NOT NULL AND \"TargetRepsMin\" > 0 AND \"TargetRepsMax\" >= \"TargetRepsMin\"))");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasIndex(x => new { x.TemplateExerciseId, x.Position }).IsUnique();
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.TargetWeightKg).HasPrecision(10, 3);
        });

        builder.Entity<WorkoutSession>(entity =>
        {
            entity.ToTable("WorkoutSessions", table =>
            {
                table.HasCheckConstraint("CK_WorkoutSessions_Status", "\"Status\" IN ('InProgress', 'Completed', 'Discarded')");
                table.HasCheckConstraint("CK_WorkoutSessions_Version", "\"Version\" >= 1");
                table.HasCheckConstraint(
                    "CK_WorkoutSessions_Template",
                    "(\"TemplateId\" IS NULL AND \"TemplateName\" IS NULL AND \"SourceTemplateVersion\" IS NULL) OR (\"TemplateId\" IS NOT NULL AND \"TemplateName\" IS NOT NULL AND \"SourceTemplateVersion\" IS NOT NULL AND \"SourceTemplateVersion\" >= 1)");
                table.HasCheckConstraint(
                    "CK_WorkoutSessions_End",
                    "(\"Status\" = 'InProgress' AND \"EndedAt\" IS NULL) OR (\"Status\" <> 'InProgress' AND \"EndedAt\" IS NOT NULL AND \"EndedAt\" >= \"StartedAt\")");
                table.HasCheckConstraint(
                    "CK_WorkoutSessions_TimeZone",
                    "\"TimeZoneId\" ~ " + MuscleForgeDbContext.NonWhitespacePattern);
                table.HasCheckConstraint(
                    "CK_WorkoutSessions_StartHash",
                    "\"StartRequestHash\" ~ '^[0-9a-f]{64}$'");
            });
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.Id, x.OwnerId });
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TemplateName).HasMaxLength(100);
            entity.Property(x => x.StartRequestHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasIndex(x => new { x.OwnerId, x.StartedAt, x.Id });
            entity.HasIndex(x => x.OwnerId).IsUnique().HasFilter("\"Status\" = 'InProgress'");
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<WorkoutTemplate>()
                .WithMany()
                .HasForeignKey(x => new { x.TemplateId, x.OwnerId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Exercises)
                .WithOne()
                .HasForeignKey(x => new { x.SessionId, x.OwnerId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.Exercises).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<SessionExercise>(entity =>
        {
            entity.ToTable("SessionExercises", table =>
            {
                table.HasCheckConstraint(
                    "CK_SessionExercises_Position",
                    "\"Position\" BETWEEN 0 AND 99 AND (\"RestSeconds\" IS NULL OR \"RestSeconds\" BETWEEN 0 AND 86400)");
                table.HasCheckConstraint("CK_SessionExercises_Tracking", "\"TrackingType\" = 'ExternalWeightAndReps'");
                table.HasCheckConstraint(
                    "CK_SessionExercises_Name",
                    "\"ExerciseName\" ~ " + MuscleForgeDbContext.NonWhitespacePattern);
                table.HasCheckConstraint(
                    "CK_SessionExercises_Equipment",
                    "\"Equipment\" ~ " + MuscleForgeDbContext.NonWhitespacePattern);
                table.HasCheckConstraint(
                    "CK_SessionExercises_Load",
                    "\"LoadConvention\" IN ('TotalLoad', 'PerDumbbell')");
            });
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.Id, x.OwnerId });
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasIndex(x => new { x.SessionId, x.Position }).IsUnique();
            entity.Property(x => x.ExerciseName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Equipment).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TrackingType).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.LoadConvention).HasConversion<string>().HasMaxLength(32);
            entity.HasOne<Exercise>()
                .WithMany()
                .HasForeignKey(x => new { x.ExerciseId, x.OwnerId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Sets)
                .WithOne()
                .HasForeignKey(x => new { x.SessionExerciseId, x.OwnerId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.Sets).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<WorkoutSet>(entity =>
        {
            entity.ToTable("WorkoutSets", table =>
            {
                table.HasCheckConstraint("CK_WorkoutSets_Position", "\"Position\" BETWEEN 0 AND 99");
                table.HasCheckConstraint(
                    "CK_WorkoutSets_Kind",
                    "\"Kind\" IN ('Warmup', 'Working') AND \"TargetKind\" IN ('Warmup', 'Working')");
                table.HasCheckConstraint(
                    "CK_WorkoutSets_Values",
                    "(\"TargetWeightKg\" IS NULL OR \"TargetWeightKg\" >= 0 AND \"TargetWeightKg\" <= 9999999.999) AND ((\"TargetRepsMin\" IS NULL AND \"TargetRepsMax\" IS NULL) OR (\"TargetRepsMin\" IS NOT NULL AND \"TargetRepsMax\" IS NOT NULL AND \"TargetRepsMin\" > 0 AND \"TargetRepsMax\" >= \"TargetRepsMin\")) AND (\"WeightKg\" IS NULL OR \"WeightKg\" >= 0 AND \"WeightKg\" <= 9999999.999) AND (\"Reps\" IS NULL OR \"Reps\" > 0) AND (NOT \"Completed\" OR (\"WeightKg\" IS NOT NULL AND \"Reps\" IS NOT NULL))");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasIndex(x => new { x.SessionExerciseId, x.Position }).IsUnique();
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.TargetKind).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.TargetWeightKg).HasPrecision(10, 3);
            entity.Property(x => x.WeightKg).HasPrecision(10, 3);
            entity.Property(x => x.Notes).HasMaxLength(2000);
        });

        builder.Entity<WorkoutMutationReceipt>(entity =>
        {
            entity.ToTable("WorkoutMutationReceipts", table =>
            {
                table.HasCheckConstraint("CK_WorkoutMutationReceipts_Version", "\"ResultVersion\" >= 1");
                table.HasCheckConstraint(
                    "CK_WorkoutMutationReceipts_Acknowledgement",
                    "jsonb_typeof(\"Acknowledgement\"::jsonb) = 'object'");
                table.HasCheckConstraint(
                    "CK_WorkoutMutationReceipts_Hash",
                    "\"SemanticRequestHash\" ~ '^[0-9a-f]{64}$'");
            });
            entity.HasKey(x => new { x.OwnerId, x.MutationId });
            entity.Property(x => x.MutationId).ValueGeneratedNever();
            entity.Property(x => x.SemanticRequestHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Acknowledgement).HasMaxLength(262144).IsRequired();
            entity.HasOne<WorkoutSession>()
                .WithMany()
                .HasForeignKey(x => new { x.SessionId, x.OwnerId })
                .HasPrincipalKey(x => new { x.Id, x.OwnerId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
