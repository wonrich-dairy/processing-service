using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcessingService.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingRunsAndUnloadDeviation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTemperatureDeviation",
                table: "unloads",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "processing_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UnloadId = table.Column<Guid>(type: "char(36)", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    StateChangedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_processing_runs_unloads_UnloadId",
                        column: x => x.UnloadId,
                        principalTable: "unloads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ux_processing_runs_unload_id",
                table: "processing_runs",
                column: "UnloadId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processing_runs");

            migrationBuilder.DropColumn(
                name: "IsTemperatureDeviation",
                table: "unloads");
        }
    }
}
