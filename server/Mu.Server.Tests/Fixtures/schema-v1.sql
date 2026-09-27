-- Frozen schema exported from the schema-1 server, before feature versions existed.
CREATE TABLE "AspNetRoles" (
    "Id" TEXT NOT NULL PRIMARY KEY,
    "Name" TEXT NULL,
    "NormalizedName" TEXT NULL,
    "ConcurrencyStamp" TEXT NULL
);
CREATE TABLE "AspNetUsers" (
    "Id" TEXT NOT NULL PRIMARY KEY,
    "CreatedAt" INTEGER NOT NULL,
    "DisabledAt" INTEGER NULL,
    "ProExpiresAt" INTEGER NULL,
    "TermsVersion" TEXT NOT NULL,
    "TermsAcceptedAt" INTEGER NOT NULL,
    "UserName" TEXT NULL,
    "NormalizedUserName" TEXT NULL,
    "Email" TEXT NULL,
    "NormalizedEmail" TEXT NULL,
    "EmailConfirmed" INTEGER NOT NULL,
    "PasswordHash" TEXT NULL,
    "SecurityStamp" TEXT NULL,
    "ConcurrencyStamp" TEXT NULL,
    "PhoneNumber" TEXT NULL,
    "PhoneNumberConfirmed" INTEGER NOT NULL,
    "TwoFactorEnabled" INTEGER NOT NULL,
    "LockoutEnd" TEXT NULL,
    "LockoutEnabled" INTEGER NOT NULL,
    "AccessFailedCount" INTEGER NOT NULL
);
CREATE TABLE "AuditEntries" (
    "Id" TEXT NOT NULL PRIMARY KEY,
    "Actor" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "UserId" TEXT NULL,
    "Reason" TEXT NOT NULL,
    "BeforeJson" TEXT NOT NULL,
    "AfterJson" TEXT NOT NULL,
    "CreatedAt" INTEGER NOT NULL
);
CREATE TABLE "DatabaseSchemas" (
    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    "Version" INTEGER NOT NULL
);
CREATE TABLE "FeatureDefinitions" (
    "Key" TEXT NOT NULL PRIMARY KEY,
    "Enabled" INTEGER NOT NULL,
    "MinimumTier" TEXT NOT NULL,
    "UpdatedAt" INTEGER NOT NULL
);
CREATE TABLE "MembershipChanges" (
    "Id" TEXT NOT NULL PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "PreviousExpiresAt" INTEGER NULL,
    "NewExpiresAt" INTEGER NULL,
    "Reason" TEXT NOT NULL,
    "Actor" TEXT NOT NULL,
    "Source" TEXT NOT NULL,
    "CreatedAt" INTEGER NOT NULL
);
CREATE TABLE "Plans" (
    "Id" TEXT NOT NULL PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "DurationDays" INTEGER NOT NULL,
    "PriceMinor" INTEGER NOT NULL,
    "Currency" TEXT NOT NULL,
    "Enabled" INTEGER NOT NULL
);
CREATE TABLE "VerificationCodes" (
    "Id" TEXT NOT NULL PRIMARY KEY,
    "Email" TEXT NOT NULL,
    "Purpose" TEXT NOT NULL,
    "CodeHash" TEXT NOT NULL,
    "CreatedAt" INTEGER NOT NULL,
    "ExpiresAt" INTEGER NOT NULL,
    "Attempts" INTEGER NOT NULL,
    "ConsumedAt" INTEGER NULL,
    "DeliverySucceeded" INTEGER NOT NULL
);
CREATE TABLE "AspNetRoleClaims" (
    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    "RoleId" TEXT NOT NULL,
    "ClaimType" TEXT NULL,
    "ClaimValue" TEXT NULL,
    FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE
);
CREATE TABLE "AspNetUserClaims" (
    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "ClaimType" TEXT NULL,
    "ClaimValue" TEXT NULL,
    FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);
CREATE TABLE "AspNetUserLogins" (
    "LoginProvider" TEXT NOT NULL,
    "ProviderKey" TEXT NOT NULL,
    "ProviderDisplayName" TEXT NULL,
    "UserId" TEXT NOT NULL,
    PRIMARY KEY ("LoginProvider", "ProviderKey"),
    FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);
CREATE TABLE "AspNetUserRoles" (
    "UserId" TEXT NOT NULL,
    "RoleId" TEXT NOT NULL,
    PRIMARY KEY ("UserId", "RoleId"),
    FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE,
    FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);
CREATE TABLE "AspNetUserTokens" (
    "UserId" TEXT NOT NULL,
    "LoginProvider" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Value" TEXT NULL,
    PRIMARY KEY ("UserId", "LoginProvider", "Name"),
    FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);
