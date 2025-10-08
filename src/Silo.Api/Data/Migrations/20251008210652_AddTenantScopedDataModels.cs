using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Silo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantScopedDataModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("1cfa0080-7678-4d5b-98af-3bd905bc66de"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("27acd356-32b7-478f-a654-9c2fc7147fad"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("38545651-61a9-4477-ae95-11625413f48f"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("5d53865a-bd11-49f4-badf-b50da6607bfd"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("ac20f746-ad58-46f1-8f72-539f827bf4d5"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("c37f4066-3722-407c-8ced-2b5bff7d64c4"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("d1633467-da0e-4dc4-8337-60dcdbb63c02"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("f00a2128-53b3-4e27-95b9-a0ba7c0608c1"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("fe587224-e86f-478e-8749-79312d2f30f2"));

            migrationBuilder.CreateTable(
                name: "BackupJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    SourcePath = table.Column<string>(type: "text", nullable: false),
                    DestinationPath = table.Column<string>(type: "text", nullable: false),
                    Schedule = table.Column<string>(type: "text", nullable: true),
                    LastRun = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextRun = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    ProcessedBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalFiles = table.Column<int>(type: "integer", nullable: false),
                    ProcessedFiles = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RetentionDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileMetadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    OriginalPath = table.Column<string>(type: "text", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "text", nullable: false),
                    Checksum = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ThumbnailPath = table.Column<string>(type: "text", nullable: true),
                    ExtractedText = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileMetadata", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    Checksum = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsCurrentVersion = table.Column<bool>(type: "boolean", nullable: false),
                    VersionType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileDiffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiffType = table.Column<string>(type: "text", nullable: false),
                    DiffContent = table.Column<string>(type: "text", nullable: true),
                    DiffSize = table.Column<long>(type: "bigint", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileDiffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileDiffs_FileVersions_SourceVersionId",
                        column: x => x.SourceVersionId,
                        principalTable: "FileVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileDiffs_FileVersions_TargetVersionId",
                        column: x => x.TargetVersionId,
                        principalTable: "FileVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Action", "CreatedAt", "Description", "Name", "Resource" },
                values: new object[,]
                {
                    { new Guid("246cb0c3-1406-4462-88fd-8fd87056f06a"), "delete", new DateTime(2025, 10, 8, 21, 6, 52, 61, DateTimeKind.Utc).AddTicks(2104), "Delete files", "files:delete", "files" },
                    { new Guid("3af08dfa-0b6d-494b-80a4-e7bf140bd956"), "read", new DateTime(2025, 10, 8, 21, 6, 52, 61, DateTimeKind.Utc).AddTicks(2074), "Read files", "files:read", "files" },
                    { new Guid("518a6b41-9754-4840-9ba8-c713fbf47f81"), "manage", new DateTime(2025, 10, 8, 21, 6, 52, 61, DateTimeKind.Utc).AddTicks(2176), "Manage users", "users:manage", "users" },
                    { new Guid("60ca51b2-14e7-4696-923e-c2033a9ae3ae"), "download", new DateTime(2025, 10, 8, 21, 6, 52, 61, DateTimeKind.Utc).AddTicks(2133), "Download files", "files:download", "files" },
                    { new Guid("6bde03c9-8d22-489d-b00e-bca488e50259"), "read", new DateTime(2025, 10, 8, 21, 6, 52, 61, DateTimeKind.Utc).AddTicks(2147), "Read users", "users:read", "users" },
                    { new Guid("9923c2e2-fdef-4674-a56a-1a03ce406ea4"), "upload", new DateTime(2025, 10, 8, 21, 6, 52, 61, DateTimeKind.Utc).AddTicks(2118), "Upload files", "files:upload", "files" },
                    { new Guid("9998a8e9-f567-449f-955d-29283d697c48"), "write", new DateTime(2025, 10, 8, 21, 6, 52, 61, DateTimeKind.Utc).AddTicks(2161), "Write users", "users:write", "users" },
                    { new Guid("abc4f7bf-e9b8-498b-8ba5-7f59d97ca266"), "admin", new DateTime(2025, 10, 8, 21, 6, 52, 61, DateTimeKind.Utc).AddTicks(2190), "System administration", "system:admin", "system" },
                    { new Guid("bca2aa38-6f05-45ef-9f0b-fec0f199fb28"), "write", new DateTime(2025, 10, 8, 21, 6, 52, 61, DateTimeKind.Utc).AddTicks(2090), "Write files", "files:write", "files" }
                });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000011"),
                column: "CreatedAt",
                value: new DateTime(2025, 10, 8, 21, 6, 52, 61, DateTimeKind.Utc).AddTicks(2025));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000012"),
                column: "CreatedAt",
                value: new DateTime(2025, 10, 8, 21, 6, 52, 61, DateTimeKind.Utc).AddTicks(2034));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000013"),
                column: "CreatedAt",
                value: new DateTime(2025, 10, 8, 21, 6, 52, 61, DateTimeKind.Utc).AddTicks(2043));

            migrationBuilder.UpdateData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2025, 10, 8, 21, 6, 52, 61, DateTimeKind.Utc).AddTicks(1848));

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_TenantId",
                table: "BackupJobs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_TenantId_Status",
                table: "BackupJobs",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FileDiffs_SourceVersionId_TargetVersionId",
                table: "FileDiffs",
                columns: new[] { "SourceVersionId", "TargetVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_FileDiffs_TargetVersionId",
                table: "FileDiffs",
                column: "TargetVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_FileMetadata_TenantId",
                table: "FileMetadata",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FileMetadata_TenantId_Checksum",
                table: "FileMetadata",
                columns: new[] { "TenantId", "Checksum" });

            migrationBuilder.CreateIndex(
                name: "IX_FileMetadata_TenantId_FileName",
                table: "FileMetadata",
                columns: new[] { "TenantId", "FileName" });

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_TenantId",
                table: "FileVersions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_TenantId_FilePath",
                table: "FileVersions",
                columns: new[] { "TenantId", "FilePath" });

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_TenantId_FilePath_VersionNumber",
                table: "FileVersions",
                columns: new[] { "TenantId", "FilePath", "VersionNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupJobs");

            migrationBuilder.DropTable(
                name: "FileDiffs");

            migrationBuilder.DropTable(
                name: "FileMetadata");

            migrationBuilder.DropTable(
                name: "FileVersions");

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("246cb0c3-1406-4462-88fd-8fd87056f06a"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("3af08dfa-0b6d-494b-80a4-e7bf140bd956"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("518a6b41-9754-4840-9ba8-c713fbf47f81"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("60ca51b2-14e7-4696-923e-c2033a9ae3ae"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("6bde03c9-8d22-489d-b00e-bca488e50259"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("9923c2e2-fdef-4674-a56a-1a03ce406ea4"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("9998a8e9-f567-449f-955d-29283d697c48"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("abc4f7bf-e9b8-498b-8ba5-7f59d97ca266"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("bca2aa38-6f05-45ef-9f0b-fec0f199fb28"));

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Action", "CreatedAt", "Description", "Name", "Resource" },
                values: new object[,]
                {
                    { new Guid("1cfa0080-7678-4d5b-98af-3bd905bc66de"), "write", new DateTime(2025, 10, 8, 12, 12, 25, 788, DateTimeKind.Utc).AddTicks(8890), "Write users", "users:write", "users" },
                    { new Guid("27acd356-32b7-478f-a654-9c2fc7147fad"), "upload", new DateTime(2025, 10, 8, 12, 12, 25, 788, DateTimeKind.Utc).AddTicks(8836), "Upload files", "files:upload", "files" },
                    { new Guid("38545651-61a9-4477-ae95-11625413f48f"), "write", new DateTime(2025, 10, 8, 12, 12, 25, 788, DateTimeKind.Utc).AddTicks(8800), "Write files", "files:write", "files" },
                    { new Guid("5d53865a-bd11-49f4-badf-b50da6607bfd"), "manage", new DateTime(2025, 10, 8, 12, 12, 25, 788, DateTimeKind.Utc).AddTicks(8909), "Manage users", "users:manage", "users" },
                    { new Guid("ac20f746-ad58-46f1-8f72-539f827bf4d5"), "delete", new DateTime(2025, 10, 8, 12, 12, 25, 788, DateTimeKind.Utc).AddTicks(8818), "Delete files", "files:delete", "files" },
                    { new Guid("c37f4066-3722-407c-8ced-2b5bff7d64c4"), "admin", new DateTime(2025, 10, 8, 12, 12, 25, 788, DateTimeKind.Utc).AddTicks(8926), "System administration", "system:admin", "system" },
                    { new Guid("d1633467-da0e-4dc4-8337-60dcdbb63c02"), "read", new DateTime(2025, 10, 8, 12, 12, 25, 788, DateTimeKind.Utc).AddTicks(8871), "Read users", "users:read", "users" },
                    { new Guid("f00a2128-53b3-4e27-95b9-a0ba7c0608c1"), "download", new DateTime(2025, 10, 8, 12, 12, 25, 788, DateTimeKind.Utc).AddTicks(8855), "Download files", "files:download", "files" },
                    { new Guid("fe587224-e86f-478e-8749-79312d2f30f2"), "read", new DateTime(2025, 10, 8, 12, 12, 25, 788, DateTimeKind.Utc).AddTicks(8780), "Read files", "files:read", "files" }
                });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000011"),
                column: "CreatedAt",
                value: new DateTime(2025, 10, 8, 12, 12, 25, 788, DateTimeKind.Utc).AddTicks(8722));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000012"),
                column: "CreatedAt",
                value: new DateTime(2025, 10, 8, 12, 12, 25, 788, DateTimeKind.Utc).AddTicks(8735));

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000013"),
                column: "CreatedAt",
                value: new DateTime(2025, 10, 8, 12, 12, 25, 788, DateTimeKind.Utc).AddTicks(8746));

            migrationBuilder.UpdateData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2025, 10, 8, 12, 12, 25, 788, DateTimeKind.Utc).AddTicks(8476));
        }
    }
}
