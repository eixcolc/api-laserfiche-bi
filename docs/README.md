# Documentación de APILFBI

| Documento | Para | Contenido |
|---|---|---|
| [openapi-v1.json](openapi-v1.json) | Equipo CRM | Contrato OpenAPI 3.1 de los 15 endpoints, con el esquema OAuth 2.0. Se importa en Postman o en el generador de clientes del CRM |
| [catalogo-codigos.md](catalogo-codigos.md) | Equipo CRM | Criterio 15: todos los códigos de respuesta, su HTTP y cuáles devuelve cada método |
| [contrato-carga-sftp.md](contrato-carga-sftp.md) | Equipo CRM | Criterios 11 a 14: cómo subir documentos por SFTP, el XML y los motivos de rechazo |
| [xsd/expediente-v1.xsd](xsd/expediente-v1.xsd) | Equipo CRM | Esquema del XML de carga, con dos ejemplos válidos |
| [plantilla-laserfiche.md](plantilla-laserfiche.md) | Equipo Laserfiche | Campos de la plantilla, mapeo desde el XML y quién escribe cada campo |
| [workflow-laserfiche.md](workflow-laserfiche.md) | Equipo Laserfiche | Workflow de registro (llamada al stored procedure) y workflow de tarea al ejecutivo |
| [modelo-datos.md](modelo-datos.md) | Desarrollo y DBA | Tablas, stored procedures y roles de la base BILF |
| [prueba-local.md](prueba-local.md) | Desarrollo y QA | Emular la carga por SFTP sin Laserfiche y probar los endpoints |

La colección [APILFBI.Api.http](../src/APILFBI.Api/APILFBI.Api.http) tiene un ejemplo listo de cada método.

`openapi-v1.json` se regenera con la API en ejecución (entorno Development):
`GET http://localhost:5080/openapi/v1.json`.
