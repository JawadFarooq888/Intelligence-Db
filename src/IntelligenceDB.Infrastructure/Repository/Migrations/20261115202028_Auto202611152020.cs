using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IntelligenceDB.Infrastructure.Repository.Migrations
{
    /// <inheritdoc />
    public partial class Auto202611152020 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "mon");

            migrationBuilder.EnsureSchema(
                name: "hc");

            migrationBuilder.AddColumn<Guid>(
                name: "AgentId",
                schema: "inv",
                table: "Servers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Agents",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MachineName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LastIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Backups",
                schema: "mon",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceBackupSetId = table.Column<long>(type: "bigint", nullable: false),
                    BackupType = table.Column<string>(type: "nchar(1)", fixedLength: true, maxLength: 1, nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    FinishUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CompressedBytes = table.Column<long>(type: "bigint", nullable: true),
                    IsCopyOnly = table.Column<bool>(type: "bit", nullable: false),
                    RecoveryModel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    DeviceType = table.Column<int>(type: "int", nullable: true),
                    PhysicalDevice = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Backups", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_Backups_Servers_ServerId",
                        column: x => x.ServerId,
                        principalSchema: "inv",
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectionRuns",
                schema: "mon",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    Collector = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRuns", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_CollectionRuns_Servers_ServerId",
                        column: x => x.ServerId,
                        principalSchema: "inv",
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Databases",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceDatabaseId = table.Column<int>(type: "int", nullable: false),
                    CreateDateUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    State = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RecoveryModel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CompatibilityLevel = table.Column<int>(type: "int", nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IsReadOnly = table.Column<bool>(type: "bit", nullable: false),
                    IsAutoCloseOn = table.Column<bool>(type: "bit", nullable: false),
                    IsAutoShrinkOn = table.Column<bool>(type: "bit", nullable: false),
                    LogReuseWait = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    VlfCount = table.Column<int>(type: "int", nullable: true),
                    LastGoodCheckDbUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LastFullBackupUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LastDiffBackupUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LastLogBackupUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    DataSizeMb = table.Column<double>(type: "float", nullable: true),
                    DataUsedMb = table.Column<double>(type: "float", nullable: true),
                    LogSizeMb = table.Column<double>(type: "float", nullable: true),
                    LogUsedMb = table.Column<double>(type: "float", nullable: true),
                    FirstSeenUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    IsDropped = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Databases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Databases_Servers_ServerId",
                        column: x => x.ServerId,
                        principalSchema: "inv",
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FileIoStats",
                schema: "mon",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    CollectedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    IntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FileId = table.Column<int>(type: "int", nullable: false),
                    Reads = table.Column<long>(type: "bigint", nullable: false),
                    Writes = table.Column<long>(type: "bigint", nullable: false),
                    BytesRead = table.Column<long>(type: "bigint", nullable: false),
                    BytesWritten = table.Column<long>(type: "bigint", nullable: false),
                    ReadStallMs = table.Column<long>(type: "bigint", nullable: false),
                    WriteStallMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileIoStats", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_FileIoStats_Servers_ServerId",
                        column: x => x.ServerId,
                        principalSchema: "inv",
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerConfig",
                schema: "inv",
                columns: table => new
                {
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    ValueInUse = table.Column<long>(type: "bigint", nullable: false),
                    Minimum = table.Column<long>(type: "bigint", nullable: false),
                    Maximum = table.Column<long>(type: "bigint", nullable: false),
                    IsDynamic = table.Column<bool>(type: "bit", nullable: false),
                    IsAdvanced = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CollectedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerConfig", x => new { x.ServerId, x.Name });
                    table.ForeignKey(
                        name: "FK_ServerConfig_Servers_ServerId",
                        column: x => x.ServerId,
                        principalSchema: "inv",
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerHealth",
                schema: "hc",
                columns: table => new
                {
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    BreakdownJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CalculatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerHealth", x => x.ServerId);
                    table.ForeignKey(
                        name: "FK_ServerHealth_Servers_ServerId",
                        column: x => x.ServerId,
                        principalSchema: "inv",
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerMetrics",
                schema: "mon",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    CollectedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    SqlCpuPct = table.Column<double>(type: "float", nullable: true),
                    OtherCpuPct = table.Column<double>(type: "float", nullable: true),
                    PageLifeExpectancy = table.Column<long>(type: "bigint", nullable: true),
                    MemoryGrantsPending = table.Column<int>(type: "int", nullable: true),
                    TotalServerMemoryMb = table.Column<long>(type: "bigint", nullable: true),
                    TargetServerMemoryMb = table.Column<long>(type: "bigint", nullable: true),
                    BatchRequestsPerSec = table.Column<double>(type: "float", nullable: true),
                    UserConnections = table.Column<int>(type: "int", nullable: true),
                    BlockedSessions = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerMetrics", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_ServerMetrics_Servers_ServerId",
                        column: x => x.ServerId,
                        principalSchema: "inv",
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Server performance samples");

            migrationBuilder.CreateTable(
                name: "VolumeStats",
                schema: "mon",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    CollectedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    MountPoint = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TotalMb = table.Column<long>(type: "bigint", nullable: false),
                    FreeMb = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VolumeStats", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_VolumeStats_Servers_ServerId",
                        column: x => x.ServerId,
                        principalSchema: "inv",
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WaitStats",
                schema: "mon",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    CollectedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    IntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    WaitType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    WaitTimeMs = table.Column<long>(type: "bigint", nullable: false),
                    SignalWaitMs = table.Column<long>(type: "bigint", nullable: false),
                    WaitingTasks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitStats", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_WaitStats_Servers_ServerId",
                        column: x => x.ServerId,
                        principalSchema: "inv",
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentCommands",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProtectedPayload = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    DeliveredUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentCommands_Agents_AgentId",
                        column: x => x.AgentId,
                        principalSchema: "inv",
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseFiles",
                schema: "inv",
                columns: table => new
                {
                    DatabaseId = table.Column<int>(type: "int", nullable: false),
                    FileId = table.Column<int>(type: "int", nullable: false),
                    LogicalName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PhysicalName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    TypeDesc = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SizeMb = table.Column<double>(type: "float", nullable: false),
                    UsedMb = table.Column<double>(type: "float", nullable: true),
                    MaxSizeMb = table.Column<double>(type: "float", nullable: true),
                    IsPercentGrowth = table.Column<bool>(type: "bit", nullable: false),
                    GrowthValue = table.Column<double>(type: "float", nullable: false),
                    VolumeMountPoint = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseFiles", x => new { x.DatabaseId, x.FileId });
                    table.ForeignKey(
                        name: "FK_DatabaseFiles_Databases_DatabaseId",
                        column: x => x.DatabaseId,
                        principalSchema: "inv",
                        principalTable: "Databases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseSizes",
                schema: "mon",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DatabaseId = table.Column<int>(type: "int", nullable: false),
                    CollectedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    DataSizeMb = table.Column<double>(type: "float", nullable: false),
                    DataUsedMb = table.Column<double>(type: "float", nullable: true),
                    LogSizeMb = table.Column<double>(type: "float", nullable: false),
                    LogUsedMb = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseSizes", x => x.Id)
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_DatabaseSizes_Databases_DatabaseId",
                        column: x => x.DatabaseId,
                        principalSchema: "inv",
                        principalTable: "Databases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Servers_AgentId",
                schema: "inv",
                table: "Servers",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentCommands_AgentId_Status",
                schema: "ops",
                table: "AgentCommands",
                columns: new[] { "AgentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_Name",
                schema: "inv",
                table: "Agents",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Backups_ServerId_DatabaseName_FinishUtc",
                schema: "mon",
                table: "Backups",
                columns: new[] { "ServerId", "DatabaseName", "FinishUtc" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_Backups_ServerId_SourceBackupSetId",
                schema: "mon",
                table: "Backups",
                columns: new[] { "ServerId", "SourceBackupSetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRuns_ServerId_StartedUtc",
                schema: "mon",
                table: "CollectionRuns",
                columns: new[] { "ServerId", "StartedUtc" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_Databases_ServerId_Name",
                schema: "inv",
                table: "Databases",
                columns: new[] { "ServerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatabaseSizes_DatabaseId_CollectedUtc",
                schema: "mon",
                table: "DatabaseSizes",
                columns: new[] { "DatabaseId", "CollectedUtc" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_FileIoStats_ServerId_CollectedUtc",
                schema: "mon",
                table: "FileIoStats",
                columns: new[] { "ServerId", "CollectedUtc" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_ServerMetrics_ServerId_CollectedUtc",
                schema: "mon",
                table: "ServerMetrics",
                columns: new[] { "ServerId", "CollectedUtc" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_VolumeStats_ServerId_CollectedUtc",
                schema: "mon",
                table: "VolumeStats",
                columns: new[] { "ServerId", "CollectedUtc" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "IX_WaitStats_ServerId_CollectedUtc",
                schema: "mon",
                table: "WaitStats",
                columns: new[] { "ServerId", "CollectedUtc" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.AddForeignKey(
                name: "FK_Servers_Agents_AgentId",
                schema: "inv",
                table: "Servers",
                column: "AgentId",
                principalSchema: "inv",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Servers_Agents_AgentId",
                schema: "inv",
                table: "Servers");

            migrationBuilder.DropTable(
                name: "AgentCommands",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "Backups",
                schema: "mon");

            migrationBuilder.DropTable(
                name: "CollectionRuns",
                schema: "mon");

            migrationBuilder.DropTable(
                name: "DatabaseFiles",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "DatabaseSizes",
                schema: "mon");

            migrationBuilder.DropTable(
                name: "FileIoStats",
                schema: "mon");

            migrationBuilder.DropTable(
                name: "ServerConfig",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "ServerHealth",
                schema: "hc");

            migrationBuilder.DropTable(
                name: "ServerMetrics",
                schema: "mon");

            migrationBuilder.DropTable(
                name: "VolumeStats",
                schema: "mon");

            migrationBuilder.DropTable(
                name: "WaitStats",
                schema: "mon");

            migrationBuilder.DropTable(
                name: "Agents",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "Databases",
                schema: "inv");

            migrationBuilder.DropIndex(
                name: "IX_Servers_AgentId",
                schema: "inv",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "AgentId",
                schema: "inv",
                table: "Servers");
        }
    }
}
