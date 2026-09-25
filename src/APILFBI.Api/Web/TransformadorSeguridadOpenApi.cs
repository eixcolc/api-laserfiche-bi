using APILFBI.Application.Comun;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace APILFBI.Api.Web;

/// <summary>Declara en el contrato OpenAPI el esquema OAuth 2.0 client_credentials.</summary>
internal sealed class TransformadorSeguridadOpenApi(IHttpContextAccessor accesor) : IOpenApiDocumentTransformer
{
    public const string Esquema = "oauth2";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Incluye la subruta de publicación (ej. /expediente) para que Authorize de Swagger funcione.
        var pathBase = accesor.HttpContext?.Request.PathBase.Value ?? string.Empty;

        document.Info.Title = "APILFBI";
        document.Info.Description = "API de gestión documental CRM ↔ Laserfiche 11.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[Esquema] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Description = "OAuth 2.0 client_credentials. El token dura 1 hora.",
            Flows = new OpenApiOAuthFlows
            {
                ClientCredentials = new OpenApiOAuthFlow
                {
                    TokenUrl = new Uri($"{pathBase}/api/v1/auth/token", UriKind.Relative),
                    Scopes = Scopes.Todos.ToDictionary(s => s, s => s),
                },
            },
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(Esquema, document)] = [.. Scopes.Todos],
        });

        return Task.CompletedTask;
    }
}
