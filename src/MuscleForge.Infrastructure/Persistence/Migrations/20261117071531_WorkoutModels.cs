using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuscleForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkoutModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Exercises_Id_OwnerId",
                table: "Exercises",
                columns: new[] { "Id", "OwnerId" });

            migrationBuilder.CreateTable(
                name: "WorkoutTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutTemplates", x => x.Id);
                    table.UniqueConstraint("AK_WorkoutTemplates_Id_OwnerId", x => new { x.Id, x.OwnerId });
                    table.CheckConstraint("CK_WorkoutTemplates_Name", "\"Name\" ~ U&'[^\\0009-\\000D\\0020\\0085\\00A0\\1680\\2000-\\200A\\2028\\2029\\202F\\205F\\3000]'");
                    table.CheckConstraint("CK_WorkoutTemplates_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_WorkoutTemplates_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemplateExercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    RestSeconds = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateExercises", x => x.Id);
                    table.UniqueConstraint("AK_TemplateExercises_Id_OwnerId", x => new { x.Id, x.OwnerId });
                    table.CheckConstraint("CK_TemplateExercises_Position", "\"Position\" BETWEEN 0 AND 99 AND (\"RestSeconds\" IS NULL OR \"RestSeconds\" BETWEEN 0 AND 86400)");
                    table.ForeignKey(
                        name: "FK_TemplateExercises_Exercises_ExerciseId_OwnerId",
                        columns: x => new { x.ExerciseId, x.OwnerId },
                        principalTable: "Exercises",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemplateExercises_WorkoutTemplates_TemplateId_OwnerId",
                        columns: x => new { x.TemplateId, x.OwnerId },
                        principalTable: "WorkoutTemplates",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceTemplateVersion = table.Column<int>(type: "integer", nullable: true),
                    StartRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutSessions", x => x.Id);
                    table.UniqueConstraint("AK_WorkoutSessions_Id_OwnerId", x => new { x.Id, x.OwnerId });
                    table.CheckConstraint("CK_WorkoutSessions_End", "(\"Status\" = 'InProgress' AND \"EndedAt\" IS NULL) OR (\"Status\" <> 'InProgress' AND \"EndedAt\" IS NOT NULL AND \"EndedAt\" >= \"StartedAt\")");
                    table.CheckConstraint("CK_WorkoutSessions_StartHash", "\"StartRequestHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_WorkoutSessions_Status", "\"Status\" IN ('InProgress', 'Completed', 'Discarded')");
                    table.CheckConstraint("CK_WorkoutSessions_Template", "(\"TemplateId\" IS NULL AND \"TemplateName\" IS NULL AND \"SourceTemplateVersion\" IS NULL) OR (\"TemplateId\" IS NOT NULL AND \"TemplateName\" IS NOT NULL AND \"SourceTemplateVersion\" IS NOT NULL AND \"SourceTemplateVersion\" >= 1)");
                    table.CheckConstraint("CK_WorkoutSessions_TimeZone", "\"TimeZoneId\" ~ U&'[^\\0009-\\000D\\0020\\0085\\00A0\\1680\\2000-\\200A\\2028\\2029\\202F\\205F\\3000]'");
                    table.CheckConstraint("CK_WorkoutSessions_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_WorkoutSessions_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkoutSessions_WorkoutTemplates_TemplateId_OwnerId",
                        columns: x => new { x.TemplateId, x.OwnerId },
                        principalTable: "WorkoutTemplates",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemplateSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetWeightKg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    TargetRepsMin = table.Column<int>(type: "integer", nullable: true),
                    TargetRepsMax = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateSets", x => x.Id);
                    table.CheckConstraint("CK_TemplateSets_Kind", "\"Kind\" IN ('Warmup', 'Working')");
                    table.CheckConstraint("CK_TemplateSets_Position", "\"Position\" BETWEEN 0 AND 99");
                    table.CheckConstraint("CK_TemplateSets_Targets", "(\"TargetWeightKg\" IS NULL OR \"TargetWeightKg\" >= 0 AND \"TargetWeightKg\" <= 9999999.999) AND ((\"TargetRepsMin\" IS NULL AND \"TargetRepsMax\" IS NULL) OR (\"TargetRepsMin\" IS NOT NULL AND \"TargetRepsMax\" IS NOT NULL AND \"TargetRepsMin\" > 0 AND \"TargetRepsMax\" >= \"TargetRepsMin\"))");
                    table.ForeignKey(
                        name: "FK_TemplateSets_TemplateExercises_TemplateExerciseId_OwnerId",
                        columns: x => new { x.TemplateExerciseId, x.OwnerId },
                        principalTable: "TemplateExercises",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionExercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    RestSeconds = table.Column<int>(type: "integer", nullable: true),
                    ExerciseName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Equipment = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TrackingType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LoadConvention = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionExercises", x => x.Id);
                    table.UniqueConstraint("AK_SessionExercises_Id_OwnerId", x => new { x.Id, x.OwnerId });
                    table.CheckConstraint("CK_SessionExercises_Equipment", "\"Equipment\" ~ U&'[^\\0009-\\000D\\0020\\0085\\00A0\\1680\\2000-\\200A\\2028\\2029\\202F\\205F\\3000]'");
                    table.CheckConstraint("CK_SessionExercises_Load", "\"LoadConvention\" IN ('TotalLoad', 'PerDumbbell')");
                    table.CheckConstraint("CK_SessionExercises_Name", "\"ExerciseName\" ~ U&'[^\\0009-\\000D\\0020\\0085\\00A0\\1680\\2000-\\200A\\2028\\2029\\202F\\205F\\3000]'");
                    table.CheckConstraint("CK_SessionExercises_Position", "\"Position\" BETWEEN 0 AND 99 AND (\"RestSeconds\" IS NULL OR \"RestSeconds\" BETWEEN 0 AND 86400)");
                    table.CheckConstraint("CK_SessionExercises_Tracking", "\"TrackingType\" = 'ExternalWeightAndReps'");
                    table.ForeignKey(
                        name: "FK_SessionExercises_Exercises_ExerciseId_OwnerId",
                        columns: x => new { x.ExerciseId, x.OwnerId },
                        principalTable: "Exercises",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionExercises_WorkoutSessions_SessionId_OwnerId",
                        columns: x => new { x.SessionId, x.OwnerId },
                        principalTable: "WorkoutSessions",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutMutationReceipts",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MutationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SemanticRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultVersion = table.Column<int>(type: "integer", nullable: false),
                    Acknowledgement = table.Column<string>(type: "character varying(262144)", maxLength: 262144, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutMutationReceipts", x => new { x.OwnerId, x.MutationId });
                    table.CheckConstraint("CK_WorkoutMutationReceipts_Acknowledgement", "jsonb_typeof(\"Acknowledgement\"::jsonb) = 'object'");
                    table.CheckConstraint("CK_WorkoutMutationReceipts_Hash", "\"SemanticRequestHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_WorkoutMutationReceipts_Version", "\"ResultVersion\" >= 1");
                    table.ForeignKey(
                        name: "FK_WorkoutMutationReceipts_WorkoutSessions_SessionId_OwnerId",
                        columns: x => new { x.SessionId, x.OwnerId },
                        principalTable: "WorkoutSessions",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetWeightKg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    TargetRepsMin = table.Column<int>(type: "integer", nullable: true),
                    TargetRepsMax = table.Column<int>(type: "integer", nullable: true),
                    WeightKg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    Reps = table.Column<int>(type: "integer", nullable: true),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutSets", x => x.Id);
                    table.CheckConstraint("CK_WorkoutSets_Kind", "\"Kind\" IN ('Warmup', 'Working') AND \"TargetKind\" IN ('Warmup', 'Working')");
                    table.CheckConstraint("CK_WorkoutSets_Position", "\"Position\" BETWEEN 0 AND 99");
                    table.CheckConstraint("CK_WorkoutSets_Values", "(\"TargetWeightKg\" IS NULL OR \"TargetWeightKg\" >= 0 AND \"TargetWeightKg\" <= 9999999.999) AND ((\"TargetRepsMin\" IS NULL AND \"TargetRepsMax\" IS NULL) OR (\"TargetRepsMin\" IS NOT NULL AND \"TargetRepsMax\" IS NOT NULL AND \"TargetRepsMin\" > 0 AND \"TargetRepsMax\" >= \"TargetRepsMin\")) AND (\"WeightKg\" IS NULL OR \"WeightKg\" >= 0 AND \"WeightKg\" <= 9999999.999) AND (\"Reps\" IS NULL OR \"Reps\" > 0) AND (NOT \"Completed\" OR (\"WeightKg\" IS NOT NULL AND \"Reps\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_WorkoutSets_SessionExercises_SessionExerciseId_OwnerId",
                        columns: x => new { x.SessionExerciseId, x.OwnerId },
                        principalTable: "SessionExercises",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionExercises_ExerciseId_OwnerId",
                table: "SessionExercises",
                columns: new[] { "ExerciseId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionExercises_SessionId_OwnerId",
                table: "SessionExercises",
                columns: new[] { "SessionId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionExercises_SessionId_Position",
                table: "SessionExercises",
                columns: new[] { "SessionId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemplateExercises_ExerciseId_OwnerId",
                table: "TemplateExercises",
                columns: new[] { "ExerciseId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateExercises_TemplateId_OwnerId",
                table: "TemplateExercises",
                columns: new[] { "TemplateId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateExercises_TemplateId_Position",
                table: "TemplateExercises",
                columns: new[] { "TemplateId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemplateSets_TemplateExerciseId_OwnerId",
                table: "TemplateSets",
                columns: new[] { "TemplateExerciseId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_TemplateSets_TemplateExerciseId_Position",
                table: "TemplateSets",
                columns: new[] { "TemplateExerciseId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutMutationReceipts_SessionId_OwnerId",
                table: "WorkoutMutationReceipts",
                columns: new[] { "SessionId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSessions_OwnerId",
                table: "WorkoutSessions",
                column: "OwnerId",
                unique: true,
                filter: "\"Status\" = 'InProgress'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSessions_OwnerId_StartedAt_Id",
                table: "WorkoutSessions",
                columns: new[] { "OwnerId", "StartedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSessions_TemplateId_OwnerId",
                table: "WorkoutSessions",
                columns: new[] { "TemplateId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSets_SessionExerciseId_OwnerId",
                table: "WorkoutSets",
                columns: new[] { "SessionExerciseId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSets_SessionExerciseId_Position",
                table: "WorkoutSets",
                columns: new[] { "SessionExerciseId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutTemplates_OwnerId",
                table: "WorkoutTemplates",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TemplateSets");

            migrationBuilder.DropTable(
                name: "WorkoutMutationReceipts");

            migrationBuilder.DropTable(
                name: "WorkoutSets");

            migrationBuilder.DropTable(
                name: "TemplateExercises");

            migrationBuilder.DropTable(
                name: "SessionExercises");

            migrationBuilder.DropTable(
                name: "WorkoutSessions");

            migrationBuilder.DropTable(
                name: "WorkoutTemplates");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Exercises_Id_OwnerId",
                table: "Exercises");
        }
    }
}
