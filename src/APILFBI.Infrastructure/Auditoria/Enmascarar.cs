namespace APILFBI.Infrastructure.Auditoria;

/// <summary>Enmascara datos personales (CIF, DNI, RTN) igual que aud.fn_Enmascarar.</summary>
public static class Enmascarar
{
    public static string? Valor(string? valor) => valor switch
    {
        null => null,
        { Length: <= 4 } => new string('*', valor.Length),
        _ => new string('*', valor.Length - 4) + valor[^4..],
    };
}
