using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProcessingService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use IF NOT EXISTS to avoid "Table already exists" when tables were created manually or via previous incomplete migrations
            // mcc_dispatch_traces - cached copy from mccdb for true isolate
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS mcc_dispatch_traces (
                    Id char(36) COLLATE ascii_general_ci NOT NULL,
                    Reference varchar(50) CHARACTER SET utf8mb4 NOT NULL,
                    BowserRegistration varchar(50) CHARACTER SET utf8mb4 NOT NULL,
                    DispatchDate datetime(6) NULL,
                    TotalQuantityLitres decimal(10,2) NOT NULL,
                    DispatchedBy varchar(100) CHARACTER SET utf8mb4 NOT NULL,
                    RecordedAtUtc datetime(6) NOT NULL,
                    CreatedAtUtc datetime(6) NOT NULL,
                    LastSyncedAtUtc datetime(6) NOT NULL,
                    TotalUnloadedKg decimal(10,2) NOT NULL,
                    CONSTRAINT PK_mcc_dispatch_traces PRIMARY KEY (Id),
                    UNIQUE INDEX ux_mcc_dispatch_traces_reference (Reference)
                ) CHARACTER SET=utf8mb4;
            ");

            // processing_run_storing_allocations - FK names shortened to <64 chars (MySQL limit 64)
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS processing_run_storing_allocations (
                    Id char(36) COLLATE ascii_general_ci NOT NULL,
                    ProcessingRunId char(36) COLLATE ascii_general_ci NOT NULL,
                    StoringTankId char(36) COLLATE ascii_general_ci NOT NULL,
                    QuantityKg decimal(10,2) NOT NULL,
                    CreatedAtUtc datetime(6) NOT NULL,
                    CreatedBy varchar(100) CHARACTER SET utf8mb4 NOT NULL,
                    CONSTRAINT PK_processing_run_storing_allocations PRIMARY KEY (Id),
                    CONSTRAINT FK_storing_alloc_run FOREIGN KEY (ProcessingRunId) REFERENCES processing_runs (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_storing_alloc_tank FOREIGN KEY (StoringTankId) REFERENCES tanks (Id) ON DELETE RESTRICT,
                    INDEX ix_storing_alloc_run_tank (ProcessingRunId, StoringTankId)
                ) CHARACTER SET=utf8mb4;
            ");

            // tank_temperature_logs - FK shortened
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS tank_temperature_logs (
                    Id char(36) COLLATE ascii_general_ci NOT NULL,
                    TankId char(36) COLLATE ascii_general_ci NOT NULL,
                    TemperatureC decimal(10,2) NOT NULL,
                    Note varchar(500) CHARACTER SET utf8mb4 NULL,
                    RecordedAtUtc datetime(6) NOT NULL,
                    CreatedAtUtc datetime(6) NOT NULL,
                    RecordedBy varchar(100) CHARACTER SET utf8mb4 NOT NULL,
                    CONSTRAINT PK_tank_temperature_logs PRIMARY KEY (Id),
                    CONSTRAINT FK_temp_logs_tank FOREIGN KEY (TankId) REFERENCES tanks (Id) ON DELETE CASCADE,
                    INDEX ix_temp_logs_tank_time (TankId, RecordedAtUtc)
                ) CHARACTER SET=utf8mb4;
            ");

            // outbox_messages - SCRUM-68 outbox pattern for publish-after-commit
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS outbox_messages (
                    Id char(36) COLLATE ascii_general_ci NOT NULL,
                    Topic varchar(200) CHARACTER SET utf8mb4 NOT NULL,
                    `Key` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
                    EventType varchar(100) CHARACTER SET utf8mb4 NOT NULL,
                    Payload longtext CHARACTER SET utf8mb4 NOT NULL,
                    HeadersJson text CHARACTER SET utf8mb4 NOT NULL,
                    CreatedAtUtc datetime(6) NOT NULL,
                    ProcessedAtUtc datetime(6) NULL,
                    RetryCount int NOT NULL,
                    LastError varchar(1000) CHARACTER SET utf8mb4 NULL,
                    Status varchar(20) CHARACTER SET utf8mb4 NOT NULL,
                    CorrelationId varchar(100) CHARACTER SET utf8mb4 NOT NULL,
                    CONSTRAINT PK_outbox_messages PRIMARY KEY (Id),
                    INDEX ix_outbox_status (Status),
                    INDEX ix_outbox_created (CreatedAtUtc),
                    INDEX ix_outbox_status_created (Status, CreatedAtUtc)
                ) CHARACTER SET=utf8mb4;
            ");

            // QualityTestStatus column - MySQL does NOT support ADD COLUMN IF NOT EXISTS (MariaDB only)
            // Use INFORMATION_SCHEMA check for idempotency
            migrationBuilder.Sql(@"
                SET @dbname = DATABASE();
                SET @tablename = 'processing_runs';
                SET @columnname = 'QualityTestStatus';
                SET @preparedStatement = (SELECT IF(
                  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @dbname AND TABLE_NAME = @tablename AND COLUMN_NAME = @columnname) > 0,
                  'SELECT 1',
                  CONCAT('ALTER TABLE ', @tablename, ' ADD COLUMN ', @columnname, ' varchar(20) NOT NULL DEFAULT ''Pending''')
                ));
                PREPARE alterIfNotExists FROM @preparedStatement;
                EXECUTE alterIfNotExists;
                DEALLOCATE PREPARE alterIfNotExists;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS outbox_messages;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS tank_temperature_logs;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS processing_run_storing_allocations;");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS mcc_dispatch_traces;");
        }
    }
}
