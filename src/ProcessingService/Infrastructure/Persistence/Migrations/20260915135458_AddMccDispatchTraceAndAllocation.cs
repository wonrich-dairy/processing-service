using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcessingService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMccDispatchTraceAndAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mcc_dispatch_traces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Reference = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BowserRegistration = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DispatchDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    TotalQuantityLitres = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DispatchedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TotalUnloadedKg = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mcc_dispatch_traces", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "processing_run_storing_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProcessingRunId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StoringTankId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    QuantityKg = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_run_storing_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_processing_run_storing_allocations_processing_runs_Processin~",
                        column: x => x.ProcessingRunId,
                        principalTable: "processing_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_processing_run_storing_allocations_tanks_StoringTankId",
                        column: x => x.StoringTankId,
                        principalTable: "tanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ux_mcc_dispatch_traces_reference",
                table: "mcc_dispatch_traces",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_run_storing_allocations_StoringTankId",
                table: "processing_run_storing_allocations",
                column: "StoringTankId");

            migrationBuilder.CreateIndex(
                name: "ix_storing_alloc_run_tank",
                table: "processing_run_storing_allocations",
                columns: new[] { "ProcessingRunId", "StoringTankId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mcc_dispatch_traces");

            migrationBuilder.DropTable(
                name: "processing_run_storing_allocations");
        }
    }
}
