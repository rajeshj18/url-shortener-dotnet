using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using UrlShortener.Core.Web;

namespace UrlShortener.Api
{
    /// <summary>
    /// OpenAPI document and Swagger UI (TSK-08 D5). Both are served only when <c>OpenApi:EnableUi</c> is true
    /// (compose sets it locally); by default the API surface isn't published. The document is also committed as
    /// <c>docs/api/openapi-v1.json</c>, and a test fails if the code drifts from it.
    /// </summary>
    public static class OpenApiSetup
    {
        /// <summary>Configuration flag that serves the document and the UI.</summary>
        public const string EnableUiKey = "OpenApi:EnableUi";

        /// <summary>Path of the generated document.</summary>
        public const string DocumentPath = "/swagger/v1/swagger.json";

        /// <summary>Registers document generation from the controllers and their XML comments.</summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public static IServiceCollection AddUrlShortenerOpenApi(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "URL Shortener API",
                    Version = "v1",
                    Description = "Management API: create, read and delete short links, and read their click statistics. Errors are RFC 7807 problems with a traceId and stable error codes.",
                });
                options.AddSecurityDefinition(ApiKeyAuthentication.SchemeName, new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = ApiKeyAuthentication.HeaderName,
                    Description = "The client's API key.",
                });
                options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(ApiKeyAuthentication.SchemeName, document)] = new List<string>(),
                });
                string xmlComments = Path.Join(AppContext.BaseDirectory, "UrlShortener.Api.xml");
                if (File.Exists(xmlComments))
                {
                    options.IncludeXmlComments(xmlComments);
                }
            });
            return services;
        }

        /// <summary>Serves the document and the UI at <c>/swagger</c> when enabled.</summary>
        /// <param name="app">The application.</param>
        /// <returns>The same application, for chaining.</returns>
        public static WebApplication UseUrlShortenerOpenApi(this WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);
            if (app.Configuration.GetValue<bool>(EnableUiKey))
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            return app;
        }
    }
}
