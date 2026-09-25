using System.Security.Cryptography;
using APILFBI.Api.Web;
using APILFBI.Application.Comun;
using APILFBI.Infrastructure.Auditoria;
using APILFBI.Infrastructure.Opciones;
using APILFBI.Infrastructure.Seguridad;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace APILFBI.Tests.Unitarias;

public sealed class SeguridadTests
{
    [Fact]
    public void Hash_de_secreto_se_verifica_solo_con_el_secreto_correcto()
    {
        var hash = HashSecreto.Calcular("mi-secreto");

        Assert.StartsWith("PBKDF2-SHA256$", hash);
        Assert.True(HashSecreto.Verificar("mi-secreto", hash));
        Assert.False(HashSecreto.Verificar("otro-secreto", hash));
        Assert.False(HashSecreto.Verificar("mi-secreto", null));
    }

    [Fact]
    public void Dos_hashes_del_mismo_secreto_son_distintos_por_la_sal()
    {
        Assert.NotEqual(HashSecreto.Calcular("x"), HashSecreto.Calcular("x"));
    }

    [Fact]
    public async Task Token_emitido_se_valida_con_la_misma_llave_y_trae_client_id_y_scopes()
    {
        var opciones = new AuthOptions { LlaveFirma = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
        var (token, expira) = new EmisorToken(Options.Create(opciones), TimeProvider.System).Emitir("crm", ["a", "b"]);

        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(token, EmisorToken.ParametrosValidacion(opciones));

        Assert.True(resultado.IsValid);
        Assert.Equal(3600, expira);
        Assert.Equal("crm", resultado.Claims[EmisorToken.ClaimClientId]);
        Assert.Equal("a b", resultado.Claims[EmisorToken.ClaimScope]);
    }

    [Fact]
    public async Task Token_firmado_con_otra_llave_no_es_valido()
    {
        var opciones = new AuthOptions { LlaveFirma = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
        var otra = new AuthOptions { LlaveFirma = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
        var (token, _) = new EmisorToken(Options.Create(opciones), TimeProvider.System).Emitir("crm", ["a"]);

        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(token, EmisorToken.ParametrosValidacion(otra));

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Llave_de_firma_corta_se_rechaza()
    {
        var opciones = new AuthOptions { LlaveFirma = Convert.ToBase64String(new byte[16]) };
        Assert.Throws<InvalidOperationException>(opciones.ObtenerLlave);
    }

    [Theory]
    [InlineData("12345678", "****5678")]
    [InlineData("123", "***")]
    [InlineData(null, null)]
    public void Enmascarar_deja_visibles_solo_los_ultimos_cuatro(string? valor, string? esperado)
    {
        Assert.Equal(esperado, Enmascarar.Valor(valor));
    }

    [Theory]
    [InlineData(1, 200, "Exito")]
    [InlineData(3, 200, "ExitoParcial")]
    [InlineData(103, 400, "ErrorValidacion")]
    [InlineData(201, 401, "NoAutorizado")]
    [InlineData(300, 404, "NoEncontrado")]
    [InlineData(500, 409, "Conflicto")]
    [InlineData(400, 422, "ReglaNegocio")]
    [InlineData(902, 503, "ErrorExterno")]
    [InlineData(900, 500, "ErrorSistema")]
    public void Resultado_de_bitacora_se_deriva_del_http(int codigo, int http, string esperado)
    {
        Assert.Equal(esperado, ResultadosOperacion.DesdeHttp(codigo, http));
    }

    [Theory]
    [InlineData("abc-123_x.y", true)]
    [InlineData("", false)]
    [InlineData("con espacio", false)]
    [InlineData("<script>", false)]
    public void Formato_de_correlation_id(string valor, bool valido)
    {
        Assert.Equal(valido, CorrelationIdMiddleware.EsValido(valor));
    }
}
