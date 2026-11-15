using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IntelligenceDB.Infrastructure.Repository.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "log");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "inv");

            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.EnsureSchema(
                name: "auth");

            migrationBuilder.CreateTable(
                name: "AppLog",
                schema: "log",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MessageTemplate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Level = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TimeStamp = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Exception = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ClientIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: true),
                    ServerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Tsql = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Credentials",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    AuthType = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ProtectedSecret = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ClientId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RotatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Credentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServerGroups",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    LastLoginUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    AllServerGroups = table.Column<bool>(type: "bit", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoleClaims",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleClaims_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "auth",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Servers",
                schema: "inv",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Host = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    Port = table.Column<int>(type: "int", nullable: true),
                    InstanceName = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DeclaredPlatform = table.Column<int>(type: "int", nullable: false),
                    DetectedPlatform = table.Column<int>(type: "int", nullable: false),
                    Environment = table.Column<int>(type: "int", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: true),
                    MonitoringCredentialId = table.Column<int>(type: "int", nullable: false),
                    AdminCredentialId = table.Column<int>(type: "int", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Encrypt = table.Column<bool>(type: "bit", nullable: false),
                    TrustServerCertificate = table.Column<bool>(type: "bit", nullable: false),
                    ConnectTimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    CollectionIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    LastCheckedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LastStatus = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Servers_Credentials_AdminCredentialId",
                        column: x => x.AdminCredentialId,
                        principalSchema: "inv",
                        principalTable: "Credentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Servers_Credentials_MonitoringCredentialId",
                        column: x => x.MonitoringCredentialId,
                        principalSchema: "inv",
                        principalTable: "Credentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Servers_ServerGroups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "inv",
                        principalTable: "ServerGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ReplacedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserClaims",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserClaims_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLogins",
                schema: "auth",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UserLogins_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                schema: "auth",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "auth",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserServerGroups",
                schema: "auth",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServerGroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserServerGroups", x => new { x.UserId, x.ServerGroupId });
                    table.ForeignKey(
                        name: "FK_UserServerGroups_ServerGroups_ServerGroupId",
                        column: x => x.ServerGroupId,
                        principalSchema: "inv",
                        principalTable: "ServerGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserServerGroups_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                schema: "auth",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerCapabilities",
                schema: "inv",
                columns: table => new
                {
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    CapabilityKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsSupported = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Alternative = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DetectedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerCapabilities", x => new { x.ServerId, x.CapabilityKey });
                    table.ForeignKey(
                        name: "FK_ServerCapabilities_Servers_ServerId",
                        column: x => x.ServerId,
                        principalSchema: "inv",
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerInfo",
                schema: "inv",
                columns: table => new
                {
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    ProductVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MajorVersion = table.Column<int>(type: "int", nullable: false),
                    ProductLevel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ProductUpdateLevel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Edition = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EngineEdition = table.Column<int>(type: "int", nullable: false),
                    HostPlatform = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CpuCount = table.Column<int>(type: "int", nullable: true),
                    PhysicalMemoryMb = table.Column<long>(type: "bigint", nullable: true),
                    SqlStartTimeUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Collation = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ServerName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsSysadmin = table.Column<bool>(type: "bit", nullable: false),
                    PermissionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DetectedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerInfo", x => x.ServerId);
                    table.ForeignKey(
                        name: "FK_ServerInfo_Servers_ServerId",
                        column: x => x.ServerId,
                        principalSchema: "inv",
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerTags",
                schema: "inv",
                columns: table => new
                {
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerTags", x => new { x.ServerId, x.Tag });
                    table.ForeignKey(
                        name: "FK_ServerTags_Servers_ServerId",
                        column: x => x.ServerId,
                        principalSchema: "inv",
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppLog_TimeStamp",
                schema: "log",
                table: "AppLog",
                column: "TimeStamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_OccurredUtc",
                schema: "audit",
                table: "AuditLog",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_ServerId_OccurredUtc",
                schema: "audit",
                table: "AuditLog",
                columns: new[] { "ServerId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                schema: "auth",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                schema: "auth",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleClaims_RoleId",
                schema: "auth",
                table: "RoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "auth",
                table: "Roles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServerGroups_Name",
                schema: "inv",
                table: "ServerGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Servers_AdminCredentialId",
                schema: "inv",
                table: "Servers",
                column: "AdminCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_DisplayName",
                schema: "inv",
                table: "Servers",
                column: "DisplayName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Servers_GroupId",
                schema: "inv",
                table: "Servers",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_MonitoringCredentialId",
                schema: "inv",
                table: "Servers",
                column: "MonitoringCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerTags_Tag",
                schema: "inv",
                table: "ServerTags",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_UserClaims_UserId",
                schema: "auth",
                table: "UserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLogins_UserId",
                schema: "auth",
                table: "UserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                schema: "auth",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "auth",
                table: "Users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "auth",
                table: "Users",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserServerGroups_ServerGroupId",
                schema: "auth",
                table: "UserServerGroups",
                column: "ServerGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppLog",
                schema: "log");

            migrationBuilder.DropTable(
                name: "AuditLog",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "RefreshTokens",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "RoleClaims",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "ServerCapabilities",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "ServerInfo",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "ServerTags",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "UserClaims",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "UserLogins",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "UserRoles",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "UserServerGroups",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "UserTokens",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "Servers",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "Credentials",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "ServerGroups",
                schema: "inv");
        }
    }
}
