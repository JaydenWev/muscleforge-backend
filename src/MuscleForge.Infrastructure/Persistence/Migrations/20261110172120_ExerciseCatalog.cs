using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuscleForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExerciseCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LoadConvention",
                table: "Exercises",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "TotalLoad");

            migrationBuilder.AddColumn<string>(
                name: "SeedKey",
                table: "Exercises",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_OwnerId_SeedKey",
                table: "Exercises",
                columns: new[] { "OwnerId", "SeedKey" },
                unique: true,
                filter: "\"SeedKey\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Exercises_LoadConvention_Supported",
                table: "Exercises",
                sql: "\"LoadConvention\" IN ('TotalLoad', 'PerDumbbell')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Exercises_SeedKey_NotBlank",
                table: "Exercises",
                sql: "\"SeedKey\" IS NULL OR \"SeedKey\" ~ U&'[^\\0009-\\000D\\0020\\0085\\00A0\\1680\\2000-\\200A\\2028\\2029\\202F\\205F\\3000]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Exercises_OwnerId_SeedKey",
                table: "Exercises");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Exercises_LoadConvention_Supported",
                table: "Exercises");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Exercises_SeedKey_NotBlank",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "LoadConvention",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "SeedKey",
                table: "Exercises");
        }
    }
}
