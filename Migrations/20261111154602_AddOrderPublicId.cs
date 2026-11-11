using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcommerceBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:order_status", "Cancelled,Confirmed,Delivered,New,Packed,Refused,Returned,Shipped")
                .Annotation("Npgsql:Enum:payment_method", "CashOnDelivery,Online")
                .Annotation("Npgsql:Enum:payment_status", "Failed,Paid,Pending,Refunded")
                .Annotation("Npgsql:Enum:shipment_status", "Cancelled,Confirmed,Pending,Problem,Requested,Shipped")
                .OldAnnotation("Npgsql:Enum:order_status", "New,Confirmed,Packed,Shipped,Delivered,Cancelled,Refused,Returned")
                .OldAnnotation("Npgsql:Enum:payment_method", "CashOnDelivery,Online")
                .OldAnnotation("Npgsql:Enum:payment_status", "Pending,Paid,Failed,Refunded")
                .OldAnnotation("Npgsql:Enum:shipment_status", "Pending,Requested,Confirmed,Shipped,Problem,Cancelled");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"Orders\" SET \"PublicId\" = replace(gen_random_uuid()::text, '-', '') WHERE \"PublicId\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "PublicId",
                table: "Orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "Orders_PublicId_key",
                table: "Orders",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "Orders_PublicId_key",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Orders");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:order_status", "New,Confirmed,Packed,Shipped,Delivered,Cancelled,Refused,Returned")
                .Annotation("Npgsql:Enum:payment_method", "CashOnDelivery,Online")
                .Annotation("Npgsql:Enum:payment_status", "Pending,Paid,Failed,Refunded")
                .Annotation("Npgsql:Enum:shipment_status", "Pending,Requested,Confirmed,Shipped,Problem,Cancelled")
                .OldAnnotation("Npgsql:Enum:order_status", "Cancelled,Confirmed,Delivered,New,Packed,Refused,Returned,Shipped")
                .OldAnnotation("Npgsql:Enum:payment_method", "CashOnDelivery,Online")
                .OldAnnotation("Npgsql:Enum:payment_status", "Failed,Paid,Pending,Refunded")
                .OldAnnotation("Npgsql:Enum:shipment_status", "Cancelled,Confirmed,Pending,Problem,Requested,Shipped");
        }
    }
}
