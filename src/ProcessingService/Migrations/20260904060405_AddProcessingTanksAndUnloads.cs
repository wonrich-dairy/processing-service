using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcessingService.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingTanksAndUnloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "processing_tanks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CapacityLitres = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_tanks", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "unloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Reference = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    DispatchNoteReference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    StoringTankId = table.Column<Guid>(type: "char(36)", nullable: false),
                    QuantityLitres = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    TemperatureCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    UnloadedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    UnloadedAtLocal = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UnloadDate = table.Column<DateTime>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unloads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_unloads_processing_tanks_StoringTankId",
                        column: x => x.StoringTankId,
                        principalTable: "processing_tanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ux_processing_tanks_code",
                table: "processing_tanks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unloads_date",
                table: "unloads",
                column: "UnloadDate");

            migrationBuilder.CreateIndex(
                name: "IX_unloads_StoringTankId",
                table: "unloads",
                column: "StoringTankId");

            migrationBuilder.CreateIndex(
                name: "ux_unloads_dispatch_note",
                table: "unloads",
                column: "DispatchNoteReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_unloads_reference",
                table: "unloads",
                column: "Reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "unloads");

            migrationBuilder.DropTable(
                name: "processing_tanks");
        }
    }
}
