using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using APILFBI.Application.Abstracciones;
using APILFBI.Application.Comun;
using APILFBI.Infrastructure.Opciones;
using APILFBI.Infrastructure.Seguridad;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace APILFBI.Api.Web;

public static class ServiciosWeb
{
    public const string PoliticaToken = "token";

    public static IServiceCollection AddApiWeb(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IContextoUsuario, ContextoUsuarioHttp>();
        services.AddSingleton<FabricaRespuestas>();
        services.AddExceptionHandler<ManejadorExcepciones>();
        services.AddProblemDetails();

        // Los campos obligatorios los valida FluentValidation con los códigos del catálogo,
        // no el [Required] implícito de MVC.
        services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
            .ConfigureApiBehaviorOptions(o =>
            o.InvalidModelStateResponseFactory = ctx =>
            {
                var errores = ctx.ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .SelectMany(e => e.Value!.Errors.Select(err => new ErrorCampo(
                        string.IsNullOrEmpty(e.Key) ? "body" : char.ToLowerInvariant(e.Key[0]) + e.Key[1..],
                        string.IsNullOrEmpty(err.ErrorMessage) ? "Valor inválido." : err.ErrorMessage)))
                    .ToList();
                var fabrica = ctx.HttpContext.RequestServices.GetRequiredService<FabricaRespuestas>();
                return fabrica.Error(ctx.HttpContext, CodigosRespuesta.SolicitudInvalida, errores: errores);
            });

        // --- Autenticación JWT (los tokens los emite esta misma API) ---
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>, FabricaRespuestas>((jwt, auth, fabrica) =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = EmisorToken.ParametrosValidacion(auth.Value);
                jwt.Events = new JwtBearerEvents
                {
                    OnChallenge = async c =>
                    {
                        c.HandleResponse();
                        c.Response.Headers.WWWAuthenticate = "Bearer";
                        await fabrica.EscribirErrorAsync(c.HttpContext, CodigosRespuesta.TokenInvalido);
                    },
                    OnForbidden = c => fabrica.EscribirErrorAsync(c.HttpContext, CodigosRespuesta.SinPermiso),
                };
            });

        // --- Autorización: todo exige token, y cada scope es una política ---
        services.AddAuthorization(o =>
        {
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            foreach (var scope in Scopes.Todos)
                o.AddPolicy(scope, p => p.RequireAuthenticatedUser().RequireAssertion(c => TieneScope(c.User, scope)));
        });

        // --- Rate limiting en memoria por instancia (el límite global va en el balanceador) ---
        var permisos = config.GetValue("RateLimit:PermisosPorMinuto", 600);
        var permisosToken = config.GetValue("RateLimit:TokenPorMinuto", 10);
        services.AddRateLimiter(o =>
        {
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ctx.User.FindFirst(EmisorToken.ClaimClientId)?.Value ?? "ip:" + ctx.Connection.RemoteIpAddress,
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = permisos, Window = TimeSpan.FromMinutes(1) }));

            // El endpoint de token se limita por IP para frenar ataques de fuerza bruta.
            o.AddPolicy(PoliticaToken, ctx => RateLimitPartition.GetFixedWindowLimiter(
                "token:" + ctx.Connection.RemoteIpAddress,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permisosToken, Window = TimeSpan.FromMinutes(1) }));

            o.OnRejected = async (c, ct) =>
            {
                if (c.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                    c.HttpContext.Response.Headers.RetryAfter = ((int)espera.TotalSeconds).ToString();
                var fabrica = c.HttpContext.RequestServices.GetRequiredService<FabricaRespuestas>();
                await fabrica.EscribirErrorAsync(c.HttpContext, CodigosRespuesta.LimiteSolicitudes);
            };
        });

        // --- Detrás del balanceador: IP real y esquema original ---
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var ip in config.GetSection("Proxy:ProxiesConocidos").Get<string[]>() ?? [])
                o.KnownProxies.Add(IPAddress.Parse(ip));
        });

        services.AddOpenApi(o => o.AddDocumentTransformer<TransformadorSeguridadOpenApi>());

        return services;
    }

    public static bool TieneScope(ClaimsPrincipal usuario, string scope) =>
        usuario.FindFirst(EmisorToken.ClaimScope)?.Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(scope, StringComparer.Ordinal) == true;
}
