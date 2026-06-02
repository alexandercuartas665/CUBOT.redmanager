using CubotRedManager.Domain.Entities;
using CubotRedManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubotRedManager.Web.Seed;

/// <summary>
/// Siembra filas demo (20 productos) en los DataContainers de la agencia demo, solo si
/// el container existe y NO tiene filas todavia. Pensado como contexto para el agente
/// FUXION en pruebas locales. Idempotente: si ya hay filas, no inserta nada.
///
/// Los nombres de producto y claims son GENERICOS (VIVA, FORZA, ALPHA, FLORA, NUTRA, etc.)
/// para no chocar con marcas registradas reales.
/// </summary>
public static class DataContainerDataSeed
{
    public static async Task EnsureAsync(CubotRedManagerDbContext db, Guid demoTenantId)
    {
        // Container "Gestion de Productos" -> 20 productos.
        var productsContainer = await db.DataContainers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == demoTenantId && c.Name == "Gestion de Productos");

        if (productsContainer is not null)
        {
            var hasRows = await db.DataContainerRows
                .IgnoreQueryFilters()
                .AnyAsync(r => r.ContainerId == productsContainer.Id);
            if (!hasRows)
            {
                await SeedProductsAsync(db, demoTenantId, productsContainer.Id);
            }
        }

