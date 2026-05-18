using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingJournal.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnsureAzureMissingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[JournalEntryImages]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [JournalEntryImages] (
                        [Id] int NOT NULL IDENTITY,
                        [JournalEntryId] int NOT NULL,
                        [ImageData] varbinary(max) NOT NULL,
                        [ImageContentType] nvarchar(100) NOT NULL,
                        [ImageFileName] nvarchar(255) NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_JournalEntryImages] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_JournalEntryImages_JournalEntries_JournalEntryId] FOREIGN KEY ([JournalEntryId]) REFERENCES [JournalEntries] ([Id]) ON DELETE CASCADE
                    );
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_JournalEntryImages_JournalEntryId' AND [object_id] = OBJECT_ID(N'[JournalEntryImages]'))
                BEGIN
                    CREATE INDEX [IX_JournalEntryImages_JournalEntryId] ON [JournalEntryImages] ([JournalEntryId]);
                END;
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[InvoiceCompanies]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [InvoiceCompanies] (
                        [Id] int NOT NULL IDENTITY,
                        [UserId] nvarchar(450) NOT NULL,
                        [Name] nvarchar(160) NOT NULL,
                        [RegistrationNumber] nvarchar(40) NULL,
                        [TaxNumber] nvarchar(40) NULL,
                        [IsVatPayer] bit NOT NULL DEFAULT CAST(0 AS bit),
                        [Street] nvarchar(220) NULL,
                        [City] nvarchar(120) NULL,
                        [ZipCode] nvarchar(30) NULL,
                        [Country] nvarchar(80) NULL,
                        [Email] nvarchar(180) NULL,
                        [Phone] nvarchar(60) NULL,
                        [BankAccount] nvarchar(80) NULL,
                        [Iban] nvarchar(80) NULL,
                        [IsDefaultSupplier] bit NOT NULL,
                        [LastHourlyRate] decimal(18,2) NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_InvoiceCompanies] PRIMARY KEY ([Id])
                    );
                END;

                IF COL_LENGTH(N'InvoiceCompanies', N'LastHourlyRate') IS NULL
                    ALTER TABLE [InvoiceCompanies] ADD [LastHourlyRate] decimal(18,2) NULL;

                IF COL_LENGTH(N'InvoiceCompanies', N'IsVatPayer') IS NULL
                    ALTER TABLE [InvoiceCompanies] ADD [IsVatPayer] bit NOT NULL DEFAULT CAST(0 AS bit);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvoiceCompanies_UserId_Name' AND [object_id] = OBJECT_ID(N'[InvoiceCompanies]'))
                    CREATE INDEX [IX_InvoiceCompanies_UserId_Name] ON [InvoiceCompanies] ([UserId], [Name]);
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[Invoices]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Invoices] (
                        [Id] int NOT NULL IDENTITY,
                        [UserId] nvarchar(450) NOT NULL,
                        [InvoiceNumber] nvarchar(40) NOT NULL,
                        [SupplierCompanyId] int NOT NULL,
                        [CustomerCompanyId] int NOT NULL,
                        [IssueDate] datetime2 NOT NULL,
                        [DueDate] datetime2 NOT NULL,
                        [TaxableSupplyDate] datetime2 NULL,
                        [Currency] nvarchar(3) NOT NULL,
                        [Status] int NOT NULL,
                        [Notes] nvarchar(1000) NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_Invoices] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_Invoices_InvoiceCompanies_CustomerCompanyId] FOREIGN KEY ([CustomerCompanyId]) REFERENCES [InvoiceCompanies] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_Invoices_InvoiceCompanies_SupplierCompanyId] FOREIGN KEY ([SupplierCompanyId]) REFERENCES [InvoiceCompanies] ([Id]) ON DELETE NO ACTION
                    );
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Invoices_CustomerCompanyId' AND [object_id] = OBJECT_ID(N'[Invoices]'))
                    CREATE INDEX [IX_Invoices_CustomerCompanyId] ON [Invoices] ([CustomerCompanyId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Invoices_SupplierCompanyId' AND [object_id] = OBJECT_ID(N'[Invoices]'))
                    CREATE INDEX [IX_Invoices_SupplierCompanyId] ON [Invoices] ([SupplierCompanyId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Invoices_UserId_InvoiceNumber' AND [object_id] = OBJECT_ID(N'[Invoices]'))
                    CREATE UNIQUE INDEX [IX_Invoices_UserId_InvoiceNumber] ON [Invoices] ([UserId], [InvoiceNumber]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Invoices_UserId_IssueDate' AND [object_id] = OBJECT_ID(N'[Invoices]'))
                    CREATE INDEX [IX_Invoices_UserId_IssueDate] ON [Invoices] ([UserId], [IssueDate]);
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[InvoiceItems]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [InvoiceItems] (
                        [Id] int NOT NULL IDENTITY,
                        [InvoiceId] int NOT NULL,
                        [Description] nvarchar(260) NOT NULL,
                        [Unit] nvarchar(20) NOT NULL,
                        [Quantity] decimal(18,4) NOT NULL,
                        [UnitPrice] decimal(18,2) NOT NULL,
                        [VatRate] decimal(5,2) NOT NULL,
                        CONSTRAINT [PK_InvoiceItems] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_InvoiceItems_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [Invoices] ([Id]) ON DELETE CASCADE
                    );
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvoiceItems_InvoiceId' AND [object_id] = OBJECT_ID(N'[InvoiceItems]'))
                    CREATE INDEX [IX_InvoiceItems_InvoiceId] ON [InvoiceItems] ([InvoiceId]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
