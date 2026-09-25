using APILFBI.Application.Comun;
using FluentValidation;

namespace APILFBI.Application.Expedientes;

/// <summary>
/// Validación de forma. Las reglas de negocio de las llaves (obligatorias, grupos, catálogos) las
/// valida trx.usp_ObtenerOCrearExpediente contra cat.TipoExpedienteLlave. El ErrorCode es el código
/// de cat.CodigoRespuesta.
/// </summary>
public sealed class SolicitudExpedienteValidator : AbstractValidator<SolicitudExpediente>
{
    public const int MaximoLlaves = 30;

    public SolicitudExpedienteValidator()
    {
        RuleFor(x => x.IdTipoExpediente)
            .GreaterThan(0).WithErrorCode(Codigo(CodigosRespuesta.CampoObligatorio))
            .WithMessage("idTipoExpediente es obligatorio.");

        RuleFor(x => x.Llaves)
            .NotEmpty().WithErrorCode(Codigo(CodigosRespuesta.CampoObligatorio))
            .WithMessage("Debe enviar las llaves del expediente.")
            .Must(l => l is null || l.Count <= MaximoLlaves).WithErrorCode(Codigo(CodigosRespuesta.FormatoCampoInvalido))
            .WithMessage($"No se admiten más de {MaximoLlaves} llaves.");

        RuleForEach(x => x.Llaves).ChildRules(llave =>
        {
            llave.RuleFor(l => l.Tipo)
                .NotEmpty().WithErrorCode(Codigo(CodigosRespuesta.CampoObligatorio)).WithMessage("tipo es obligatorio.")
                .MaximumLength(50).WithErrorCode(Codigo(CodigosRespuesta.FormatoCampoInvalido)).WithMessage("tipo admite 50 caracteres.");
            llave.RuleFor(l => l.Valor)
                .MaximumLength(250).WithErrorCode(Codigo(CodigosRespuesta.FormatoCampoInvalido)).WithMessage("valor admite 250 caracteres.");
        });
    }

    private static string Codigo(int codigo) => codigo.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
