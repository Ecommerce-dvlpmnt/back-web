using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcommerceBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReferralRewardPaid",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ReferralRewardAmount",
                table: "Orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferrerId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Partners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReferralCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 5.0m),
                    TotalEarned = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partners", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ReferrerId",
                table: "Orders",
                column: "ReferrerId");

            migrationBuilder.CreateIndex(
                name: "Partners_ReferralCode_key",
                table: "Partners",
                column: "ReferralCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "Partners_TelegramUserId_key",
                table: "Partners",
                column: "TelegramUserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "Orders_ReferrerId_fkey",
                table: "Orders",
                column: "ReferrerId",
                principalTable: "Partners",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "Orders_ReferrerId_fkey",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "Partners");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ReferrerId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsReferralRewardPaid",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReferralRewardAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReferrerId",
                table: "Orders");
        }
    }
}
