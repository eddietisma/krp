# AGENTS.md

## Code reviews
Review the provided code for readability, maintainability, correctness, and adherence to project standards. Suggest improvements and highlight potential issues.

## Code style
Follow the repository's `.editorconfig`. Key points include:

- Use four spaces for indentation.
- Use CRLF line endings and do not add a trailing newline.
- Use file-scoped namespaces.
- Place open braces on their own line and always include braces for statement blocks.
- Declare local variables with `var` when the type is obvious.
- Use PascalCase for public members and types.
- Use underscore-prefixed camelCase for private or internal fields.
- Use ALL_CAPS with underscores for constants.
- Prefix interfaces with `I`.
- Do not qualify member access with `this` unless required.
- Do not sort or group `using` directives.
- Surround binary operators with a single space and omit spaces inside parentheses.
- Use expression-bodied members for properties, accessors, and indexers when they fit on one line.
- Prefer object and collection initializers.
- Prefer pattern matching and `switch` expressions for clarity.
- Start comments with a capital letter and end them with a period.

## Testing
- Run `dotnet test` to validate changes.

## Release notes generation
- Group entries under the headings Features, Fixes, Documentation, and Maintenance, following Docker Buildx style.
- Each bullet must end with a period.
- Use the imperative mood for entries (e.g., "fix bug" not "fixed bug").
- When a pull request number is available, link it in the format "(#123)"
    - Look into the PR description and changes.
- Summaries should only describe commits since the previous release tag.
- Keep wording short and concise.
- Under Maintenance, list all non-infra NuGet package version updates in the format: "PackageName: x.y.z → a.b.c".