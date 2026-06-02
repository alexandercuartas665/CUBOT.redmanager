using CubotRedManager.Domain.Entities;
using CubotRedManager.Domain.Enums;
using CubotRedManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Web.Seed;

/// <summary>
/// Siembra los dos modelos demo del Contenedor de Datos para la agencia demo, solo si no existen.
/// Se crean vacios (sin filas) para que el operador cargue datos o importe desde Excel.
/// </summary>
public static class DataContainerSeed
{
    public static async Task EnsureAsync(CubotRedManagerDbContext db, Guid demoTenantId)
    {
        await EnsureContainerAsync(db, demoTenantId, "Gestion de Productos", new[]
        {
            ("NOMBRE LINEA", DataColumnType.Text),
            ("NOMBRE SUBLINEA", DataColumnType.Text),
            ("LINEA", DataColumnType.Text),
            ("SUBLINEA", DataColumnType.Text),
            ("SINTOMAS", DataColumnType.Text),
            ("MENSAJE 1", DataColumnType.Text),
            ("MENSAJE 2", DataColumnType.Text),
            ("NOMBRE PRODUCTOS", DataColumnType.Text),
            ("PAIS", DataColumnType.Text)
        });

        await EnsureContainerAsync(db, demoTenantId, "Listado Precios Productos", new[]
        {
            ("PAIS", DataColumnType.Text),
            ("PRODUCTO", DataColumnType.Text),
            ("PRECIO", DataColumnType.Decimal),
            ("BENEFICIO", DataColumnType.Text)
        });
    }

    private static async Task EnsureContainerAsync(
        CubotRedManagerDbContext db,
        Guid tenantId,
        string name,
        (string Name, DataColumnType Type)[] columns)
    {
        var exists = await db.DataContainers
            .IgnoreQueryFilters()
            .AnyAsync(c => c.TenantId == tenantId && c.Name == name);
        if (exists) { return; }

        var container = new DataContainer
        {
            TenantId = tenantId,
            Name = name,
            Description = null
        };
        db.DataContainers.Add(container);

        var order = 0;
        foreach (var (colName, colType) in columns)
        {
            db.DataContainerColumns.Add(new DataContainerColumn
            {
                TenantId = tenantId,
                ContainerId = container.Id,
                Name = colName,
                Type = colType,
                SortOrder = order++,
                IsRequired = false
            });
        }
        await db.SaveChangesAsync();
    }
}
