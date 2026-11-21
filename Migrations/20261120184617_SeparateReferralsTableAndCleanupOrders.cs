using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcommerceBackend.Migrations
{
    /// <inheritdoc />
    public partial class SeparateReferralsTableAndCleanupOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "Orders_ReferrerId_fkey",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "PartnerPayoutRequests_PartnerId_fkey",
                table: "PartnerPayoutRequests");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Partners",
                table: "Partners");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PartnerPayoutRequests",
                table: "PartnerPayoutRequests");

            migrationBuilder.DropColumn(
                name: "IsReferralRewardPaid",
                table: "Orders");

            migrationBuilder.RenameTable(
                name: "Partners",
                newName: "Referrals");

            migrationBuilder.RenameTable(
                name: "PartnerPayoutRequests",
                newName: "ReferralPayoutRequests");

            migrationBuilder.AddColumn<string>(
                name: "CardHolderName",
                table: "Referrals",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardNumber",
                table: "Referrals",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Referrals",
                table: "Referrals",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReferralPayoutRequests",
                table: "ReferralPayoutRequests",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "Orders_ReferrerId_fkey",
                table: "Orders",
                column: "ReferrerId",
                principalTable: "Referrals",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "PartnerPayoutRequests_PartnerId_fkey",
                table: "ReferralPayoutRequests",
                column: "PartnerId",
                principalTable: "Referrals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "Orders_ReferrerId_fkey",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "PartnerPayoutRequests_PartnerId_fkey",
                table: "ReferralPayoutRequests");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Referrals",
                table: "Referrals");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReferralPayoutRequests",
                table: "ReferralPayoutRequests");

            migrationBuilder.DropColumn(
                name: "CardHolderName",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "CardNumber",
                table: "Referrals");

            migrationBuilder.RenameTable(
                name: "Referrals",
                newName: "Partners");

            migrationBuilder.RenameTable(
                name: "ReferralPayoutRequests",
                newName: "PartnerPayoutRequests");

            migrationBuilder.AddColumn<bool>(
                name: "IsReferralRewardPaid",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Partners",
                table: "Partners",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PartnerPayoutRequests",
                table: "PartnerPayoutRequests",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "Orders_ReferrerId_fkey",
                table: "Orders",
                column: "ReferrerId",
                principalTable: "Partners",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "PartnerPayoutRequests_PartnerId_fkey",
                table: "PartnerPayoutRequests",
                column: "PartnerId",
                principalTable: "Partners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
