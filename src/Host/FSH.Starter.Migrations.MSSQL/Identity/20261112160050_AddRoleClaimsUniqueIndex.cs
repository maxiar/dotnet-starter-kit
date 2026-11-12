using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSH.Starter.Migrations.MSSQL.Identity
{
    /// <inheritdoc />
    public partial class AddRoleClaimsUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove the duplicate grants concurrent permission writers left behind, keeping the lowest Id
            // per (RoleId, ClaimType, ClaimValue). Must run before the unique index below is created.
            // Plain equality matches the index, which filters out rows with a NULL ClaimType or ClaimValue.
            migrationBuilder.Sql(
                """
                DELETE dup
                FROM [identity].[RoleClaims] AS dup
                INNER JOIN [identity].[RoleClaims] AS kept
                    ON dup.[RoleId] = kept.[RoleId]
                   AND dup.[ClaimType] = kept.[ClaimType]
                   AND dup.[ClaimValue] = kept.[ClaimValue]
                   AND dup.[Id] > kept.[Id];
                """);

            migrationBuilder.DropIndex(
                name: "IX_RoleClaims_RoleId",
                schema: "identity",
                table: "RoleClaims");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimValue",
                schema: "identity",
                table: "RoleClaims",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClaimType",
                schema: "identity",
                table: "RoleClaims",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleClaims_RoleId_ClaimType_ClaimValue",
                schema: "identity",
                table: "RoleClaims",
                columns: new[] { "RoleId", "ClaimType", "ClaimValue" },
                unique: true,
                filter: "[ClaimType] IS NOT NULL AND [ClaimValue] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoleClaims_RoleId_ClaimType_ClaimValue",
                schema: "identity",
                table: "RoleClaims");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimValue",
                schema: "identity",
                table: "RoleClaims",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClaimType",
                schema: "identity",
                table: "RoleClaims",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleClaims_RoleId",
                schema: "identity",
                table: "RoleClaims",
                column: "RoleId");
        }
    }
}