        // Container "Listado Precios Productos" -> 20 precios (uno por producto).
        var pricesContainer = await db.DataContainers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == demoTenantId && c.Name == "Listado Precios Productos");

        if (pricesContainer is not null)
        {
            var hasRows = await db.DataContainerRows
                .IgnoreQueryFilters()
                .AnyAsync(r => r.ContainerId == pricesContainer.Id);
            if (!hasRows)
            {
                await SeedPricesAsync(db, demoTenantId, pricesContainer.Id);
            }
        }
    }

    private static async Task SeedProductsAsync(CubotRedManagerDbContext db, Guid tenantId, Guid containerId)
    {
        var columns = await db.DataContainerColumns
            .IgnoreQueryFilters()
            .Where(c => c.ContainerId == containerId)
            .ToListAsync();

        var colByName = columns.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);

        var products = BuildProductRows();
        foreach (var p in products)
        {
            var row = new DataContainerRow
            {
                TenantId = tenantId,
                ContainerId = containerId
            };
            db.DataContainerRows.Add(row);

            AddCell(db, tenantId, row.Id, colByName, "NOMBRE LINEA", p.NombreLinea);
            AddCell(db, tenantId, row.Id, colByName, "NOMBRE SUBLINEA", p.NombreSublinea);
            AddCell(db, tenantId, row.Id, colByName, "LINEA", p.Linea);
            AddCell(db, tenantId, row.Id, colByName, "SUBLINEA", p.Sublinea);
            AddCell(db, tenantId, row.Id, colByName, "SINTOMAS", p.Sintomas);
            AddCell(db, tenantId, row.Id, colByName, "MENSAJE 1", p.Mensaje1);
            AddCell(db, tenantId, row.Id, colByName, "MENSAJE 2", p.Mensaje2);
            AddCell(db, tenantId, row.Id, colByName, "NOMBRE PRODUCTOS", p.NombreProducto);
            AddCell(db, tenantId, row.Id, colByName, "PAIS", p.Pais);
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedPricesAsync(CubotRedManagerDbContext db, Guid tenantId, Guid containerId)
    {
        var columns = await db.DataContainerColumns
            .IgnoreQueryFilters()
            .Where(c => c.ContainerId == containerId)
            .ToListAsync();

        var colByName = columns.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);

        var prices = BuildPriceRows();
        foreach (var price in prices)
        {
            var row = new DataContainerRow
            {
                TenantId = tenantId,
                ContainerId = containerId
            };
            db.DataContainerRows.Add(row);

            AddCell(db, tenantId, row.Id, colByName, "PAIS", price.Pais);
            AddCell(db, tenantId, row.Id, colByName, "PRODUCTO", price.Producto);
            AddCell(db, tenantId, row.Id, colByName, "PRECIO", price.Precio);
            AddCell(db, tenantId, row.Id, colByName, "BENEFICIO", price.Beneficio);
        }

        await db.SaveChangesAsync();
    }

    private static void AddCell(
        CubotRedManagerDbContext db,
        Guid tenantId,
        Guid rowId,
        IReadOnlyDictionary<string, Guid> colByName,
        string colName,
        string? value)
    {
        if (!colByName.TryGetValue(colName, out var colId)) { return; }
        db.DataContainerCells.Add(new DataContainerCell
        {
            TenantId = tenantId,
            RowId = rowId,
            ColumnId = colId,
            Value = value
        });
    }

    private sealed record ProductRow(
        string NombreLinea,
        string NombreSublinea,
        string Linea,
        string Sublinea,
        string Sintomas,
        string Mensaje1,
        string Mensaje2,
        string NombreProducto,
        string Pais);

    private sealed record PriceRow(
        string Pais,
        string Producto,
        string Precio,
        string Beneficio);

    private static ProductRow[] BuildProductRows() => new[]
    {
        new ProductRow(
            "Linea Nutricion", "Suplementos diarios",
            "Nutricion", "Suplementos",
            "cansancio, fatiga cronica, falta de energia",
            "Reactiva tu energia diaria de forma natural",
            "Aporta nutrientes esenciales que tu cuerpo pide",
            "VIVA", "Colombia"),

        new ProductRow(
            "Linea Energia", "Activacion mental",
            "Energia", "Activadores",
            "cansancio, somnolencia, falta de concentracion",
            "Despierta tu mejor version cada manana",
            "Foco mental y vitalidad sin estimulantes agresivos",
            "FORZA", "Mexico"),

        new ProductRow(
            "Linea Bienestar", "Equilibrio integral",
            "Bienestar", "Adaptogenos",
            "estres, ansiedad, nerviosismo",
            "Equilibra cuerpo y mente cada dia",
            "Ayuda al cuerpo a adaptarse al estres del dia a dia",
            "ALPHA BALANCE", "Peru"),

        new ProductRow(
            "Linea Nutricion", "Salud digestiva",
            "Nutricion", "Probioticos",
            "mala digestion, hinchazon, estrenimiento",
            "Cuida tu digestion desde adentro",
            "Microbiota saludable para mejor absorcion",
            "FLORA LIV", "Ecuador"),

        new ProductRow(
            "Linea Energia", "Cafes funcionales",
            "Energia", "Bebidas",
            "cansancio, baja energia, falta de motivacion",
            "El cafe que ademas te nutre",
            "Sabor cafe con extractos naturales energizantes",
            "CAFE GANO", "Chile"),

        new ProductRow(
            "Linea Inmunidad", "Defensa diaria",
            "Inmunidad", "Antioxidantes",
            "baja inmunidad, resfriados frecuentes, gripa",
            "Fortalece tus defensas todos los dias",
            "Vitaminas y antioxidantes para tu sistema inmune",
            "NUTRA SHIELD", "Argentina"),

        new ProductRow(
            "Linea Belleza", "Piel saludable",
            "Belleza", "Colageno",
            "piel apagada, arrugas tempranas, cabello debil",
            "Belleza que se nota desde adentro",
            "Colageno hidrolizado para piel, cabello y unas",
            "BELLA SKIN", "Colombia"),

        new ProductRow(
            "Linea Nutricion", "Control de peso",
            "Nutricion", "Saciedad",
            "ansiedad por comer, sobrepeso, antojos",
            "Controla tu apetito de forma natural",
            "Fibra y proteinas para sentirte satisfecho",
            "SLIM PRO", "Panama"),

        new ProductRow(
            "Linea Bienestar", "Sueno reparador",
            "Bienestar", "Relajantes",
            "insomnio, sueno ligero, despertar cansado",
            "Duerme profundo, despierta renovado",
            "Mezcla botanica que ayuda a conciliar el sueno",
            "NOX DREAM", "Mexico"),

        new ProductRow(
            "Linea Salud Articular", "Movilidad",
            "Salud", "Articular",
            "dolores articulares, rigidez, dolor de rodillas",
            "Recupera la libertad de moverte sin dolor",
            "Soporte natural para articulaciones y cartilagos",
            "FLEXI MAX", "Peru"),

        new ProductRow(
            "Linea Energia", "Pre entreno",
            "Energia", "Deportivo",
            "fatiga al entrenar, baja resistencia",
            "Mas energia en cada entrenamiento",
            "Combinacion deportiva para rendimiento fisico",
            "FORZA SPORT", "Ecuador"),

        new ProductRow(
            "Linea Nutricion", "Vitaminas mujer",
            "Nutricion", "Vitaminas",
            "fatiga, cabello debil, ciclo irregular",
            "Cuidate como solo tu sabes hacerlo",
            "Multivitaminico especifico para la mujer activa",
            "VIVA FEM", "Chile"),

        new ProductRow(
            "Linea Nutricion", "Vitaminas hombre",
            "Nutricion", "Vitaminas",
            "fatiga, baja libido, perdida de masa muscular",
            "Energia y vitalidad para el hombre activo",
            "Multivitaminico con zinc, magnesio y vitamina D",
            "FORZA MEN", "Argentina"),

        new ProductRow(
            "Linea Bienestar", "Salud cardiovascular",
            "Bienestar", "Omegas",
            "colesterol alto, presion arterial, fatiga",
            "Cuida tu corazon todos los dias",
            "Omega 3 de origen marino para salud cardiovascular",
            "OMEGA HEART", "Colombia"),

        new ProductRow(
            "Linea Inmunidad", "Defensa avanzada",
            "Inmunidad", "Vitamina C",
            "baja inmunidad, gripas, alergias",
            "Tu escudo natural contra el ambiente",
            "Vitamina C liposomal de alta absorcion",
            "NUTRA C PLUS", "Panama"),

        new ProductRow(
            "Linea Belleza", "Antiage",
            "Belleza", "Antioxidantes",
            "envejecimiento, manchas, piel cansada",
            "Frena el reloj de tu piel",
            "Resveratrol y antioxidantes para piel joven",
            "BELLA AGE", "Mexico"),

        new ProductRow(
            "Linea Nutricion", "Salud cerebral",
            "Nutricion", "Cognitivo",
            "perdida de memoria, falta de concentracion, dolor de cabeza",
            "Mente clara, decisiones mas rapidas",
            "Nootropicos naturales para foco y memoria",
            "ALPHA MIND", "Peru"),

        new ProductRow(
            "Linea Bienestar", "Detox",
            "Bienestar", "Limpieza",
            "mala digestion, hinchazon, pesadez, fatiga",
            "Reinicia tu cuerpo de forma natural",
            "Apoyo hepatico y limpieza intestinal suave",
            "DETOX LIV", "Colombia"),

        new ProductRow(
            "Linea Nutricion", "Proteinas",
            "Nutricion", "Proteinas",
            "perdida muscular, fatiga, baja energia",
            "Construye masa muscular limpia",
            "Proteina vegetal de alta calidad y digestion suave",
            "PROTEO MAX", "Chile"),

        new ProductRow(
            "Linea Salud Hormonal", "Equilibrio mujer",
            "Salud", "Hormonal",
            "sofocos, irritabilidad, ciclo irregular, fatiga",
            "Acompana tu equilibrio hormonal cada dia",
            "Mezcla botanica para apoyar el equilibrio hormonal femenino",
            "FEMME BALANCE", "Argentina")
    };

    private static PriceRow[] BuildPriceRows() => new[]
    {
        new PriceRow("Colombia", "VIVA", "49.99",
            "Multinutriente diario que reactiva la energia con vitaminas del complejo B y adaptogenos suaves."),
        new PriceRow("Mexico", "FORZA", "54.50",
            "Activador mental que mejora foco y concentracion sin cafeina excesiva, ideal para horarios largos de trabajo."),
        new PriceRow("Peru", "ALPHA BALANCE", "62.99",
            "Adaptogeno que ayuda al cuerpo a manejar el estres y reduce la ansiedad cotidiana."),
        new PriceRow("Ecuador", "FLORA LIV", "39.50",
            "Probiotico que regula la digestion, reduce hinchazon y mejora la salud intestinal."),
        new PriceRow("Chile", "CAFE GANO", "32.99",
            "Cafe funcional con extractos naturales que aporta energia sostenida y antioxidantes."),
        new PriceRow("Argentina", "NUTRA SHIELD", "47.99",
            "Soporte inmunologico con vitaminas C, D, zinc y antioxidantes para fortalecer defensas."),
        new PriceRow("Colombia", "BELLA SKIN", "59.50",
            "Colageno hidrolizado con vitamina C y biotina para piel firme, cabello fuerte y unas resistentes."),
        new PriceRow("Panama", "SLIM PRO", "65.99",
            "Control de apetito con fibra y proteinas que reduce antojos y favorece la perdida de peso saludable."),
        new PriceRow("Mexico", "NOX DREAM", "44.50",
            "Mezcla relajante con melatonina, magnesio y manzanilla para conciliar el sueno y descansar profundo."),
        new PriceRow("Peru", "FLEXI MAX", "72.99",
            "Soporte articular con glucosamina, condroitina y cucurma para reducir dolor e inflamacion."),
        new PriceRow("Ecuador", "FORZA SPORT", "58.50",
            "Pre entreno natural que mejora resistencia, energia y rendimiento sin estimulantes agresivos."),
        new PriceRow("Chile", "VIVA FEM", "52.99",
            "Multivitaminico para mujer activa con hierro, acido folico, calcio y vitaminas del grupo B."),
        new PriceRow("Argentina", "FORZA MEN", "55.50",
            "Multivitaminico masculino con zinc, magnesio y vitamina D para energia y vitalidad."),
        new PriceRow("Colombia", "OMEGA HEART", "46.99",
            "Omega 3 marino de alta concentracion que apoya salud cardiovascular y reduce trigliceridos."),
        new PriceRow("Panama", "NUTRA C PLUS", "38.50",
            "Vitamina C liposomal de alta absorcion para defensa inmunologica diaria."),
        new PriceRow("Mexico", "BELLA AGE", "78.99",
            "Antioxidante avanzado con resveratrol y coenzima Q10 para combatir signos del envejecimiento."),
        new PriceRow("Peru", "ALPHA MIND", "67.50",
            "Nootropicos naturales como bacopa y ginkgo para mejorar memoria, foco y claridad mental."),
        new PriceRow("Colombia", "DETOX LIV", "41.99",
            "Apoyo hepatico con cardo mariano y diente de leon para limpiar el organismo de forma suave."),
        new PriceRow("Chile", "PROTEO MAX", "84.50",
            "Proteina vegetal de arroz y guisante de alta digestibilidad para construccion muscular limpia."),
        new PriceRow("Argentina", "FEMME BALANCE", "69.99",
            "Soporte hormonal femenino con dong quai, vitex y vitamina E para equilibrio durante el ciclo y la menopausia.")
    };
}
