using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuscleForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "AspNetUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Etc/UTC");

            migrationBuilder.AddColumn<string>(
                name: "WeightUnit",
                table: "AspNetUsers",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "kg");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AspNetUsers_WeightUnit_Supported",
                table: "AspNetUsers",
                sql: "\"WeightUnit\" IN ('kg', 'lb')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AspNetUsers_WeightUnit_Supported",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WeightUnit",
                table: "AspNetUsers");
        }
    }
}
