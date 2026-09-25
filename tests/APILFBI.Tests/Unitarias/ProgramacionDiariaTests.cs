using APILFBI.Infrastructure.Trabajos;

namespace APILFBI.Tests.Unitarias;

public sealed class ProgramacionDiariaTests
{
    private static readonly TimeZoneInfo Centroamerica = TimeZoneInfo.FindSystemTimeZoneById("Central America Standard Time");   // UTC-6

    [Fact]
    public void Si_la_hora_aun_no_llega_hoy_se_programa_para_hoy()
    {
        var ahora = new DateTimeOffset(2026, 9, 24, 6, 30, 0, TimeSpan.Zero);   // 00:30 local

        var siguiente = ProgramacionDiaria.Siguiente(ahora, new TimeOnly(1, 0), Centroamerica);

        Assert.Equal(new DateTimeOffset(2026, 9, 24, 7, 0, 0, TimeSpan.Zero), siguiente);
    }

    [Fact]
    public void Si_la_hora_ya_paso_se_programa_para_manana()
    {
        var ahora = new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);   // 02:00 local

        var siguiente = ProgramacionDiaria.Siguiente(ahora, new TimeOnly(1, 0), Centroamerica);

        Assert.Equal(new DateTimeOffset(2026, 9, 25, 7, 0, 0, TimeSpan.Zero), siguiente);
    }

    [Fact]
    public void La_fecha_de_corte_es_la_fecha_local_no_la_utc()
    {
        var ahora = new DateTimeOffset(2026, 9, 25, 3, 0, 0, TimeSpan.Zero);   // 21:00 local del 24

        Assert.Equal(new DateOnly(2026, 9, 24), ProgramacionDiaria.FechaLocal(ahora, Centroamerica));
    }
}
