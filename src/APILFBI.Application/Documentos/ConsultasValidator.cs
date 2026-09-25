using APILFBI.Application.Comun;
using FluentValidation;

namespace APILFBI.Application.Documentos;

/// <summary>
/// Criterio 7: idTipoExpediente y noCaso obligatorios; además CIF (vinculado) o tipo + número de
/// identificación (potencial).
/// </summary>
public sealed class ConsultaNaturalValidator : AbstractValidator<ConsultaNatural>
{
    public ConsultaNaturalValidator()
    {
        var obligatorio = CodigosRespuesta.CampoObligatorio.ToString(System.Globalization.CultureInfo.InvariantCulture);
        RuleFor(x => x.IdTipoExpediente).GreaterThan(0).WithErrorCode(obligatorio).WithMessage("idTipoExpediente es obligatorio.");
        RuleFor(x => x.NoCaso).NotEmpty().WithErrorCode(obligatorio).WithMessage("noCaso es obligatorio.");
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Cif)
                       || (!string.IsNullOrWhiteSpace(x.TipoIdentificacion) && !string.IsNullOrWhiteSpace(x.NoIdentificacion)))
            .OverridePropertyName("cif")
            .WithErrorCode(obligatorio)
            .WithMessage("Envíe cif (cliente vinculado) o tipoIdentificacion y noIdentificacion (cliente potencial).");
    }
}

/// <summary>Criterio 8: idTipoExpediente y noCaso obligatorios; además CIF (vinculado) o RTN (potencial).</summary>
public sealed class ConsultaJuridicaValidator : AbstractValidator<ConsultaJuridica>
{
    public ConsultaJuridicaValidator()
    {
        var obligatorio = CodigosRespuesta.CampoObligatorio.ToString(System.Globalization.CultureInfo.InvariantCulture);
        RuleFor(x => x.IdTipoExpediente).GreaterThan(0).WithErrorCode(obligatorio).WithMessage("idTipoExpediente es obligatorio.");
        RuleFor(x => x.NoCaso).NotEmpty().WithErrorCode(obligatorio).WithMessage("noCaso es obligatorio.");
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Cif) || !string.IsNullOrWhiteSpace(x.Rtn))
            .OverridePropertyName("cif")
            .WithErrorCode(obligatorio)
            .WithMessage("Envíe cif (cliente vinculado) o rtn (cliente potencial).");
    }
}
