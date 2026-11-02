Build started...
Build succeeded.
warn: Microsoft.EntityFrameworkCore.Model.Validation[20601]
      The 'OrderStatus' property 'OrderStatus' on entity type 'Order' is configured with a database-generated default, but has no configured sentinel value. The database-generated default will always be used for inserts when the property has the value 'New', since this is the CLR default for the 'OrderStatus' type. Consider using a nullable type, using a nullable backing field, or setting the sentinel value for the property to ensure the database default is used when, and only when, appropriate. See https://aka.ms/efcore-docs-default-values for more information.
warn: Microsoft.EntityFrameworkCore.Model.Validation[20601]
      The 'PaymentMethod' property 'PaymentMethod' on entity type 'Order' is configured with a database-generated default, but has no configured sentinel value. The database-generated default will always be used for inserts when the property has the value 'CashOnDelivery', since this is the CLR default for the 'PaymentMethod' type. Consider using a nullable type, using a nullable backing field, or setting the sentinel value for the property to ensure the database default is used when, and only when, appropriate. See https://aka.ms/efcore-docs-default-values for more information.
warn: Microsoft.EntityFrameworkCore.Model.Validation[20601]
      The 'PaymentStatus' property 'PaymentStatus' on entity type 'Order' is configured with a database-generated default, but has no configured sentinel value. The database-generated default will always be used for inserts when the property has the value 'Pending', since this is the CLR default for the 'PaymentStatus' type. Consider using a nullable type, using a nullable backing field, or setting the sentinel value for the property to ensure the database default is used when, and only when, appropriate. See https://aka.ms/efcore-docs-default-values for more information.
warn: Microsoft.EntityFrameworkCore.Model.Validation[20601]
      The 'ShipmentStatus' property 'Status' on entity type 'OrderShipment' is configured with a database-generated default, but has no configured sentinel value. The database-generated default will always be used for inserts when the property has the value 'Pending', since this is the CLR default for the 'ShipmentStatus' type. Consider using a nullable type, using a nullable backing field, or setting the sentinel value for the property to ensure the database default is used when, and only when, appropriate. See https://aka.ms/efcore-docs-default-values for more information.
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

CREATE TYPE order_status AS ENUM ('New', 'Confirmed', 'Packed', 'Shipped', 'Delivered', 'Cancelled', 'Refused', 'Returned');
CREATE TYPE payment_method AS ENUM ('CashOnDelivery', 'Online');
CREATE TYPE payment_status AS ENUM ('Pending', 'Paid', 'Failed', 'Refunded');
CREATE TYPE shipment_status AS ENUM ('Pending', 'Requested', 'Confirmed', 'Shipped', 'Problem', 'Cancelled');

CREATE TABLE "Admins" (
    "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
    "Name" character varying(100) NOT NULL,
    "TelegramId" bigint,
    "Role" character varying(50) NOT NULL DEFAULT 'Manager',
    CONSTRAINT "PK_Admins" PRIMARY KEY ("Id")
);

CREATE TABLE "Campaigns" (
    "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
    "Name" character varying(150) NOT NULL,
    "Slug" character varying(150) NOT NULL,
    "StartsAt" timestamptz NOT NULL,
    "EndsAt" timestamptz NOT NULL,
    "BannerUrl" character varying(500),
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    CONSTRAINT "PK_Campaigns" PRIMARY KEY ("Id"),
    CONSTRAINT "Campaigns_Period_check" CHECK ("EndsAt" > "StartsAt")
);

CREATE TABLE "Categories" (
    "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
    "Name" character varying(100) NOT NULL,
    "Slug" character varying(100) NOT NULL,
    "ParentId" uuid,
    "SortOrder" integer NOT NULL DEFAULT 0,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "FilterSchema" jsonb DEFAULT ('{}'::jsonb),
    "MetaTitle" character varying(255),
    "MetaDescription" text,
    CONSTRAINT "PK_Categories" PRIMARY KEY ("Id"),
    CONSTRAINT "Categories_ParentId_fkey" FOREIGN KEY ("ParentId") REFERENCES "Categories" ("Id") ON DELETE SET NULL
);

CREATE TABLE "Suppliers" (
    "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
    "Name" character varying(100) NOT NULL,
    "Contact" character varying(150),
    "City" character varying(100),
    "Notes" text,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    CONSTRAINT "PK_Suppliers" PRIMARY KEY ("Id")
);

