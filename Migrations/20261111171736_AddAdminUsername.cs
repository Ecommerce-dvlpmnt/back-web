using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcommerceBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "Admins_Email_key",
                table: "Admins");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "Admins",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Admin",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "Manager");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Admins",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "Admins",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE \"Admins\" SET \"Username\" = 'admin' WHERE \"Username\" = '';");

            migrationBuilder.CreateIndex(
                name: "Admins_Username_key",
                table: "Admins",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "Admins_Username_key",
                table: "Admins");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "Admins");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "Admins",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Manager",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "Admin");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Admins",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "Admins_Email_key",
                table: "Admins",
                column: "Email",
                unique: true);
        }
    }
}
