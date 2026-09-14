using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ProcessingService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tanks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CapacityKg = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    RemainingKg = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RowVersion = table.Column<DateTime>(type: "timestamp(6)", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tanks", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "processing_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DispatchNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StoringTankId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    QuantityKg = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    TemperatureC = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    IsTemperatureDeviation = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    State = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HoldReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BatchCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_processing_runs_tanks_StoringTankId",
                        column: x => x.StoringTankId,
                        principalTable: "tanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "processing_stages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProcessingRunId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    MixingTankId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StageType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EndTemperatureC = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    IsDeviation = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    CultureAdded = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CultureAddedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_stages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_processing_stages_processing_runs_ProcessingRunId",
                        column: x => x.ProcessingRunId,
                        principalTable: "processing_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_processing_stages_tanks_MixingTankId",
                        column: x => x.MixingTankId,
                        principalTable: "tanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "quality_panels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProcessingRunId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DispatchNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FatPercent = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    RawLactometerReading = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    TemperatureCelsius = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    WaterPercent = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Snf = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Ts = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Ph = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    KqColour = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AlcoholOutcomesJson = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AlcoholResult = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SmellOk = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ColourOk = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TasteOk = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Verdict = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FailedParameter = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FailedValue = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsSmellConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsTasteConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConfirmedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_panels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quality_panels_processing_runs_ProcessingRunId",
                        column: x => x.ProcessingRunId,
                        principalTable: "processing_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tank_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProcessingRunId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SourceStoringTankId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DestinationMixingTankId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    QuantityKg = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    ProductType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BatchNumber = table.Column<int>(type: "int", nullable: false),
                    BatchLetter = table.Column<string>(type: "varchar(2)", maxLength: 2, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BatchCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AllocatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    OverrideReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tank_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tank_allocations_processing_runs_ProcessingRunId",
                        column: x => x.ProcessingRunId,
                        principalTable: "processing_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tank_allocations_tanks_DestinationMixingTankId",
                        column: x => x.DestinationMixingTankId,
                        principalTable: "tanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tank_allocations_tanks_SourceStoringTankId",
                        column: x => x.SourceStoringTankId,
                        principalTable: "tanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "tanks",
                columns: new[] { "Id", "CapacityKg", "Code", "CreatedAtUtc", "CreatedBy", "Kind", "RemainingKg", "Status", "UpdatedAtUtc", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), 5000m, "ST-01", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Storing", 0m, "Active", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), 5000m, "ST-02", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Storing", 0m, "Active", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed" },
                    { new Guid("33333333-3333-3333-3333-333333333333"), 5000m, "ST-03", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Storing", 0m, "Active", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed" },
                    { new Guid("44444444-4444-4444-4444-444444444444"), 3000m, "MT-01", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Mixing", 0m, "Active", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed" },
                    { new Guid("55555555-5555-5555-5555-555555555555"), 3000m, "MT-02", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Mixing", 0m, "Active", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed" },
                    { new Guid("66666666-6666-6666-6666-666666666666"), 3000m, "MT-03", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Mixing", 0m, "Active", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_processing_runs_StoringTankId",
                table: "processing_runs",
                column: "StoringTankId");

            migrationBuilder.CreateIndex(
                name: "ux_processing_runs_batchcode",
                table: "processing_runs",
                column: "BatchCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_processing_runs_dispatch",
                table: "processing_runs",
                column: "DispatchNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_stages_MixingTankId",
                table: "processing_stages",
                column: "MixingTankId");

            migrationBuilder.CreateIndex(
                name: "IX_processing_stages_ProcessingRunId",
                table: "processing_stages",
                column: "ProcessingRunId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_panels_ProcessingRunId",
                table: "quality_panels",
                column: "ProcessingRunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tank_allocations_DestinationMixingTankId",
                table: "tank_allocations",
                column: "DestinationMixingTankId");

            migrationBuilder.CreateIndex(
                name: "IX_tank_allocations_ProcessingRunId",
                table: "tank_allocations",
                column: "ProcessingRunId");

            migrationBuilder.CreateIndex(
                name: "IX_tank_allocations_SourceStoringTankId",
                table: "tank_allocations",
                column: "SourceStoringTankId");

            migrationBuilder.CreateIndex(
                name: "ux_tank_allocations_batchcode",
                table: "tank_allocations",
                column: "BatchCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tank_allocations_day_product_letter",
                table: "tank_allocations",
                columns: new[] { "BatchNumber", "ProductType", "BatchLetter" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tanks_code",
                table: "tanks",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processing_stages");

            migrationBuilder.DropTable(
                name: "quality_panels");

            migrationBuilder.DropTable(
                name: "tank_allocations");

            migrationBuilder.DropTable(
                name: "processing_runs");

            migrationBuilder.DropTable(
                name: "tanks");
        }
    }
}
