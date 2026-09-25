using System.Threading.Channels;
using APILFBI.Application.Abstracciones;
using APILFBI.Infrastructure.Opciones;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APILFBI.Infrastructure.Auditoria;

/// <summary>Cola en memoria de la bitácora. La vacía <see cref="EscritorBitacora"/> en lotes.</summary>
internal sealed class BitacoraService : IBitacoraService
{
    private readonly Channel<EntradaBitacora> _canal;
    private readonly ILogger<BitacoraService> _log;

    public BitacoraService(IOptions<BitacoraOptions> opciones, ILogger<BitacoraService> log)
    {
        _log = log;
        _canal = Channel.CreateBounded<EntradaBitacora>(new BoundedChannelOptions(opciones.Value.Capacidad)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });
    }

    internal ChannelReader<EntradaBitacora> Lector => _canal.Reader;

    public void Encolar(EntradaBitacora entrada)
    {
        if (!_canal.Writer.TryWrite(entrada))
            // Solo pasa si la base lleva mucho tiempo sin responder y la cola se llenó.
            _log.LogError("Cola de bitácora llena: se descartó {TipoOperacion} de {Usuario} ({CorrelationId})",
                entrada.TipoOperacion, entrada.UsuarioServicio, entrada.CorrelationId);
    }
}