CREATE TABLE "Orders" (
    "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
    "OrderNumber" integer GENERATED ALWAYS AS IDENTITY,
    "CustomerFirstName" character varying(100) NOT NULL,
    "CustomerLastName" character varying(100) NOT NULL,
    "CustomerPhone" character varying(20) NOT NULL,
    "CustomerComment" text,
    "DeliveryCityRef" character varying(50),
    "DeliveryCityName" character varying(150),
    "DeliveryWarehouseRef" character varying(50),
    "DeliveryWarehouseName" character varying(250),
    "PaymentMethod" payment_method NOT NULL DEFAULT ('CashOnDelivery'),
    "PaymentProvider" character varying(20),
    "PaymentStatus" payment_status NOT NULL DEFAULT ('Pending'),
    "OrderStatus" order_status NOT NULL DEFAULT ('New'),
    "ExternalPaymentId" character varying(100),
    "TotalPrice" numeric(10,2) NOT NULL,
    "TotalProfit" numeric(10,2) NOT NULL,
    "UtmSource" character varying(100),
    "UtmMedium" character varying(100),
    "UtmCampaign" character varying(100),
    "CampaignId" uuid,
    "AdminComment" text,
    "CreatedAt" timestamptz NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    "UpdatedAt" timestamptz NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT "PK_Orders" PRIMARY KEY ("Id"),
    CONSTRAINT "Orders_CampaignId_fkey" FOREIGN KEY ("CampaignId") REFERENCES "Campaigns" ("Id") ON DELETE SET NULL
);

