using APILFBI.Application.Abstracciones;
using APILFBI.Infrastructure.Seguridad;

namespace APILFBI.Api.Web;

internal sealed class ContextoUsuarioHttp(IHttpContextAccessor accesor) : IContextoUsuario
{
    public const string HeaderUsuarioOperacion = "X-Operation-User";
    private const int LargoMaximo = 100;

    private HttpContext? Ctx => accesor.HttpContext;

    public string UsuarioServicio => Ctx?.User.FindFirst(EmisorToken.ClaimClientId)?.Value ?? "(anonimo)";

    public string? UsuarioOperacion
    {
        get
        {
            var valor = Ctx?.Request.Headers[HeaderUsuarioOperacion].ToString().Trim();
            if (string.IsNullOrEmpty(valor)) return null;
            return valor.Length > LargoMaximo ? valor[..LargoMaximo] : valor;
        }
    }

    public IReadOnlyCollection<string> Scopes =>
        Ctx?.User.FindFirst(EmisorToken.ClaimScope)?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

    public string? CorrelationId => Ctx?.TraceIdentifier;

    public string? IpOrigen => Ctx?.Connection.RemoteIpAddress?.ToString();
}
