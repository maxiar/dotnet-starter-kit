using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSH.Starter.Migrations.MSSQL.Files
{
    /// <inheritdoc />
    public partial class FileAssetLegacyKeyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_FileAsset_LegacyKey",
                schema: "files",
                table: "FileAssets",
                column: "Id",
                filter: "[Visibility] = 0 AND [Status] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FileAsset_LegacyKey",
                schema: "files",
                table: "FileAssets");
        }
    }
}
