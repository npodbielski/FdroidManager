using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FdroidMgr.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DownloadCount",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DayUtc = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    PackageName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    VersionCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadCount", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Keys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PackageName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Alias = table.Column<string>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", nullable: true),
                    StorePath = table.Column<string>(type: "TEXT", nullable: false),
                    FingerprintSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordEncrypted = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RepoConfig",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepoUrl = table.Column<string>(type: "TEXT", nullable: false),
                    RepoName = table.Column<string>(type: "TEXT", nullable: false),
                    RepoDescription = table.Column<string>(type: "TEXT", nullable: false),
                    RepoPath = table.Column<string>(type: "TEXT", nullable: false),
                    KeyPath = table.Column<string>(type: "TEXT", nullable: false),
                    OAuthIssuer = table.Column<string>(type: "TEXT", nullable: true),
                    OAuthAudience = table.Column<string>(type: "TEXT", nullable: true),
                    RepoKeystorePassEnc = table.Column<string>(type: "TEXT", nullable: true),
                    RepoKeySha256 = table.Column<string>(type: "TEXT", nullable: true),
                    BackupPath = table.Column<string>(type: "TEXT", nullable: true),
                    LastIndexBuiltUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastIndexError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepoConfig", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiKeys_ApiUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApiUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FdroidClientKey",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SecretCipher = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRevealedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RegeneratedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FdroidClientKey", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FdroidClientKey_ApiUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApiUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyHash",
                table: "ApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_UserId",
                table: "ApiKeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiUsers_Subject",
                table: "ApiUsers",
                column: "Subject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadCount_DayUtc_PackageName_VersionCode",
                table: "DownloadCount",
                columns: new[] { "DayUtc", "PackageName", "VersionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FdroidClientKey_KeyHash",
                table: "FdroidClientKey",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FdroidClientKey_UserId",
                table: "FdroidClientKey",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Keys_PackageName_Branch",
                table: "Keys",
                columns: new[] { "PackageName", "Branch" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "DownloadCount");

            migrationBuilder.DropTable(
                name: "FdroidClientKey");

            migrationBuilder.DropTable(
                name: "Keys");

            migrationBuilder.DropTable(
                name: "RepoConfig");

            migrationBuilder.DropTable(
                name: "ApiUsers");
        }
    }
}
