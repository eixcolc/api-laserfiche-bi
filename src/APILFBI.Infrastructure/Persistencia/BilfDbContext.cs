using APILFBI.Domain.Entidades;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APILFBI.Infrastructure.Persistencia;

/// <summary>
/// Mapeo de la base BILF. El esquema lo definen los scripts de database/ (fuente de verdad);
/// aquí no se generan migraciones.
/// </summary>
public sealed class BilfDbContext(DbContextOptions<BilfDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<CuentaServicio> CuentasServicio => Set<CuentaServicio>();
    public DbSet<Scope> Scopes => Set<Scope>();
    public DbSet<Bitacora> Bitacora => Set<Bitacora>();
    public DbSet<VersionCatalogo> VersionesCatalogo => Set<VersionCatalogo>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Todas las fechas de BILF son datetime2(3).</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder cb) =>
        cb.Properties<DateTime>().HavePrecision(3);

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Catálogos simples: cat.<Tabla> con PK Id<Tabla>
        Catalogo<TipoCliente>(mb, "TipoCliente");
        Catalogo<TipoIdentificacion>(mb, "TipoIdentificacion");
        Catalogo<Segmentacion>(mb, "Segmentacion");
        Catalogo<EstadoExpediente>(mb, "EstadoExpediente");
        Catalogo<EstadoCarga>(mb, "EstadoCarga");
        Catalogo<MotivoRechazoCarga>(mb, "MotivoRechazoCarga");
        Catalogo<ReglaCarga>(mb, "ReglaCarga");
        Catalogo<OrigenAsociacion>(mb, "OrigenAsociacion");
        Catalogo<TipoRechazo>(mb, "TipoRechazo");
        Catalogo<EtapaRechazo>(mb, "EtapaRechazo");
        Catalogo<TipoDato>(mb, "TipoDato");
        Catalogo<ReglaNormalizacion>(mb, "ReglaNormalizacion");
        Catalogo<TipoOperacion>(mb, "TipoOperacion");
        Catalogo<OrigenOperacion>(mb, "OrigenOperacion");
        Catalogo<ResultadoOperacion>(mb, "ResultadoOperacion");
        Catalogo<EstadoDocumento>(mb, "EstadoDocumento");
        Catalogo<TipoArchivo>(mb, "TipoArchivo");
        Catalogo<TipoExpediente>(mb, "TipoExpediente");
        Catalogo<Llave>(mb, "Llave");
        Catalogo<TipoDocumento>(mb, "TipoDocumento").Property(e => e.Id).ValueGeneratedNever();
        Catalogo<Scope>(mb, "Scope", "seg");

        mb.Entity<TransicionEstadoDocumento>(e =>
        {
            e.ToTable("TransicionEstadoDocumento", "cat", t => t.HasTrigger("trg_TransicionEstadoDocumento_Version"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("IdTransicionEstadoDocumento");
        });

        mb.Entity<TipoDocumentoTipoArchivo>(e =>
        {
            e.ToTable("TipoDocumentoTipoArchivo", "cat", t => t.HasTrigger("trg_TipoDocumentoTipoArchivo_Version"));
            e.HasKey(x => new { x.IdTipoDocumento, x.IdTipoArchivo });
        });

        mb.Entity<TipoExpedienteTipoDocumento>(e =>
        {
            e.ToTable("TipoExpedienteTipoDocumento", "cat", t => t.HasTrigger("trg_TipoExpedienteTipoDocumento_Version"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("IdTipoExpedienteTipoDocumento");
        });

        mb.Entity<TipoExpedienteLlave>(e =>
        {
            e.ToTable("TipoExpedienteLlave", "cat", t =>
            {
                t.HasTrigger("trg_TipoExpedienteLlave_Version");
                t.HasTrigger("trg_TipoExpedienteLlave_Validar");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("IdTipoExpedienteLlave");
        });

        mb.Entity<CodigoRespuesta>(e =>
        {
            e.ToTable("CodigoRespuesta", "cat", t => t.HasTrigger("trg_CodigoRespuesta_Version"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("IdCodigoRespuesta");
        });

        mb.Entity<VersionCatalogo>(e =>
        {
            e.ToTable("VersionCatalogo", "cat");
            e.HasKey(x => x.NombreCatalogo);
        });

        mb.Entity<CuentaServicio>(e =>
        {
            e.ToTable("CuentaServicio", "seg");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("IdCuentaServicio");
            e.HasMany(x => x.Scopes).WithOne().HasForeignKey(x => x.IdCuentaServicio);
        });

        mb.Entity<CuentaServicioScope>(e =>
        {
            e.ToTable("CuentaServicioScope", "seg");
            e.HasKey(x => new { x.IdCuentaServicio, x.IdScope });
            e.HasOne(x => x.Scope).WithMany().HasForeignKey(x => x.IdScope);
        });

        mb.Entity<Bitacora>(e =>
        {
            // El trigger de solo inserción obliga a EF a no usar OUTPUT sin INTO.
            e.ToTable("Bitacora", "aud", t => t.HasTrigger("trg_Bitacora_SoloInsercion"));
            e.HasKey(x => new { x.FechaHoraUtc, x.IdBitacora });
            e.Property(x => x.IdBitacora).ValueGeneratedOnAdd();
        });

        mb.Entity<DataProtectionKey>(e => e.ToTable("DataProtectionKeys", "seg"));

        // Transaccional: solo lectura desde EF; las escrituras van por stored procedures.
        mb.Entity<Expediente>(e =>
        {
            e.ToTable("Expediente", "trx");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("IdExpediente");
            e.HasMany(x => x.Llaves).WithOne().HasForeignKey(x => x.IdExpediente);
        });

        mb.Entity<LlaveExpediente>(e =>
        {
            e.ToTable("LlaveExpediente", "trx");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("IdLlaveExpediente");
        });

        mb.Entity<Documento>(e =>
        {
            e.ToTable("Documento", "trx");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("IdDocumento");
        });

        mb.Entity<ExpedienteDocumento>(e =>
        {
            e.ToTable("ExpedienteDocumento", "trx");
            e.HasKey(x => new { x.IdExpediente, x.IdDocumento });
        });

        mb.Entity<CargaDocumento>(e =>
        {
            e.ToTable("CargaDocumento", "trx");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("IdCargaDocumento");
        });
    }

    private static EntityTypeBuilder<T> Catalogo<T>(ModelBuilder mb, string tabla, string esquema = "cat") where T : Catalogo
    {
        var e = mb.Entity<T>();
        e.ToTable(tabla, esquema, t =>
        {
            if (esquema == "cat") t.HasTrigger($"trg_{tabla}_Version");
        });
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("Id" + tabla);
        return e;
    }
}
