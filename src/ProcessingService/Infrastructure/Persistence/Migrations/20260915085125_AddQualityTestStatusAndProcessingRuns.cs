using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcessingService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityTestStatusAndProcessingRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QualityTestStatus",
                table: "processing_runs",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QualityTestStatus",
                table: "processing_runs");
        }
    }
}
