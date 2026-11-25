using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcommerceBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerInvitedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Referrals"
                ADD COLUMN IF NOT EXISTS "InvitedByPartnerId" uuid NULL
                REFERENCES "Referrals"("Id") ON DELETE SET NULL;
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_Referrals_InvitedByPartnerId"
                ON "Referrals" ("InvitedByPartnerId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Referrals_InvitedByPartnerId";""");
            migrationBuilder.Sql("""ALTER TABLE "Referrals" DROP COLUMN IF EXISTS "InvitedByPartnerId";""");
        }
    }
}