CREATE TABLE "AuthSessions" (
    "Id" TEXT NOT NULL PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "FamilyId" TEXT NOT NULL,
    "AccessHash" TEXT NOT NULL,
    "RefreshHash" TEXT NOT NULL,
    "CreatedAt" INTEGER NOT NULL,
    "AccessExpiresAt" INTEGER NOT NULL,
    "RefreshExpiresAt" INTEGER NOT NULL,
    "RotatedAt" INTEGER NULL,
    "RevokedAt" INTEGER NULL,
    "DeviceName" TEXT NOT NULL,
    FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);
CREATE TABLE "Orders" (
    "Id" TEXT NOT NULL PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "PlanId" TEXT NOT NULL,
    "PlanName" TEXT NOT NULL,
    "DurationDays" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "AmountMinor" INTEGER NOT NULL,
    "Currency" TEXT NOT NULL,
    "CreatedAt" INTEGER NOT NULL,
    "PaidAt" INTEGER NULL,
    "Provider" TEXT NULL,
    "ProviderTransactionId" TEXT NULL,
    FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    FOREIGN KEY ("PlanId") REFERENCES "Plans" ("Id") ON DELETE RESTRICT
);
CREATE TABLE "PaymentEvents" (
    "Id" TEXT NOT NULL PRIMARY KEY,
    "Provider" TEXT NOT NULL,
    "TransactionId" TEXT NOT NULL,
    "OrderId" TEXT NULL,
    "ReceivedAt" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE RESTRICT
);
CREATE INDEX "IX_AspNetRoleClaims_RoleId" ON "AspNetRoleClaims" ("RoleId");
CREATE UNIQUE INDEX "RoleNameIndex" ON "AspNetRoles" ("NormalizedName");
CREATE INDEX "IX_AspNetUserClaims_UserId" ON "AspNetUserClaims" ("UserId");
CREATE INDEX "IX_AspNetUserLogins_UserId" ON "AspNetUserLogins" ("UserId");
CREATE INDEX "IX_AspNetUserRoles_RoleId" ON "AspNetUserRoles" ("RoleId");
CREATE UNIQUE INDEX "EmailIndex" ON "AspNetUsers" ("NormalizedEmail");
CREATE UNIQUE INDEX "UserNameIndex" ON "AspNetUsers" ("NormalizedUserName");
CREATE INDEX "IX_AuditEntries_CreatedAt" ON "AuditEntries" ("CreatedAt");
CREATE UNIQUE INDEX "IX_AuthSessions_AccessHash" ON "AuthSessions" ("AccessHash");
CREATE UNIQUE INDEX "IX_AuthSessions_RefreshHash" ON "AuthSessions" ("RefreshHash");
CREATE INDEX "IX_AuthSessions_UserId_FamilyId" ON "AuthSessions" ("UserId", "FamilyId");
CREATE INDEX "IX_MembershipChanges_UserId_CreatedAt" ON "MembershipChanges" ("UserId", "CreatedAt");
CREATE INDEX "IX_Orders_PlanId" ON "Orders" ("PlanId");
CREATE UNIQUE INDEX "IX_Orders_Provider_ProviderTransactionId" ON "Orders" ("Provider", "ProviderTransactionId");
CREATE INDEX "IX_Orders_UserId_CreatedAt" ON "Orders" ("UserId", "CreatedAt");
CREATE INDEX "IX_PaymentEvents_OrderId" ON "PaymentEvents" ("OrderId");
CREATE UNIQUE INDEX "IX_PaymentEvents_Provider_TransactionId" ON "PaymentEvents" ("Provider", "TransactionId");
CREATE INDEX "IX_VerificationCodes_Email_Purpose_CreatedAt" ON "VerificationCodes" ("Email", "Purpose", "CreatedAt");

INSERT INTO "DatabaseSchemas" VALUES (1, 1);
INSERT INTO "AspNetUsers" ("Id","CreatedAt","ProExpiresAt","TermsVersion","TermsAcceptedAt","UserName","NormalizedUserName","Email","NormalizedEmail","EmailConfirmed","PasswordHash","SecurityStamp","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount")
VALUES ('legacy-user',1700000000,1800000000,'2026-09-26',1700000000,'legacy@example.com','LEGACY@EXAMPLE.COM','legacy@example.com','LEGACY@EXAMPLE.COM',1,'fixture-password-hash','fixture-security-stamp',0,0,1,0);
INSERT INTO "FeatureDefinitions" VALUES ('library.manage',0,'pro',1700000001),('game.launch',1,'standard',1700000002);
INSERT INTO "AuditEntries" VALUES ('legacy-audit','fixture-admin','feature.configure',NULL,'fixture reason','{"legacy":true}','{"enabled":false}',1700000001);
INSERT INTO "AuthSessions" VALUES ('legacy-session','legacy-user','legacy-family','fixture-access-hash','fixture-refresh-hash',1700000000,1800000000,1801000000,NULL,NULL,'fixture-device');
INSERT INTO "MembershipChanges" VALUES ('legacy-grant','legacy-user',NULL,1800000000,'fixture grant','fixture-admin','manual',1700000000);