CREATE TABLE "Products" (
    "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
    "CategoryId" uuid NOT NULL,
    "SupplierId" uuid NOT NULL,
    "Sku" character varying(50) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Slug" character varying(200) NOT NULL,
    "Description" text,
    "DropPrice" numeric(10,2) NOT NULL DEFAULT 0.0,
    "Price" numeric(10,2) NOT NULL,
    "OldPrice" numeric(10,2),
    "Stock" integer NOT NULL DEFAULT 0,
    "StockUpdatedAt" timestamptz,
    "MainImageUrl" text,
    "Attributes" jsonb DEFAULT ('{}'::jsonb),
    "MetaTitle" character varying(255),
    "MetaDescription" text,
    "WeightKg" numeric(5,2),
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "CreatedAt" timestamptz NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    "UpdatedAt" timestamptz NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT "PK_Products" PRIMARY KEY ("Id"),
    CONSTRAINT "Products_Stock_Price_check" CHECK ("Stock" >= 0 AND "Price" >= 0),
    CONSTRAINT "Products_CategoryId_fkey" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "Products_SupplierId_fkey" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "OrderShipments" (
    "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
    "OrderId" uuid NOT NULL,
    "SupplierId" uuid NOT NULL,
    "SupplierOrderRef" character varying(100),
    "TrackingNumber" character varying(20),
    "Status" shipment_status NOT NULL DEFAULT ('Pending'),
    "AdminComment" text,
    "CreatedAt" timestamptz NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    "UpdatedAt" timestamptz NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT "PK_OrderShipments" PRIMARY KEY ("Id"),
    CONSTRAINT "OrderShipments_OrderId_fkey" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE,
    CONSTRAINT "OrderShipments_SupplierId_fkey" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "OrderStatusHistory" (
    "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
    "OrderId" uuid NOT NULL,
    "OldStatus" order_status,
    "NewStatus" order_status NOT NULL,
    "ChangedByAdminId" uuid,
    "Comment" text,
    "CreatedAt" timestamptz NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT "PK_OrderStatusHistory" PRIMARY KEY ("Id"),
    CONSTRAINT "OrderStatusHistory_ChangedByAdminId_fkey" FOREIGN KEY ("ChangedByAdminId") REFERENCES "Admins" ("Id") ON DELETE SET NULL,
    CONSTRAINT "OrderStatusHistory_OrderId_fkey" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE
);

CREATE TABLE "CampaignProducts" (
    "CampaignId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "PromoPrice" numeric(10,2),
    CONSTRAINT "PK_CampaignProducts" PRIMARY KEY ("CampaignId", "ProductId"),
    CONSTRAINT "CampaignProducts_CampaignId_fkey" FOREIGN KEY ("CampaignId") REFERENCES "Campaigns" ("Id") ON DELETE CASCADE,
    CONSTRAINT "CampaignProducts_ProductId_fkey" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
);

CREATE TABLE "OrderItems" (
    "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
    "OrderId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductNameSnapshot" character varying(200) NOT NULL,
    "Quantity" integer NOT NULL,
    "UnitPrice" numeric(10,2) NOT NULL,
    "UnitDropPrice" numeric(10,2) NOT NULL,
    CONSTRAINT "PK_OrderItems" PRIMARY KEY ("Id"),
    CONSTRAINT "OrderItems_Quantity_check" CHECK ("Quantity" > 0),
    CONSTRAINT "OrderItems_OrderId_fkey" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE,
    CONSTRAINT "OrderItems_ProductId_fkey" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "ProductImages" (
    "Id" uuid NOT NULL DEFAULT (gen_random_uuid()),
    "ProductId" uuid NOT NULL,
    "Url" text NOT NULL,
    "Alt" character varying(255),
    "SortOrder" integer NOT NULL DEFAULT 0,
    CONSTRAINT "PK_ProductImages" PRIMARY KEY ("Id"),
    CONSTRAINT "ProductImages_ProductId_fkey" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
);

CREATE TABLE "ProductRelated" (
    "ProductId" uuid NOT NULL,
    "RelatedProductId" uuid NOT NULL,
    CONSTRAINT "PK_ProductRelated" PRIMARY KEY ("ProductId", "RelatedProductId"),
    CONSTRAINT "ProductRelated_NotSelf_check" CHECK ("ProductId" <> "RelatedProductId"),
    CONSTRAINT "ProductRelated_ProductId_fkey" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
    CONSTRAINT "ProductRelated_RelatedProductId_fkey" FOREIGN KEY ("RelatedProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "Admins_TelegramId_key" ON "Admins" ("TelegramId");

CREATE INDEX "IX_CampaignProducts_ProductId" ON "CampaignProducts" ("ProductId");

CREATE UNIQUE INDEX "Campaigns_Slug_key" ON "Campaigns" ("Slug");

CREATE UNIQUE INDEX "Categories_Slug_key" ON "Categories" ("Slug");

CREATE INDEX "IX_Categories_ParentId" ON "Categories" ("ParentId");

CREATE INDEX "IX_OrderItems_ProductId" ON "OrderItems" ("ProductId");

CREATE UNIQUE INDEX "OrderItems_OrderId_ProductId_key" ON "OrderItems" ("OrderId", "ProductId");

CREATE INDEX "IX_Orders_Active" ON "Orders" ("OrderStatus") WHERE "OrderStatus" IN ('New','Confirmed','Packed');

CREATE INDEX "IX_Orders_CampaignId" ON "Orders" ("CampaignId");

CREATE INDEX "IX_Orders_CreatedAt" ON "Orders" ("CreatedAt" DESC);

CREATE INDEX "IX_Orders_CustomerPhone" ON "Orders" ("CustomerPhone");

CREATE UNIQUE INDEX "IX_Orders_ExternalPaymentId" ON "Orders" ("ExternalPaymentId") WHERE "ExternalPaymentId" IS NOT NULL;

CREATE UNIQUE INDEX "Orders_OrderNumber_key" ON "Orders" ("OrderNumber");

CREATE INDEX "IX_OrderShipments_SupplierId" ON "OrderShipments" ("SupplierId");

CREATE UNIQUE INDEX "OrderShipments_OrderId_SupplierId_key" ON "OrderShipments" ("OrderId", "SupplierId");

CREATE INDEX "IX_OrderStatusHistory_ChangedByAdminId" ON "OrderStatusHistory" ("ChangedByAdminId");

CREATE INDEX "IX_OrderStatusHistory_OrderId" ON "OrderStatusHistory" ("OrderId");

CREATE INDEX "IX_ProductImages_ProductId" ON "ProductImages" ("ProductId");

CREATE INDEX "IX_ProductRelated_RelatedProductId" ON "ProductRelated" ("RelatedProductId");

CREATE INDEX "IX_Products_Attributes" ON "Products" USING gin ("Attributes");

CREATE INDEX "IX_Products_Catalog" ON "Products" ("CategoryId", "Price") WHERE "IsActive" IS TRUE;

CREATE INDEX "IX_Products_SupplierId" ON "Products" ("SupplierId");

CREATE UNIQUE INDEX "Products_Sku_key" ON "Products" ("Sku");

CREATE UNIQUE INDEX "Products_Slug_key" ON "Products" ("Slug");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261102231516_InitialCreate', '8.0.11');

COMMIT;



