using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TradingJournal.Data;

// Keep the two existing migration histories separate within the application assembly.
public sealed class ProviderMigrationsAssembly : IMigrationsAssembly
{
    public Assembly Assembly => typeof(ApplicationDbContext).Assembly;
    public ModelSnapshot? ModelSnapshot => null;
    public IReadOnlyDictionary<string, TypeInfo> Migrations { get; }
    public ProviderMigrationsAssembly(ICurrentDbContext current)
    {
        var ns = current.Context.Database.IsSqlServer()
            ? "TradingJournal.Data.Migrations" : "TradingJournal.Data.PostgresMigrations";
        Migrations = Assembly.DefinedTypes
            .Where(t => t.Namespace == ns && !t.IsAbstract && typeof(Migration).IsAssignableFrom(t))
            .Select(t => (Type: t, Id: t.GetCustomAttribute<MigrationAttribute>()?.Id))
            .Where(x => x.Id != null).OrderBy(x => x.Id)
            .ToDictionary(x => x.Id!, x => x.Type);
    }
    public string? FindMigrationId(string nameOrId) => Migrations.Keys.FirstOrDefault(id =>
        string.Equals(id, nameOrId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(id[(id.IndexOf('_') + 1)..], nameOrId, StringComparison.OrdinalIgnoreCase));
    public Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
    {
        var migration = (Migration)Activator.CreateInstance(migrationClass.AsType())!;
        migration.ActiveProvider = activeProvider;
        return migration;
    }
}
