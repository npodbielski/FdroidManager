using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FdroidMgr.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterKeySalt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MasterKeySalt",
                table: "RepoConfig",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MasterKeySalt",
                table: "RepoConfig");
        }
    }
}
