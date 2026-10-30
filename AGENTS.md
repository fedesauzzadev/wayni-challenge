# AGENTS.md

## Reglas de trabajo (obligatorias)

1. **Analizá todo el código antes de escribir.** Antes de escribir o modificar código, explorá el codebase completo (búsqueda de patrones existentes, utilidades compartidas, convenciones y abstracciones ya definidas en `src/` y en los proyectos hermanos). Nunca edites un archivo de forma aislada sin entender el contexto global.

2. **Priorizá reutilización sobre duplicación.** Si existe código que resuelve el problema (aunque esté en otro proyecto/servicio), reutilizalo o extraelo a un lugar compartido (p. ej. `Bcra.Shared.Infrastructure`) en vez de copiarlo. Antes de crear algo nuevo, verificá que no exista. Si detectás duplicación al tocar un área, señalala y proponé su deduplicación.

## Verificación

- Build sin warnings: `dotnet build`.
- Tests unitarios y de integración antes de dar por terminada una tarea.
